using Barotrauma;
using Barotrauma.LuaCs;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace InGameCharacterCustomizer;

public sealed class InGameCharacterCustomizerServer : IAssemblyPlugin
{
    private const string ApplyMessage = "InGameCharacterCustomizer.Apply";
    private const string SyncMessage = "InGameCharacterCustomizer.Sync";
    private const string PermissionRequestMessage = "InGameCharacterCustomizer.PermissionRequest";
    private const string PermissionSyncMessage = "InGameCharacterCustomizer.PermissionSync";
    private const string SetPermissionCommand = "icc_setpermission";
    private const string GetPermissionCommand = "icc_getpermission";
    private const string ReloadPermissionsCommand = "icc_reloadpermissions";
    private const string ListPermissionsCommand = "icc_listpermissions";

    private static CustomizePermissionStore permissionStore;

    public void PreInitPatching()
    {
    }

    public void Initialize()
    {
        permissionStore = new CustomizePermissionStore();
        permissionStore.Load();

        LuaCsSetup.Instance.Networking.Receive(ApplyMessage, ReadClientAppearance);
        LuaCsSetup.Instance.Networking.Receive(PermissionRequestMessage, ReadPermissionRequest);

        RegisterCommand(SetPermissionCommand, "icc_setpermission <client> <AnyCrew|CurrentCrew|None>");
        RegisterCommand(GetPermissionCommand, "icc_getpermission <client>");
        RegisterCommand(ReloadPermissionsCommand, "icc_reloadpermissions");
        RegisterCommand(ListPermissionsCommand, "icc_listpermissions");

        DebugConsole.AssignOnClientRequestExecute(SetPermissionCommand, HandleSetPermissionClientRequest);
        DebugConsole.AssignOnClientRequestExecute(GetPermissionCommand, HandleGetPermissionClientRequest);
        DebugConsole.AssignOnClientRequestExecute(ReloadPermissionsCommand, HandleReloadPermissionsClientRequest);
        DebugConsole.AssignOnClientRequestExecute(ListPermissionsCommand, HandleListPermissionsClientRequest);

        LuaCsLogger.Log("InGameCharacterCustomizer server loaded.");
    }

    public void OnLoadCompleted()
    {
    }

    public void Dispose()
    {
        LuaCsSetup.Instance.Game.RemoveCommand(SetPermissionCommand);
        LuaCsSetup.Instance.Game.RemoveCommand(GetPermissionCommand);
        LuaCsSetup.Instance.Game.RemoveCommand(ReloadPermissionsCommand);
        LuaCsSetup.Instance.Game.RemoveCommand(ListPermissionsCommand);
        permissionStore = null;
        LuaCsLogger.Log("InGameCharacterCustomizer server disposed.");
    }

    private static void ReadClientAppearance(IReadMessage message, Client sender)
    {
        AppearancePayload requested = AppearancePayload.Read(message);
        Character target = FindCharacter(requested.CharacterId);
        if (!CanCustomize(sender, target))
        {
            SendAuthoritativeAppearance(sender, target);
            return;
        }

        string requestedName = Client.SanitizeName(requested.Name ?? string.Empty);
        string currentName = target.Info.Name;
        if (string.IsNullOrWhiteSpace(requestedName) ||
            (requestedName != currentName &&
             !GameMain.Server.IsNameValid(sender, requestedName, clientRenamingSelf: target == sender.Character)))
        {
            requestedName = currentName;
        }

        requested = requested.WithName(requestedName);
        AppearancePayload validated = requested.ApplyValidatedTo(target);
        PersistCampaignAppearance(target, validated);

        IWriteMessage response = LuaCsSetup.Instance.Networking.Start(SyncMessage);
        validated.Write(response);
        LuaCsSetup.Instance.Networking.SendToClient(response, deliveryMethod: DeliveryMethod.Reliable);
    }

    private static void ReadPermissionRequest(IReadMessage message, Client sender)
    {
        SendPermission(sender);
    }

