using Barotrauma;
using Barotrauma.CharacterEditor;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.LuaCs;
using Barotrauma.Steam;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace CharacterViewer;

public sealed class CharacterViewerPlugin : IAssemblyPlugin
{
    private static CharacterViewerPlugin instance;

    private readonly List<Item> viewerEquippedItems = new List<Item>();
    private readonly Dictionary<string, Point> windowOffsets = new Dictionary<string, Point>();

    private Harmony harmony;
    private GUIFrame headWindow;
    private GUIFrame clothingWindow;
    private GUITextBlock clothingInfoText;
    private GUITextBlock statusText;
    private GUITextBox searchBox;
    private GUIDropDown clothingDropDown;
    private ItemPrefab selectedClothingPrefab;
    private string searchText = string.Empty;
    private Identifier selectedGender = Identifier.Empty;
    private bool recreateGuiQueued = true;
    private bool applyingGender;
    private bool suppressClothingSelection;
    private bool panelsEnabled;
    private GUIFrame draggedWindow;
    private Vector2 draggedWindowOffset;

    public void PreInitPatching()
    {
    }

    public void Initialize()
    {
        instance = this;
        harmony = new Harmony("CharacterViewer.Example");

        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "AddToGUIUpdateList", postfix: nameof(AddToGUIUpdateListPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "Update", new[] { typeof(double) }, postfix: nameof(UpdatePostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "CreateFileEditPanel", postfix: nameof(CreateFileEditPanelPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "CreateModesPanel", new[] { typeof(Vector2) }, postfix: nameof(CreateModesPanelPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "SpawnCharacter", new[] { typeof(Identifier), typeof(RagdollParams) }, prefix: nameof(SpawnCharacterPrefix), postfix: nameof(SpawnCharacterPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "DeselectEditorSpecific", postfix: nameof(DeselectEditorPostfix));

        GameMain.ExecuteAfterContentFinishedLoading(SelectCharacterViewerIfRequested);
        LuaCsLogger.Log("CharacterViewer loaded.");
    }

    public void OnLoadCompleted()
    {
    }

    public void Dispose()
    {
        ClearViewerClothing();
        RemoveWindows();
        harmony?.UnpatchSelf();
        harmony = null;

        if (instance == this)
        {
            instance = null;
        }

        LuaCsLogger.Log("CharacterViewer disposed.");
    }

    private void Patch(string typeName, string methodName, Type[] parameterTypes = null, string prefix = null, string postfix = null)
    {
        Type targetType = AccessTools.TypeByName(typeName);
        MethodInfo target = targetType == null ? null : AccessTools.Method(targetType, methodName, parameterTypes);
        if (target == null)
        {
            LuaCsLogger.LogError($"CharacterViewer could not patch {typeName}.{methodName}.");
            return;
        }

        HarmonyMethod prefixMethod = prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(CharacterViewerPlugin), prefix));
        HarmonyMethod postfixMethod = postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(CharacterViewerPlugin), postfix));
        harmony.Patch(target, prefixMethod, postfixMethod);
    }

    private static void AddToGUIUpdateListPostfix()
    {
        instance?.AddWindowsToGuiUpdateList();
    }

    private static void SpawnCharacterPrefix()
    {
        instance?.ClearViewerClothing();
    }

    private static void UpdatePostfix()
    {
        instance?.OnCharacterEditorUpdated();
    }

    private static void CreateFileEditPanelPostfix(CharacterEditorScreen __instance)
    {
        instance?.AddModManagerButtonToFilePanel(__instance);
    }

    private static void CreateModesPanelPostfix(CharacterEditorScreen __instance)
    {
        instance?.AddPanelToggleToModesPanel(__instance);
    }

    private static void SpawnCharacterPostfix()
    {
        instance?.OnCharacterSpawned();
    }

    private static void DeselectEditorPostfix()
    {
        instance?.RemoveWindows();
    }

