using Barotrauma;
using Barotrauma.LuaCs;
using Barotrauma.Networking;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;
using System.Reflection;

namespace InGameCharacterCustomizer;

public sealed class InGameCharacterCustomizerClient : IAssemblyPlugin
{
    private const string ApplyMessage = "InGameCharacterCustomizer.Apply";
    private const string SyncMessage = "InGameCharacterCustomizer.Sync";
    private const string CustomizeButtonUserData = "InGameCharacterCustomizer.CustomizeButton";

    private static InGameCharacterCustomizerClient instance;

    private Harmony harmony;
    private CharacterInfo.AppearanceCustomizationMenu customizationMenu;
    private GUIFrame customizationRoot;
    private AppearancePayload originalAppearance;
    private AppearancePayload savedAppearance;
    private Character customizedCharacter;
    private int savedCampaignRoundId = -1;
    private ushort lastAppliedSavedAppearanceCharacterId;
    private bool hasSavedAppearance;

    public void PreInitPatching()
    {
    }

    public void Initialize()
    {
        instance = this;
        harmony = new Harmony("InGameCharacterCustomizer.Client");

        Patch("Barotrauma.CharacterInfo", "CreateInfoFrame", postfix: nameof(CreateInfoFramePostfix));
        Patch("Barotrauma.GameScreen", "AddToGUIUpdateList", postfix: nameof(GameScreenAddToGUIUpdateListPostfix));

        LuaCsSetup.Instance.Networking.Receive(SyncMessage, ReadServerAppearance);
        LuaCsLogger.Log("InGameCharacterCustomizer client loaded.");
    }

    public void OnLoadCompleted()
    {
    }

    public void Dispose()
    {
        CloseCustomizationWindow(revert: false);
        harmony?.UnpatchSelf();
        harmony = null;
        if (instance == this)
        {
            instance = null;
        }
        LuaCsLogger.Log("InGameCharacterCustomizer client disposed.");
    }

    private void Patch(string typeName, string methodName, string postfix)
    {
        Type targetType = AccessTools.TypeByName(typeName);
        MethodInfo target = targetType == null ? null : AccessTools.Method(targetType, methodName);
        MethodInfo postfixMethod = AccessTools.Method(typeof(InGameCharacterCustomizerClient), postfix);
        if (target == null || postfixMethod == null)
        {
            LuaCsLogger.LogError($"InGameCharacterCustomizer could not patch {typeName}.{methodName}.");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(postfixMethod));
    }

    private static void CreateInfoFramePostfix(CharacterInfo __instance, GUIComponent __result)
    {
        instance?.TryAddCustomizeButton(__instance, __result);
    }

    private static void GameScreenAddToGUIUpdateListPostfix()
    {
        instance?.UpdateInGameCustomization();
    }

    private void TryAddCustomizeButton(CharacterInfo info, GUIComponent infoFrame)
    {
        if (infoFrame == null || info?.Character == null) { return; }
        if (!GameSession.IsTabMenuOpen || TabMenu.SelectedTab != TabMenu.InfoFrameTab.Crew) { return; }
        if (info.Character != Character.Controlled) { return; }
        if (infoFrame.FindChild(CustomizeButtonUserData, recursive: true) != null) { return; }

        GUIComponent portrait = infoFrame.GetAllChildren<GUICustomComponent>().FirstOrDefault();
        if (portrait == null) { return; }

        var button = new GUIButton(
            new RectTransform(new Vector2(0.45f, 0.24f), portrait.RectTransform, Anchor.TopLeft, scaleBasis: ScaleBasis.BothWidth)
            {
                MinSize = new Point(74, 20),
                MaxSize = new Point(118, 28),
                AbsoluteOffset = new Point(2, 2)
            },
            "Customize",
            style: "GUIButtonSmall")
        {
            UserData = CustomizeButtonUserData,
            IgnoreLayoutGroups = true,
            ToolTip = "Customize character",
            OnClicked = (_, _) =>
            {
                OpenCustomizationWindow(info.Character);
                return true;
            }
        };

        button.TextBlock.AutoScaleHorizontal = true;
    }