    private static bool CanCustomize(Client sender, Character target)
    {
        if (sender?.Character?.Info?.Head == null || sender.Character.IsDead ||
            target?.Info?.Head == null || target.IsDead)
        {
            return false;
        }

        return permissionStore?.GetMode(sender) switch
        {
            CustomizePermissionMode.AnyCrew => GameSession.GetSessionCrewCharacters(CharacterType.Both).Contains(target),
            CustomizePermissionMode.CurrentCrew => target == sender.Character,
            _ => false
        };
    }

    private static Character FindCharacter(ushort characterId)
    {
        return Character.CharacterList.FirstOrDefault(character => character.ID == characterId);
    }

    private static Client FindClient(string argument)
    {
        if (GameMain.Server?.ConnectedClients == null || string.IsNullOrWhiteSpace(argument)) { return null; }

        Client client = GameMain.Server.ConnectedClients.Find(c => Homoglyphs.Compare(c.Name, argument));
        if (byte.TryParse(argument, out byte sessionId))
        {
            client ??= GameMain.Server.ConnectedClients.Find(c => c.SessionId == sessionId);
        }
        if (Address.Parse(argument).TryUnwrap(out Address address))
        {
            client ??= GameMain.Server.ConnectedClients.Find(c => c.AddressMatches(address));
        }
        if (AccountId.Parse(argument).TryUnwrap(out AccountId accountId))
        {
            client ??= GameMain.Server.ConnectedClients.Find(c => c.AccountId.ValueEquals(accountId));
        }
        return client;
    }

    private static Client FindCharacterOwner(Character target)
    {
        return target == null
            ? null
            : GameMain.Server?.ConnectedClients.FirstOrDefault(client =>
                client.Character == target || client.CharacterInfo == target.Info);
    }

