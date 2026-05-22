using Barotrauma;
using Barotrauma.LuaCs;
using Barotrauma.Networking;

namespace InGameCharacterCustomizer;

public sealed class InGameCharacterCustomizerServer : IAssemblyPlugin
{
    private const string ApplyMessage = "InGameCharacterCustomizer.Apply";
    private const string SyncMessage = "InGameCharacterCustomizer.Sync";

    public void PreInitPatching()
    {
    }

    public void Initialize()
    {
        LuaCsSetup.Instance.Networking.Receive(ApplyMessage, ReadClientAppearance);
        LuaCsLogger.Log("InGameCharacterCustomizer server loaded.");
    }

    public void OnLoadCompleted()
    {
    }

    public void Dispose()
    {
        LuaCsLogger.Log("InGameCharacterCustomizer server disposed.");
    }

    private static void ReadClientAppearance(IReadMessage message, Client sender)
    {
        if (sender?.Character?.Info?.Head == null || sender.Character.IsDead) { return; }

        AppearancePayload requested = AppearancePayload.Read(message);
        if (requested.CharacterId != sender.Character.ID) { return; }

        AppearancePayload validated = requested.ApplyValidatedTo(sender.Character);
        if (sender.CharacterInfo != sender.Character.Info)
        {
            validated.ApplyTo(sender.CharacterInfo);
        }
        PersistCampaignAppearance(sender, validated);

        IWriteMessage response = LuaCsSetup.Instance.Networking.Start(SyncMessage);
        validated.Write(response);
        LuaCsSetup.Instance.Networking.SendToClient(response, deliveryMethod: DeliveryMethod.Reliable);
    }

    private static void PersistCampaignAppearance(Client sender, AppearancePayload validated)
    {
        if (GameMain.GameSession?.Campaign is not MultiPlayerCampaign campaign) { return; }

        CharacterCampaignData characterData = campaign.GetClientCharacterData(sender);
        if (characterData?.CharacterInfo?.Head == null) { return; }

        if (characterData.CharacterInfo != sender.Character.Info &&
            characterData.CharacterInfo != sender.CharacterInfo)
        {
            validated.ApplyTo(characterData.CharacterInfo);
        }

        campaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.CharacterInfo);
    }
}