    private void OpenCustomizationWindow(Character character)
    {
        if (character?.Info?.Head == null) { return; }

        CloseCustomizationWindow(revert: false);
        customizedCharacter = character;
        originalAppearance = AppearancePayload.FromCharacter(character);

        customizationRoot = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null, color: Color.Black * 0.6f)
        {
            CanBeFocused = true
        };

        var window = new GUIFrame(new RectTransform(new Vector2(0.72f, 0.72f), customizationRoot.RectTransform, Anchor.Center)
        {
            MinSize = new Point(560, 500)
        }, style: "GUIFrame")
        {
            CanBeFocused = true
        };

        var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.94f, 0.86f), window.RectTransform, Anchor.TopCenter)
        {
            RelativeOffset = new Vector2(0.0f, 0.04f)
        })
        {
            Stretch = true,
            AbsoluteSpacing = 8
        };

        new GUITextBlock(
            new RectTransform(new Vector2(1.0f, 0.0f), layout.RectTransform),
            "Customize Character",
            font: GUIStyle.SubHeadingFont,
            textAlignment: Alignment.Center);

        var menuHost = new UpdatingFrame(
            new RectTransform(new Vector2(1.0f, 0.84f), layout.RectTransform),
            _ =>
            {
                if (PlayerInput.KeyHit(Keys.Escape))
                {
                    CloseCustomizationWindow(revert: true);
                    return;
                }

                customizationMenu?.Update();
            },
            style: "GUIFrameListBox");

        var buttonRow = new GUILayoutGroup(new RectTransform(new Vector2(0.56f, 0.07f), window.RectTransform, Anchor.BottomCenter)
        {
            RelativeOffset = new Vector2(0.0f, -0.04f),
            MinSize = new Point(280, 32),
            MaxSize = new Point(520, 42)
        }, isHorizontal: true, childAnchor: Anchor.Center)
        {
            Stretch = true,
            RelativeSpacing = 0.06f,
            IgnoreLayoutGroups = true
        };

        new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonRow.RectTransform), "Save", style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                SaveAppearance();
                return true;
            }
        };

        new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonRow.RectTransform), "Revert", style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                CloseCustomizationWindow(revert: true);
                return true;
            }
        };

        customizationMenu = new CharacterInfo.AppearanceCustomizationMenu(character.Info, menuHost);
    }

    private void UpdateInGameCustomization()
    {
        TryApplySavedAppearanceToControlledCharacter();
        AddCustomizerToGuiUpdateList();
    }

    private void AddCustomizerToGuiUpdateList()
    {
        customizationRoot?.AddToGUIUpdateList(ignoreChildren: false, order: 10);

        GUIListBox headSelectionList = customizationMenu?.HeadSelectionList;
        if (headSelectionList is { Visible: true })
        {
            AddPopupListToGuiUpdateList(headSelectionList, order: 20);
        }

        if (customizationRoot == null) { return; }
        foreach (GUIDropDown dropdown in customizationRoot.GetAllChildren<GUIDropDown>())
        {
            if (dropdown.Dropped)
            {
                AddPopupListToGuiUpdateList(dropdown.ListBox, order: 30);
            }
        }
    }

    private void SaveAppearance()
    {
        if (customizedCharacter?.Info?.Head == null) { return; }

        AppearancePayload payload = AppearancePayload.FromCharacter(customizedCharacter);
        payload.ApplyTo(customizedCharacter);
        savedAppearance = payload;
        savedCampaignRoundId = GetCampaignRoundId();
        lastAppliedSavedAppearanceCharacterId = payload.CharacterId;
        hasSavedAppearance = true;
        SaveLocalPreferences(payload);

        SendAppearanceToServer(payload);

        CloseCustomizationWindow(revert: false);
    }

    private void TryApplySavedAppearanceToControlledCharacter()
    {
        Character controlled = Character.Controlled;
        int campaignRoundId = GetCampaignRoundId();
        if (!hasSavedAppearance ||
            campaignRoundId < 0 ||
            campaignRoundId == savedCampaignRoundId ||
            controlled?.Info?.Head == null ||
            controlled.IsDead ||
            controlled.ID == lastAppliedSavedAppearanceCharacterId)
        {
            return;
        }

        AppearancePayload payload = savedAppearance.WithCharacterId(controlled.ID);
        payload.ApplyTo(controlled);
        savedAppearance = payload;
        savedCampaignRoundId = campaignRoundId;
        lastAppliedSavedAppearanceCharacterId = controlled.ID;
        SaveLocalPreferences(payload);
        SendAppearanceToServer(payload);
    }

    private static void SendAppearanceToServer(AppearancePayload payload)
    {
        if (GameMain.Client == null) { return; }

        IWriteMessage message = LuaCsSetup.Instance.Networking.Start(ApplyMessage);
        payload.Write(message);
        LuaCsSetup.Instance.Networking.SendToServer(message, DeliveryMethod.Reliable);
    }

    private static int GetCampaignRoundId()
    {
        return GameMain.GameSession?.Campaign is MultiPlayerCampaign campaign ? campaign.RoundID : -1;
    }

    private static void SaveLocalPreferences(AppearancePayload payload)
    {
        MultiplayerPreferences preferences = MultiplayerPreferences.Instance;
        if (preferences == null) { return; }

        preferences.TagSet.Clear();
        preferences.TagSet.UnionWith(payload.Tags);
        preferences.HairIndex = payload.HairIndex;
        preferences.BeardIndex = payload.BeardIndex;
        preferences.MoustacheIndex = payload.MoustacheIndex;
        preferences.FaceAttachmentIndex = payload.FaceAttachmentIndex;
        preferences.SkinColor = payload.SkinColor;
        preferences.HairColor = payload.HairColor;
        preferences.FacialHairColor = payload.FacialHairColor;
        GameSettings.SaveCurrentConfig();
    }

    private void CloseCustomizationWindow(bool revert)
    {
        if (revert)
        {
            originalAppearance.ApplyTo(customizedCharacter);
        }

        customizationMenu?.Dispose();
        customizationMenu = null;
        customizationRoot?.RemoveFromGUIUpdateList();
        if (customizationRoot != null)
        {
            customizationRoot.RectTransform.Parent = null;
            customizationRoot = null;
        }
        customizedCharacter = null;
    }

    private static void ReadServerAppearance(IReadMessage message)
    {
        AppearancePayload payload = AppearancePayload.Read(message);
        Character character = Character.CharacterList.FirstOrDefault(c => c.ID == payload.CharacterId);
        payload.ApplyTo(character);
        instance?.RememberServerAppearance(character, payload);
    }

    private void RememberServerAppearance(Character character, AppearancePayload payload)
    {
        if (character == null || character != Character.Controlled) { return; }

        savedAppearance = payload;
        savedCampaignRoundId = GetCampaignRoundId();
        lastAppliedSavedAppearanceCharacterId = payload.CharacterId;
        hasSavedAppearance = true;
        SaveLocalPreferences(payload);
    }

    private static void AddPopupListToGuiUpdateList(GUIListBox listBox, int order)
    {
        RectTransform parent = listBox.RectTransform.Parent;
        if (parent?.Children.Contains(listBox.RectTransform) == true)
        {
            listBox.SetAsLastChild();
        }
        listBox.AddToGUIUpdateList(ignoreChildren: false, order: order);
    }

    private sealed class UpdatingFrame : GUIFrame
    {
        private readonly Action<float> onUpdate;

        public UpdatingFrame(RectTransform rectT, Action<float> onUpdate, string style = "", Color? color = null)
            : base(rectT, style, color)
        {
            this.onUpdate = onUpdate;
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            onUpdate?.Invoke(deltaTime);
        }
    }
}