    private static void PersistCampaignAppearance(Character target, AppearancePayload validated)
    {
        if (GameMain.GameSession?.Campaign is not MultiPlayerCampaign campaign) { return; }

        Client targetOwner = FindCharacterOwner(target);
        CharacterCampaignData targetData = campaign.GetCharacterData(target.Info);
        CharacterCampaignData ownerData = targetOwner == null ? null : campaign.GetClientCharacterData(targetOwner);

        var campaignInfos = new System.Collections.Generic.HashSet<CharacterInfo>();
        AddCampaignInfo(target.Info);
        AddCampaignInfo(targetOwner?.CharacterInfo);
        AddCampaignInfo(targetData?.CharacterInfo);
        AddCampaignInfo(ownerData?.CharacterInfo);

        void AddCampaignInfo(CharacterInfo info)
        {
            if (info != null && info.Head != null && info != target.Info)
            {
                campaignInfos.Add(info);
            }
        }

        foreach (CharacterInfo campaignInfo in campaignInfos)
        {
            validated.ValidateFor(campaignInfo).ApplyTo(campaignInfo);
        }

        campaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.CharacterInfo);
    }

    private static void SendAuthoritativeAppearance(Client recipient, Character target)
    {
        if (recipient?.Connection == null || target?.Info?.Head == null) { return; }

        IWriteMessage response = LuaCsSetup.Instance.Networking.Start(SyncMessage);
        AppearancePayload.FromCharacter(target).Write(response);
        LuaCsSetup.Instance.Networking.SendToClient(response, recipient.Connection, DeliveryMethod.Reliable);
    }

    private static void SendPermission(Client recipient)
    {
        if (recipient?.Connection == null || permissionStore == null) { return; }

        IWriteMessage response = LuaCsSetup.Instance.Networking.Start(PermissionSyncMessage);
        response.WriteByte((byte)permissionStore.GetMode(recipient));
        LuaCsSetup.Instance.Networking.SendToClient(response, recipient.Connection, DeliveryMethod.Reliable);
    }

    private static void RegisterCommand(string commandName, string help)
    {
        LuaCsSetup.Instance.Game.AddCommand(commandName, help, args =>
        {
            string[] commandArgs = args.Length > 0 && args[0] is string[] parsedArgs
                ? parsedArgs
                : Array.Empty<string>();
            ExecuteCommand(commandName, commandArgs, null);
        });
    }

    private static void HandleSetPermissionClientRequest(Client sender, Vector2 cursorPosition, string[] args)
        => ExecuteCommand(SetPermissionCommand, args, sender);

    private static void HandleGetPermissionClientRequest(Client sender, Vector2 cursorPosition, string[] args)
        => ExecuteCommand(GetPermissionCommand, args, sender);

    private static void HandleReloadPermissionsClientRequest(Client sender, Vector2 cursorPosition, string[] args)
        => ExecuteCommand(ReloadPermissionsCommand, args, sender);

    private static void HandleListPermissionsClientRequest(Client sender, Vector2 cursorPosition, string[] args)
        => ExecuteCommand(ListPermissionsCommand, args, sender);

    private static void ExecuteCommand(string commandName, string[] args, Client sender)
    {
        if (!IsPermissionAdmin(sender))
        {
            SendCommandMessage(sender, "You need ManagePermissions to use InGameCharacterCustomizer permission commands.", Color.Red);
            return;
        }

        switch (commandName)
        {
            case SetPermissionCommand:
                SetPermission(args, sender);
                break;
            case GetPermissionCommand:
                GetPermission(args, sender);
                break;
            case ReloadPermissionsCommand:
                ReloadPermissions(args, sender);
                break;
            case ListPermissionsCommand:
                ListPermissions(args, sender);
                break;
        }
    }

    private static bool IsPermissionAdmin(Client client)
    {
        return client == null || client.Connection == GameMain.Server?.OwnerConnection ||
               client.HasPermission(ClientPermissions.ManagePermissions);
    }

    private static void SetPermission(string[] args, Client sender)
    {
        if (args.Length != 2 || !TryParsePermissionMode(args[1], out CustomizePermissionMode mode))
        {
            SendCommandMessage(sender, "Usage: icc_setpermission <client> <AnyCrew|CurrentCrew|None>", Color.Yellow);
            return;
        }

        Client target = FindClient(args[0]);
        if (target == null)
        {
            SendCommandMessage(sender, $"Client \"{args[0]}\" was not found.", Color.Red);
            return;
        }

        permissionStore.SetMode(target, mode);
        SendPermission(target);
        SendCommandMessage(sender, $"Set {target.Name}'s customize permission to {mode}.", Color.White);
    }

    private static void GetPermission(string[] args, Client sender)
    {
        if (args.Length != 1)
        {
            SendCommandMessage(sender, "Usage: icc_getpermission <client>", Color.Yellow);
            return;
        }

        Client target = FindClient(args[0]);
        if (target == null)
        {
            SendCommandMessage(sender, $"Client \"{args[0]}\" was not found.", Color.Red);
            return;
        }

        SendCommandMessage(sender, $"{target.Name}'s customize permission is {permissionStore.GetMode(target)}.", Color.White);
    }

    private static void ReloadPermissions(string[] args, Client sender)
    {
        if (args.Length != 0)
        {
            SendCommandMessage(sender, "Usage: icc_reloadpermissions", Color.Yellow);
            return;
        }

        permissionStore.Load();
        foreach (Client client in GameMain.Server.ConnectedClients)
        {
            SendPermission(client);
        }
        SendCommandMessage(sender, "Reloaded InGameCharacterCustomizer permissions.", Color.White);
    }

    private static void ListPermissions(string[] args, Client sender)
    {
        if (args.Length != 0)
        {
            SendCommandMessage(sender, "Usage: icc_listpermissions", Color.Yellow);
            return;
        }

        foreach (Client client in GameMain.Server.ConnectedClients)
        {
            SendCommandMessage(sender, $"{client.SessionId}: {client.Name} = {permissionStore.GetMode(client)}", Color.White);
        }
    }

    private static bool TryParsePermissionMode(string value, out CustomizePermissionMode mode)
    {
        return Enum.TryParse(value, ignoreCase: true, out mode) &&
               Enum.IsDefined(typeof(CustomizePermissionMode), mode);
    }

    private static void SendCommandMessage(Client recipient, string message, Color color)
    {
        if (recipient == null)
        {
            DebugConsole.NewMessage(message, color);
        }
        else
        {
            GameMain.Server.SendConsoleMessage(message, recipient, color);
        }
    }
}
