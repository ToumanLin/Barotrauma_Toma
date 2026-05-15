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

        IWriteMessage response = LuaCsSetup.Instance.Networking.Start(SyncMessage);
        validated.Write(response);
        LuaCsSetup.Instance.Networking.SendToClient(response, deliveryMethod: DeliveryMethod.Reliable);
    }
}