    private void SelectCharacterViewerIfRequested()
    {
        if (!GameMain.Instance.ConsoleArguments.Any(arg =>
                arg.Equals("-characterviewer", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-charactereditor", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        GameMain.Instance.Window.Title = "Barotrauma Character Viewer";
        GameMain.CharacterEditorScreen.Select();
        panelsEnabled = true;
        QueueGuiRecreate();
    }

    private void AddWindowsToGuiUpdateList()
    {
        if (Screen.Selected is not CharacterEditorScreen) { return; }
        EnsureEditorPanelControls();
        if (!panelsEnabled || GUIMessageBox.VisibleBox != null)
        {
            RemoveWindows();
            return;
        }

        if (recreateGuiQueued || headWindow == null || clothingWindow == null)
        {
            RecreateWindows();
        }

        headWindow?.AddToGUIUpdateList(ignoreChildren: false, order: 1);
        clothingWindow?.AddToGUIUpdateList(ignoreChildren: false, order: 1);
    }

    private void OnCharacterEditorUpdated()
    {
        if (Screen.Selected is not CharacterEditorScreen) { return; }

        EnsureEditorPanelControls();
        UpdateShortcuts();

        if (panelsEnabled && GUIMessageBox.VisibleBox == null)
        {
            UpdateWindowDragging();
        }
    }

    private void UpdateShortcuts()
    {
        if (Screen.Selected is not CharacterEditorScreen) { return; }
        if (GUI.KeyboardDispatcher.Subscriber != null) { return; }
        if (!PlayerInput.KeyHit(Keys.D6)) { return; }

        panelsEnabled = !panelsEnabled;
        if (!panelsEnabled)
        {
            RemoveWindows();
        }
        else
        {
            QueueGuiRecreate();
        }
        SyncPanelToggle();
    }

    private void EnsureEditorPanelControls()
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        if (editor == null) { return; }

        AddModManagerButtonToFilePanel(editor);
        AddPanelToggleToModesPanel(editor);
    }

    private void AddModManagerButtonToFilePanel(CharacterEditorScreen editor)
    {
        GUIFrame fileEditPanel = AccessTools.Field(editor.GetType(), "fileEditPanel")?.GetValue(editor) as GUIFrame;
        GUILayoutGroup layout = fileEditPanel?.GetChild<GUILayoutGroup>();
        if (layout == null || layout.GetAllChildren().Any(c => c.UserData as string == "CharacterViewer.ModManager")) { return; }

        var button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.04f), layout.RectTransform), "Mod Manager")
        {
            UserData = "CharacterViewer.ModManager",
            OnClicked = (_, _) =>
            {
                OpenModManager();
                return true;
            }
        };
        fileEditPanel.RectTransform.MinSize += new Point(0, button.RectTransform.MinSize.Y + layout.AbsoluteSpacing);
        layout.Recalculate();
    }

    private void AddPanelToggleToModesPanel(CharacterEditorScreen editor)
    {
        GUIFrame modesPanel = AccessTools.Field(editor.GetType(), "modesPanel")?.GetValue(editor) as GUIFrame;
        GUILayoutGroup layout = modesPanel?.GetChild<GUILayoutGroup>();
        if (layout == null || layout.GetAllChildren().Any(c => c.UserData as string == "CharacterViewer.PanelToggle")) { return; }

        var tickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.03f), layout.RectTransform), "CHARACTER VIEWER [6]")
        {
            UserData = "CharacterViewer.PanelToggle",
            Selected = panelsEnabled,
            OnSelected = box =>
            {
                panelsEnabled = box.Selected;
                if (!panelsEnabled)
                {
                    RemoveWindows();
                }
                else
                {
                    QueueGuiRecreate();
                }
                return true;
            }
        };
        modesPanel.RectTransform.MinSize += new Point(0, tickBox.RectTransform.MinSize.Y + layout.AbsoluteSpacing);
        layout.Recalculate();
    }

    private void SyncPanelToggle()
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        GUIFrame modesPanel = AccessTools.Field(editor.GetType(), "modesPanel")?.GetValue(editor) as GUIFrame;
        var tickBox = modesPanel?.GetAllChildren().OfType<GUITickBox>().FirstOrDefault(c => c.UserData as string == "CharacterViewer.PanelToggle");
        if (tickBox != null)
        {
            tickBox.Selected = panelsEnabled;
        }
    }

    private void OnCharacterSpawned()
    {
        if (!applyingGender)
        {
            ApplySelectedGender();
        }
        QueueGuiRecreate();
    }

    private void QueueGuiRecreate()
    {
        recreateGuiQueued = true;
    }

    private void RecreateWindows()
    {
        recreateGuiQueued = false;
        RemoveWindows();

        CreateHeadWindow();
        CreateClothingWindow();
    }

    private void RemoveWindows()
    {
        RemoveWindow(headWindow);
        RemoveWindow(clothingWindow);
        headWindow = null;
        clothingWindow = null;
        clothingInfoText = null;
        statusText = null;
        searchBox = null;
        clothingDropDown = null;
        draggedWindow = null;
    }

    private static void RemoveWindow(GUIFrame window)
    {
        if (window == null) { return; }
        window.RemoveFromGUIUpdateList();
        window.RectTransform.Parent = null;
    }

    private GUILayoutGroup CreateFloatingWindow(string title, Point size, Point defaultOffset, out GUIFrame window)
    {
        Point offset = windowOffsets.TryGetValue(title, out Point storedOffset) ? storedOffset : defaultOffset;
        window = new GUIFrame(
            new RectTransform(size.Multiply(GUI.Scale), GUI.Canvas, Anchor.TopLeft, Pivot.TopLeft)
            {
                AbsoluteOffset = offset.Multiply(GUI.Scale)
            },
            style: "GUIFrame")
        {
            UserData = title
        };

        GUILayoutGroup outer = new GUILayoutGroup(new RectTransform(Vector2.One, window.RectTransform), isHorizontal: false)
        {
            Stretch = true,
            AbsoluteSpacing = GUI.IntScale(4)
        };

        GUIFrame header = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.09f), outer.RectTransform), style: "GUIFrameListBox");
        new GUITextBlock(new RectTransform(new Vector2(0.94f, 1.0f), header.RectTransform, Anchor.CenterLeft), title, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
        {
            CanBeFocused = false
        };

        GUILayoutGroup content = new GUILayoutGroup(new RectTransform(new Vector2(0.96f, 0.88f), outer.RectTransform, Anchor.Center), isHorizontal: false, childAnchor: Anchor.TopLeft)
        {
            Stretch = true,
            AbsoluteSpacing = GUI.IntScale(5)
        };

        return content;
    }

    private void CreateHeadWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow("Character Viewer", new Point(420, 420), new Point(24, 30), out headWindow);
        Character character = CurrentCharacter;
        CharacterInfo info = character?.Info;
        if (info?.Head?.Preset == null)
        {
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "No CharacterInfo available.", wrap: true);
            return;
        }

        CreateGenderDropDown(content, character, info);
        CreateHeadPresetDropDown(content, character, info);
        info.LoadHeadAttachments();
        CreateAttachmentDropDown(content, "Hair", info.Hairs, info.Head.HairIndex, index => info.Head.HairIndex = index);
        CreateAttachmentDropDown(content, "Beard", info.Beards, info.Head.BeardIndex, index => info.Head.BeardIndex = index);
        CreateAttachmentDropDown(content, "Moustache", info.Moustaches, info.Head.MoustacheIndex, index => info.Head.MoustacheIndex = index);
        CreateAttachmentDropDown(content, "Face", info.FaceAttachments, info.Head.FaceAttachmentIndex, index => info.Head.FaceAttachmentIndex = index);

    }

    private void CreateGenderDropDown(GUILayoutGroup content, Character character, CharacterInfo info)
    {
        if (character == null || !character.SpeciesName.Equals(CharacterPrefab.HumanSpeciesName)) { return; }
        if (!info.Prefab.VarTags.TryGetValue("GENDER".ToIdentifier(), out ImmutableHashSet<Identifier> genders) || genders.None()) { return; }

        if (selectedGender.IsEmpty && info.Head?.Preset != null)
        {
            selectedGender = info.Head.Preset.TagSet.FirstOrDefault(genders.Contains);
        }

        GUILayoutGroup row = CreateLabeledRow(content, "Gender");
        GUIDropDown dropDown = new GUIDropDown(new RectTransform(new Vector2(0.68f, 1.0f), row.RectTransform), selectedGender.IsEmpty ? "Default" : selectedGender.Value);
        foreach (Identifier gender in genders.OrderBy(g => g.Value))
        {
            dropDown.AddItem(gender.Value, gender);
        }
        if (!selectedGender.IsEmpty)
        {
            dropDown.SelectItem(selectedGender);
        }
        dropDown.OnSelected = (_, data) =>
        {
            if (data is not Identifier gender || gender == selectedGender) { return true; }
            selectedGender = gender;
            applyingGender = true;
            try
            {
                GameMain.CharacterEditorScreen.SpawnCharacter(CharacterPrefab.HumanSpeciesName);
                ApplySelectedGender();
            }
            finally
            {
                applyingGender = false;
            }
            QueueGuiRecreate();
            return true;
        };
    }

    private void CreateHeadPresetDropDown(GUILayoutGroup content, Character character, CharacterInfo info)
    {
        GUILayoutGroup row = CreateLabeledRow(content, "Head");
        GUIDropDown dropDown = new GUIDropDown(new RectTransform(new Vector2(0.68f, 1.0f), row.RectTransform), GetHeadPresetLabel(info.Head.Preset));
        foreach (CharacterInfo.HeadPreset preset in info.Prefab.Heads)
        {
            dropDown.AddItem(GetHeadPresetLabel(preset), preset);
        }
        dropDown.SelectItem(info.Head.Preset);
        dropDown.OnSelected = (_, data) =>
        {
            if (data is not CharacterInfo.HeadPreset preset) { return true; }
            ApplyHeadPreset(character, info, preset);
            QueueGuiRecreate();
            return true;
        };
    }

    private void CreateAttachmentDropDown(GUILayoutGroup content, string label, IReadOnlyList<ContentXElement> elements, int selectedIndex, Action<int> applyIndex)
    {
        if (elements == null || elements.Count == 0) { return; }

        selectedIndex = Math.Max(0, Math.Min(selectedIndex, elements.Count - 1));
        GUILayoutGroup row = CreateLabeledRow(content, label);
        GUIDropDown dropDown = new GUIDropDown(new RectTransform(new Vector2(0.68f, 1.0f), row.RectTransform), GetAttachmentLabel(elements[selectedIndex], selectedIndex));
        for (int i = 0; i < elements.Count; i++)
        {
            dropDown.AddItem(GetAttachmentLabel(elements[i], i), i);
        }
        dropDown.SelectItem(selectedIndex);
        dropDown.OnSelected = (_, data) =>
        {
            if (data is not int index) { return true; }
            Character character = CurrentCharacter;
            CharacterInfo info = character?.Info;
            if (info == null) { return true; }
            applyIndex(index);
            info.ReloadHeadAttachments();
            character.LoadHeadAttachments();
            QueueGuiRecreate();
            return true;
        };
    }

    private static GUILayoutGroup CreateLabeledRow(GUILayoutGroup parent, string label)
    {
        GUILayoutGroup row = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.095f), parent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), row.RectTransform), label, textAlignment: Alignment.CenterLeft);
        return row;
    }

    private static string GetHeadPresetLabel(CharacterInfo.HeadPreset preset)
    {
        return preset == null ? "None" : string.Join(", ", preset.TagSet.Select(t => t.Value));
    }

    private static string GetAttachmentLabel(ContentXElement element, int index)
    {
        if (element == null) { return $"{index}: None"; }
        string name = element.GetAttributeString("name", null) ??
                      element.GetAttributeString("identifier", null) ??
                      element.Name.ToString();
        return $"{index}: {name}";
    }

    private void ApplyHeadPreset(Character character, CharacterInfo info, CharacterInfo.HeadPreset preset)
    {
        if (character == null || info?.Head == null || preset == null) { return; }
        CharacterInfo.HeadInfo oldHead = info.Head;
        Color skinColor = oldHead.SkinColor;
        Color hairColor = oldHead.HairColor;
        Color facialHairColor = oldHead.FacialHairColor;
        info.RecreateHead(preset.TagSet, oldHead.HairIndex, oldHead.BeardIndex, oldHead.MoustacheIndex, oldHead.FaceAttachmentIndex);
        info.Head.SkinColor = skinColor;
        info.Head.HairColor = hairColor;
        info.Head.FacialHairColor = facialHairColor;
        info.ReloadHeadAttachments();
        character.LoadHeadAttachments();
    }

    private void ApplySelectedGender()
    {
        Character character = CurrentCharacter;
        CharacterInfo info = character?.Info;
        if (character == null || info?.Head?.Preset == null || selectedGender.IsEmpty) { return; }
        if (!character.SpeciesName.Equals(CharacterPrefab.HumanSpeciesName)) { return; }
        if (!info.Prefab.VarTags.TryGetValue("GENDER".ToIdentifier(), out ImmutableHashSet<Identifier> genderTags) || !genderTags.Contains(selectedGender)) { return; }

        CharacterInfo.HeadPreset preset = info.Prefab.Heads.FirstOrDefault(h => h.TagSet.Contains(selectedGender));
        if (preset == null) { return; }
        ApplyHeadPreset(character, info, preset);
    }

    private void CreateClothingWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow("Clothing Manager", new Point(470, 500), new Point(468, 30), out clothingWindow);

        searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.08f), content.RectTransform), searchText, style: "GUITextBox")
        {
            OnTextChangedDelegate = (_, text) =>
            {
                searchText = text ?? string.Empty;
                PopulateClothingDropDown();
                return true;
            }
        };

        clothingDropDown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.09f), content.RectTransform), "Select clothing");
        clothingDropDown.OnSelected = (_, data) =>
        {
            if (suppressClothingSelection) { return true; }
            selectedClothingPrefab = data as ItemPrefab;
            EquipViewerClothing(selectedClothingPrefab);
            return true;
        };
        PopulateClothingDropDown();

        GUILayoutGroup navRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.09f), content.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        CreateButton(navRow, "Prev", () => SelectAdjacentClothing(-1));
        CreateButton(navRow, "Next", () => SelectAdjacentClothing(1));
        CreateButton(navRow, "Reload", () => EquipViewerClothing(selectedClothingPrefab));
        CreateButton(navRow, "Remove", () =>
        {
            selectedClothingPrefab = null;
            ClearViewerClothing();
            UpdateClothingInfo("No clothing selected.");
            PopulateClothingDropDown();
        });

        clothingInfoText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.38f), content.RectTransform), "", wrap: true, font: GUIStyle.SmallFont);
        statusText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.08f), content.RectTransform), "", wrap: true, font: GUIStyle.SmallFont)
        {
            TextColor = GUIStyle.TextColorDim
        };

        UpdateClothingInfo();
    }

    private static void CreateButton(GUILayoutGroup parent, string text, Action onClicked)
    {
        new GUIButton(new RectTransform(Vector2.One, parent.RectTransform), text, style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                onClicked();
                return true;
            }
        };
    }

    private void PopulateClothingDropDown()
    {
        if (clothingDropDown == null) { return; }

        suppressClothingSelection = true;
        clothingDropDown.ClearChildren();
        List<ItemPrefab> prefabs = GetFilteredWearablePrefabs();
        foreach (ItemPrefab prefab in prefabs)
        {
            string label = $"{prefab.Name} ({prefab.Identifier})";
            GUIComponent item = clothingDropDown.AddItem(label, prefab);
            item.ToolTip = prefab.FilePath.Value;
        }
        if (selectedClothingPrefab != null && prefabs.Contains(selectedClothingPrefab))
        {
            clothingDropDown.SelectItem(selectedClothingPrefab);
        }
        else
        {
            clothingDropDown.Text = prefabs.Count == 0 ? "No matching wearable items" : "Select clothing";
        }
        suppressClothingSelection = false;
    }

    private List<ItemPrefab> GetFilteredWearablePrefabs()
    {
        string filter = searchText?.Trim() ?? string.Empty;
        return ItemPrefab.Prefabs
            .Where(p => p.ConfigElement.GetChildElements("Wearable").Any())
            .Where(p => filter.Length == 0 ||
                        p.Name.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        p.Identifier.Value.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        p.FilePath.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name.ToString())
            .ThenBy(p => p.Identifier.Value)
            .ToList();
    }

    private void SelectAdjacentClothing(int direction)
    {
        List<ItemPrefab> prefabs = GetFilteredWearablePrefabs();
        if (prefabs.Count == 0) { return; }

        int index = selectedClothingPrefab == null ? -1 : prefabs.IndexOf(selectedClothingPrefab);
        int nextIndex = index < 0 ? (direction > 0 ? 0 : prefabs.Count - 1) : (index + direction + prefabs.Count) % prefabs.Count;
        selectedClothingPrefab = prefabs[nextIndex];
        clothingDropDown?.SelectItem(selectedClothingPrefab);
        EquipViewerClothing(selectedClothingPrefab);
    }

    private void EquipViewerClothing(ItemPrefab prefab)
    {
        ClearViewerClothing();
        if (prefab == null)
        {
            UpdateClothingInfo("No clothing selected.");
            return;
        }

        Character character = CurrentCharacter;
        if (character?.Inventory == null)
        {
            UpdateClothingInfo("No character inventory available.");
            return;
        }

        Item item = null;
        try
        {
            item = new Item(prefab, character.Position, null)
            {
                UnequipAutomatically = false
            };
            Wearable wearable = item.GetComponent<Wearable>();
            Pickable pickable = item.GetComponent<Pickable>();
            if (wearable == null)
            {
                item.Remove();
                UpdateClothingInfo("Selected item has no wearable component.");
                return;
            }
            if (pickable == null)
            {
                item.Remove();
                UpdateClothingInfo("Selected item has no pickable component.");
                return;
            }

            List<InvSlotType> allowedSlots = item.GetComponents<Pickable>().Count() > 1 ?
                new List<InvSlotType>(wearable.AllowedSlots) :
                new List<InvSlotType>(item.AllowedSlots);
            allowedSlots.Remove(InvSlotType.Any);

            if (!character.Inventory.TryPutItem(item, null, allowedSlots, createNetworkEvent: false))
            {
                item.Remove();
                UpdateClothingInfo("Could not equip the selected item in any allowed slot.");
                return;
            }

            wearable.Equip(character);
            viewerEquippedItems.Add(item);
            UpdateClothingInfo("Equipped for preview.");
        }
        catch (Exception ex)
        {
            item?.Remove();
            LuaCsLogger.LogError($"CharacterViewer failed to equip {prefab.Identifier}: {ex}");
            UpdateClothingInfo("Failed to equip selected clothing. See console for details.");
        }
    }

    private void ClearViewerClothing()
    {
        Character character = CurrentCharacter;
        foreach (Item item in viewerEquippedItems.ToArray())
        {
            try
            {
                item.GetComponent<Wearable>()?.Unequip(character);
                item.ParentInventory?.RemoveItem(item);
                item.Remove();
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"CharacterViewer failed to remove preview item: {ex}");
            }
        }
        viewerEquippedItems.Clear();
    }

    private void UpdateClothingInfo(string state = null)
    {
        if (clothingInfoText == null) { return; }
        if (selectedClothingPrefab == null)
        {
            clothingInfoText.Text = "No clothing selected.";
        }
        else
        {
            int spriteCount = selectedClothingPrefab.ConfigElement
                .GetChildElements("Wearable")
                .SelectMany(w => w.GetChildElements("sprite"))
                .Count();
            clothingInfoText.Text =
                $"Name: {selectedClothingPrefab.Name}\n" +
                $"Identifier: {selectedClothingPrefab.Identifier}\n" +
                $"Wearable sprites: {spriteCount}\n" +
                $"XML: {selectedClothingPrefab.FilePath.Value}";
        }
        if (statusText != null)
        {
            statusText.Text = state ?? "";
        }
    }

    private void OpenModManager()
    {
        GUIMessageBox messageBox = new GUIMessageBox("Mod Manager", "", new LocalizedString[] { "Cancel", "Apply" }, new Vector2(0.78f, 0.88f));
        if (messageBox.Text != null)
        {
            messageBox.Text.Visible = false;
        }
        messageBox.Content.ClearChildren();
        messageBox.Content.Stretch = true;

        GUIFrame menuFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.92f), messageBox.Content.RectTransform), style: null);
        MutableWorkshopMenu workshopMenu = new MutableWorkshopMenu(menuFrame);
        workshopMenu.SelectTab(MutableWorkshopMenu.Tab.InstalledMods);

        GUILayoutGroup buttonRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), messageBox.Content.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        new GUIButton(new RectTransform(Vector2.One, buttonRow.RectTransform), "Cancel")
        {
            OnClicked = (_, _) =>
            {
                messageBox.Close();
                return true;
            }
        };
        new GUIButton(new RectTransform(Vector2.One, buttonRow.RectTransform), "Apply")
        {
            OnClicked = (_, _) =>
            {
                ApplyModManagerChanges(workshopMenu, messageBox);
                return true;
            }
        };

        foreach (GUIButton button in messageBox.Buttons)
        {
            button.Visible = false;
            button.Enabled = false;
        }
    }

    private void ApplyModManagerChanges(MutableWorkshopMenu workshopMenu, GUIMessageBox messageBox)
    {
        Identifier previousSpecies = CurrentCharacter?.SpeciesName ?? CharacterPrefab.HumanSpeciesName;
        try
        {
            ClearViewerClothing();
            Character character = CurrentCharacter;
            if (character != null)
            {
                character.Remove();
            }
            if (Character.Controlled == character)
            {
                Character.Controlled = null;
            }

            workshopMenu.Apply();
            GameSettings.SaveCurrentConfig();
            RefreshCharacterEditorSpeciesCache();
            RespawnBestAvailableSpecies(previousSpecies);
            selectedClothingPrefab = null;
            searchText = string.Empty;
            messageBox.Close();
            QueueGuiRecreate();
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError($"CharacterViewer failed to apply mod changes: {ex}");
            DebugConsole.ThrowError("CharacterViewer failed to apply mod changes.", ex);
            messageBox.Close();
            QueueGuiRecreate();
        }
    }

    private static void RefreshCharacterEditorSpeciesCache()
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        FieldInfo visibleSpecies = AccessTools.Field(editor.GetType(), "visibleSpecies");
        visibleSpecies?.SetValue(editor, null);

        MethodInfo createGui = AccessTools.Method(editor.GetType(), "CreateGUI");
        createGui?.Invoke(editor, Array.Empty<object>());
    }

    private void RespawnBestAvailableSpecies(Identifier preferredSpecies)
    {
        Identifier species = CharacterPrefab.Prefabs.Any(p => p.MatchesSpeciesNameOrGroup(preferredSpecies))
            ? preferredSpecies
            : CharacterPrefab.Prefabs.Any(p => p.MatchesSpeciesNameOrGroup(CharacterPrefab.HumanSpeciesName))
                ? CharacterPrefab.HumanSpeciesName
                : CharacterPrefab.Prefabs.FirstOrDefault()?.Identifier ?? Identifier.Empty;

        if (!species.IsEmpty)
        {
            GameMain.CharacterEditorScreen.SpawnCharacter(species);
        }
    }

    private void UpdateWindowDragging()
    {
        GUIFrame hoverWindow = GetHoveredWindow();
        if (PlayerInput.PrimaryMouseButtonDown() && hoverWindow != null && !IsInteractiveChild(GUI.MouseOn))
        {
            draggedWindow = hoverWindow;
            draggedWindowOffset = draggedWindow.RectTransform.ScreenSpaceOffset.ToVector2() - PlayerInput.MousePosition;
        }

        if (PlayerInput.PrimaryMouseButtonHeld() && draggedWindow != null)
        {
            GUI.MouseCursor = CursorState.Dragging;
            draggedWindow.RectTransform.ScreenSpaceOffset = (PlayerInput.MousePosition + draggedWindowOffset).ToPoint();
            return;
        }

        if (draggedWindow?.UserData is string title)
        {
            Point absoluteOffset = draggedWindow.RectTransform.AbsoluteOffset;
            Point screenOffset = draggedWindow.RectTransform.ScreenSpaceOffset;
            windowOffsets[title] = new Point(
                (int)((absoluteOffset.X + screenOffset.X) / GUI.Scale),
                (int)((absoluteOffset.Y + screenOffset.Y) / GUI.Scale));
            draggedWindow.RectTransform.AbsoluteOffset += screenOffset;
            draggedWindow.RectTransform.ScreenSpaceOffset = Point.Zero;
            draggedWindow = null;
        }
    }

    private GUIFrame GetHoveredWindow()
    {
        if (IsWindowHeaderHovered(headWindow)) { return headWindow; }
        if (IsWindowHeaderHovered(clothingWindow)) { return clothingWindow; }
        return null;
    }

    private static bool IsWindowHeaderHovered(GUIFrame window)
    {
        if (window == null) { return false; }
        Rectangle headerRect = new Rectangle(
            window.Rect.X,
            window.Rect.Y,
            window.Rect.Width,
            Math.Max(GUI.IntScale(32), (int)(window.Rect.Height * 0.09f)));
        return headerRect.Contains(PlayerInput.MousePosition);
    }

    private static bool IsInteractiveChild(GUIComponent component)
    {
        return component is GUIButton or GUIDropDown or GUITextBox or GUIListBox or GUITickBox;
    }

    private static Character CurrentCharacter => GameMain.CharacterEditorScreen?.SpawnedCharacter;
}
