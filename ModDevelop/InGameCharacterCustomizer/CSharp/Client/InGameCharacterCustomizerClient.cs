using Barotrauma;
using Barotrauma.LuaCs;
using Barotrauma.Networking;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
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
    private GUIComponent customizationMenuHost;
    private GUITextBox characterNameBox;
    private SimpleColorPicker colorPicker;
    private readonly Color?[] colorSwatches = new Color?[7];
    private AppearancePayload savedAppearance;
    private Character customizedCharacter;
    private CharacterInfo previewInfo;
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

        Patch("Barotrauma.TabMenu", "CreateCrewListFrame", postfix: nameof(CreateCrewListFramePostfix));
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

    private static void CreateCrewListFramePostfix(GUIFrame crewFrame)
    {
        instance?.TryAddCustomizeButton(crewFrame);
    }

    private static void GameScreenAddToGUIUpdateListPostfix()
    {
        instance?.UpdateInGameCustomization();
    }

    private void TryAddCustomizeButton(GUIFrame crewFrame)
    {
        if (crewFrame == null) { return; }

        IEnumerable<GUIFrame> characterRows = crewFrame
            .GetAllChildren<GUIFrame>()
            .Where(frame => frame.UserData is Character);
        if (GameMain.IsMultiplayer)
        {
            characterRows = characterRows.Where(frame => frame.UserData == Character.Controlled);
        }

        foreach (GUIFrame characterRow in characterRows)
        {
            AddCustomizeButtonToRow(characterRow, (Character)characterRow.UserData);
        }
    }

    private void AddCustomizeButtonToRow(GUIFrame characterRow, Character character)
    {
        GUILayoutGroup rowLayout = characterRow?
            .GetAllChildren<GUILayoutGroup>()
            .FirstOrDefault(group => group.Parent == characterRow);
        GUITextBlock nameBlock = rowLayout?.GetChild(1) as GUITextBlock;
        if (nameBlock == null || rowLayout.FindChild(CustomizeButtonUserData, recursive: true) != null) { return; }

        int originalNameWidth = nameBlock.RectTransform.NonScaledSize.X;
        int buttonWidth = Math.Min(GUI.IntScale(110f), Math.Max(GUI.IntScale(74f), originalNameWidth / 3));
        int remainingNameWidth = Math.Max(1, originalNameWidth - buttonWidth - rowLayout.AbsoluteSpacing);
        nameBlock.RectTransform.Resize(new Point(remainingNameWidth, nameBlock.RectTransform.NonScaledSize.Y));

        var button = new GUIButton(
            new RectTransform(new Point(buttonWidth, rowLayout.Rect.Height), rowLayout.RectTransform, Anchor.Center, isFixedSize: true),
            "Customize",
            style: "GUIButtonSmall")
        {
            UserData = CustomizeButtonUserData,
            ToolTip = "Customize character",
            OnClicked = (_, _) =>
            {
                OpenCustomizationWindow(character);
                return true;
            }
        };

        button.RectTransform.RepositionChildInHierarchy(1);
        button.TextBlock.AutoScaleHorizontal = true;
        rowLayout.Recalculate();
    }

    private void OpenCustomizationWindow(Character character)
    {
        if (character?.Info?.Head == null) { return; }

        CloseCustomizationWindow(revert: false);
        customizedCharacter = character;
        AppearancePayload originalAppearance = AppearancePayload.FromCharacter(character);
        previewInfo = CreatePreviewInfo(character, originalAppearance);

        customizationRoot = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null, color: Color.Black * 0.6f)
        {
            CanBeFocused = true
        };

        var window = new GUIFrame(new RectTransform(new Vector2(0.45f, 0.60f), customizationRoot.RectTransform, Anchor.Center)
        {
            MinSize = new Point(333, 444)
        }, style: "GUIFrame")
        {
            CanBeFocused = true
        };

        var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.94f, 0.92f), window.RectTransform, Anchor.TopCenter)
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

        var nameRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), layout.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.04f
        };

        new GUITextBlock(
            new RectTransform(new Vector2(0.25f, 1.0f), nameRow.RectTransform),
            "Name",
            textAlignment: Alignment.CenterLeft);

        characterNameBox = new GUITextBox(
            new RectTransform(new Vector2(0.75f, 1.0f), nameRow.RectTransform),
            previewInfo.Name)
        {
            MaxTextLength = Client.MaxNameLength,
            OverflowClip = true
        };

        var traitRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), layout.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.04f
        };

        new GUITextBlock(
            new RectTransform(new Vector2(0.25f, 1.0f), traitRow.RectTransform),
            TextManager.Get("PersonalityTrait").Fallback("Trait"),
            textAlignment: Alignment.CenterLeft);

        var traitDropdown = new GUIDropDown(new RectTransform(new Vector2(0.75f, 1.0f), traitRow.RectTransform), elementCount: 6);
        Identifier selectedTrait = AppearancePayload.GetPersonalityTraitIdentifier(previewInfo);
        foreach ((Identifier identifier, LocalizedString displayName) in AppearancePayload.GetPersonalityTraits().OrderBy(option => option.DisplayName.Value))
        {
            traitDropdown.AddItem(displayName, identifier);
        }
        traitDropdown.OnSelected = (_, data) =>
        {
            if (data is Identifier identifier)
            {
                AppearancePayload.SetPersonalityTrait(previewInfo, identifier);
            }
            return true;
        };
        GUIComponent selectedTraitItem = traitDropdown.ListBox.Content.FindChild(component => component.UserData is Identifier identifier && identifier == selectedTrait);
        if (selectedTraitItem != null)
        {
            traitDropdown.Select(traitDropdown.ListBox.Content.GetChildIndex(selectedTraitItem));
        }

        customizationMenuHost = new UpdatingFrame(
            new RectTransform(new Vector2(1.0f, 1.0f), layout.RectTransform),
            _ =>
            {
                if (PlayerInput.KeyHit(Keys.Escape))
                {
                    if (colorPicker != null)
                    {
                        CloseColorPicker();
                    }
                    else
                    {
                        CloseCustomizationWindow(revert: true);
                    }
                    return;
                }

                customizationMenu?.Update();
                EnsureCustomColorButtons();
            },
            style: "GUIFrameListBox");

        var buttonRow = new GUILayoutGroup(new RectTransform(new Vector2(0.56f, 0.07f), window.RectTransform, Anchor.BottomCenter)
        {
            RelativeOffset = new Vector2(0.0f, -0.04f),
            MinSize = new Point(280, 32),
            MaxSize = new Point(520, 42)
        }, isHorizontal: true, childAnchor: Anchor.CenterLeft)
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

        customizationMenu = new CharacterInfo.AppearanceCustomizationMenu(previewInfo, customizationMenuHost);
        EnsureCustomColorButtons();
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
            FitPopupListToCanvas(headSelectionList);
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
        if (customizedCharacter?.Info?.Head == null || previewInfo?.Head == null) { return; }

        string name = Client.SanitizeName(characterNameBox?.Text ?? customizedCharacter.Info.Name);
        if (string.IsNullOrWhiteSpace(name)) { return; }

        AppearancePayload payload = AppearancePayload.FromCharacterInfo(previewInfo, customizedCharacter.ID)
            .WithName(name)
            .ValidateFor(customizedCharacter.Info);
        payload.ApplyTo(customizedCharacter);
        if (GameMain.Client != null && customizedCharacter == Character.Controlled)
        {
            savedAppearance = payload;
            savedCampaignRoundId = GetCampaignRoundId();
            lastAppliedSavedAppearanceCharacterId = payload.CharacterId;
            hasSavedAppearance = true;
            SaveLocalPreferences(payload);
        }

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

        AppearancePayload payload = savedAppearance.WithCharacterId(controlled.ID).ValidateFor(controlled.Info);
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
        preferences.PlayerName = payload.Name;
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
        CloseColorPicker();
        if (revert)
        {
            previewInfo = null;
        }

        customizationMenu?.Dispose();
        customizationMenu = null;
        customizationMenuHost = null;
        characterNameBox = null;
        customizationRoot?.RemoveFromGUIUpdateList();
        if (customizationRoot != null)
        {
            customizationRoot.RectTransform.Parent = null;
            customizationRoot = null;
        }
        customizedCharacter = null;
        previewInfo = null;
    }

    private void EnsureCustomColorButtons()
    {
        if (customizationMenuHost == null || previewInfo?.Head == null) { return; }

        var colorTargets = new List<(LocalizedString Label, Func<Color> Getter, Action<Color> Setter)>();
        if (previewInfo.CountValidAttachmentsOfType(WearableType.Hair) > 0)
        {
            colorTargets.Add((TextManager.Get($"Customization.{nameof(previewInfo.Head.HairColor)}"),
                () => previewInfo.Head.HairColor,
                color => previewInfo.Head.HairColor = color));
        }
        if (previewInfo.CountValidAttachmentsOfType(WearableType.Moustache) > 0 ||
            previewInfo.CountValidAttachmentsOfType(WearableType.Beard) > 0)
        {
            colorTargets.Add((TextManager.Get($"Customization.{nameof(previewInfo.Head.FacialHairColor)}"),
                () => previewInfo.Head.FacialHairColor,
                color => previewInfo.Head.FacialHairColor = color));
        }
        colorTargets.Add((TextManager.Get($"Customization.{nameof(previewInfo.Head.SkinColor)}"),
            () => previewInfo.Head.SkinColor,
            color => previewInfo.Head.SkinColor = color));

        GUIDropDown[] selectors = customizationMenuHost.GetAllChildren<GUIDropDown>().ToArray();
        int count = Math.Min(selectors.Length, colorTargets.Count);
        for (int i = 0; i < count; i++)
        {
            int targetIndex = i;
            GUIDropDown selector = selectors[i];
            GUIComponent row = selector.Parent;
            string userData = $"InGameCharacterCustomizer.ColorPicker.{i}";
            if (row == null || row.FindChild(userData, recursive: true) != null) { continue; }

            selector.RectTransform.Resize(new Vector2(0.55f, 1f));
            selector.RectTransform.SetPosition(Anchor.Center);
            var button = new GUIButton(new RectTransform(new Vector2(0.20f, 1f), row.RectTransform, Anchor.CenterRight), "Custom", style: "GUIButtonSmall")
            {
                UserData = userData,
                ToolTip = "Open color picker",
                OnClicked = (_, _) =>
                {
                    OpenColorPicker(colorTargets[targetIndex]);
                    return true;
                }
            };
            button.TextBlock.AutoScaleHorizontal = true;
        }
    }

    private void OpenColorPicker((LocalizedString Label, Func<Color> Getter, Action<Color> Setter) target)
    {
        CloseColorPicker();
        colorPicker = new SimpleColorPicker(
            customizationRoot,
            target.Label,
            target.Getter(),
            colorSwatches,
            color =>
            {
                target.Setter(color);
                previewInfo.RefreshHead();
            },
            closedPicker =>
            {
                if (colorPicker == closedPicker) { colorPicker = null; }
            });
    }

    private void CloseColorPicker()
    {
        SimpleColorPicker picker = colorPicker;
        colorPicker = null;
        picker?.Dispose();
    }

    private static void ReadServerAppearance(IReadMessage message)
    {
        AppearancePayload payload = AppearancePayload.Read(message);
        Character character = Character.CharacterList.FirstOrDefault(c => c.ID == payload.CharacterId);
        if (character?.Info?.Head != null)
        {
            payload = payload.ValidateFor(character.Info);
        }
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

    private static CharacterInfo CreatePreviewInfo(Character character, AppearancePayload appearance)
    {
        CharacterInfo source = character.Info;
        var preview = new CharacterInfo(source.SpeciesName, source.Name, source.OriginalName, source.Job);
        appearance.ApplyTo(preview);
        return preview;
    }

    private static void FitPopupListToCanvas(GUIListBox listBox)
    {
        if (listBox?.RectTransform == null || GUI.Canvas == null) { return; }

        Rectangle canvas = GUI.Canvas.Rect;
        Rectangle rect = listBox.Rect;
        int maxHeight = Math.Max(120, canvas.Bottom - rect.Y - 12);
        if (rect.Height > maxHeight)
        {
            listBox.RectTransform.Resize(new Point(rect.Width, maxHeight));
            rect = listBox.Rect;
        }

        Point offset = listBox.RectTransform.AbsoluteOffset;
        if (rect.Right > canvas.Right)
        {
            offset.X -= rect.Right - canvas.Right + 12;
        }
        if (rect.Bottom > canvas.Bottom)
        {
            offset.Y -= rect.Bottom - canvas.Bottom + 12;
        }
        if (rect.X < canvas.X)
        {
            offset.X += canvas.X - rect.X + 12;
        }
        if (rect.Y < canvas.Y)
        {
            offset.Y += canvas.Y - rect.Y + 12;
        }
        listBox.RectTransform.AbsoluteOffset = offset;
    }

    private sealed class UpdatingFrame : GUIFrame
    {
        private readonly Action<float> onUpdate;

        public UpdatingFrame(RectTransform rectT, Action<float> onUpdate, string style = "", Color? color = null)
            : base(rectT, style, color)
        {
            this.onUpdate = onUpdate;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            onUpdate?.Invoke(deltaTime);
        }
    }
}
