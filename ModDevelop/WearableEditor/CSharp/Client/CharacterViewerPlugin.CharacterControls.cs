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

public sealed partial class CharacterViewerPlugin
{

    private void EnsureEditorPanelControls()
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        if (editor == null) { return; }

        AddModManagerButtonToFilePanel(editor);
        AddPanelToggleToModesPanel(editor);
        AddInGameBehaviorToggleToMinorModesPanel(editor);
    }

    private void AddModManagerButtonToFilePanel(CharacterEditorScreen editor)
    {
        GUIFrame fileEditPanel = AccessTools.Field(editor.GetType(), "fileEditPanel")?.GetValue(editor) as GUIFrame;
        GUILayoutGroup layout = fileEditPanel?.GetChild<GUILayoutGroup>();
        if (layout == null || layout.GetAllChildren().Any(c => c.UserData as string == ModManagerButtonUserData)) { return; }

        var button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.04f), layout.RectTransform), Text("button.modmanager", "Mod Manager"))
        {
            UserData = ModManagerButtonUserData,
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
        if (layout == null) { return; }

        if (!layout.GetAllChildren().Any(c => c.UserData as string == PanelToggleUserData))
        {
            var tickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.03f), layout.RectTransform), Text("toggle.wearableviewer", "WEARABLE VIEWER [6]"))
            {
                UserData = PanelToggleUserData,
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
        }

        if (!layout.GetAllChildren().Any(c => c.UserData as string == WearableEditorToggleUserData))
        {
            var tickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.03f), layout.RectTransform), Text("toggle.wearableeditor", "WEARABLE EDITOR [7]"))
            {
                UserData = WearableEditorToggleUserData,
                Selected = wearableEditorEnabled,
                OnSelected = box =>
                {
                    SetWearableEditorEnabled(box.Selected);
                    return true;
                }
            };
            modesPanel.RectTransform.MinSize += new Point(0, tickBox.RectTransform.MinSize.Y + layout.AbsoluteSpacing);
        }
        layout.Recalculate();
    }

    private void SyncPanelToggle()
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        GUIFrame modesPanel = AccessTools.Field(editor.GetType(), "modesPanel")?.GetValue(editor) as GUIFrame;
        var tickBox = modesPanel?.GetAllChildren().OfType<GUITickBox>().FirstOrDefault(c => c.UserData as string == PanelToggleUserData);
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
        UpdateAllViewerSpriteInfo();
        QueueWearableEditorRebuild();
        QueueGuiRecreate();
    }

    private void CreateHeadWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow(WindowTitleCharacterViewer, new Point(300, 250), new Point(1, 15), out headWindow);
        Character character = CurrentCharacter;
        CharacterInfo info = character?.Info;
        if (info?.Head?.Preset == null)
        {
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), Text("message.nocharacterinfo", "No CharacterInfo available."), wrap: true);
            return;
        }

        CreateGenderDropDown(content, character, info);
        CreateHeadPresetDropDown(content, character, info);
        info.LoadHeadAttachments();
        CreateAttachmentDropDown(content, Text("label.hair", "Hair"), info.Hairs, info.Head.HairIndex, index => info.Head.HairIndex = index);
        CreateAttachmentDropDown(content, Text("label.beard", "Beard"), info.Beards, info.Head.BeardIndex, index => info.Head.BeardIndex = index);
        CreateAttachmentDropDown(content, Text("label.moustache", "Moustache"), info.Moustaches, info.Head.MoustacheIndex, index => info.Head.MoustacheIndex = index);
        CreateAttachmentDropDown(content, Text("label.face", "Face"), info.FaceAttachments, info.Head.FaceAttachmentIndex, index => info.Head.FaceAttachmentIndex = index);

    }

    private void CreateGenderDropDown(GUILayoutGroup content, Character character, CharacterInfo info)
    {
        if (character == null || !character.SpeciesName.Equals(CharacterPrefab.HumanSpeciesName)) { return; }
        if (!info.Prefab.VarTags.TryGetValue("GENDER".ToIdentifier(), out ImmutableHashSet<Identifier> genders) || genders.None()) { return; }

        if (selectedGender.IsEmpty && info.Head?.Preset != null)
        {
            selectedGender = info.Head.Preset.TagSet.FirstOrDefault(genders.Contains);
        }

        GUILayoutGroup row = CreateLabeledRow(content, Text("label.gender", "Gender"));
        GUIDropDown dropDown = new GUIDropDown(new RectTransform(new Vector2(0.68f, 1.0f), row.RectTransform), selectedGender.IsEmpty ? Text("value.default", "Default") : selectedGender.Value);
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
        GUILayoutGroup row = CreateLabeledRow(content, Text("label.head", "Head"));
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
            UpdateAllViewerSpriteInfo();
            QueueGuiRecreate();
            return true;
        };
    }

    private void CreateAttachmentDropDown(GUILayoutGroup content, LocalizedString label, IReadOnlyList<ContentXElement> elements, int selectedIndex, Action<int> applyIndex)
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
            UpdateAllViewerSpriteInfo();
            QueueGuiRecreate();
            return true;
        };
    }

    private static GUILayoutGroup CreateLabeledRow(GUILayoutGroup parent, LocalizedString label)
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
        return preset == null ? Text("value.none", "None").Value : string.Join(", ", preset.TagSet.Select(t => t.Value));
    }

    private static string GetAttachmentLabel(ContentXElement element, int index)
    {
        if (element == null) { return TextWithVariables("format.indexnone", "[index]: None", ("[index]", index.ToString())).Value; }
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
        UpdateAllViewerSpriteInfo();
    }

    private void OpenModManager()
    {
        GUIMessageBox messageBox = new GUIMessageBox(Text("button.modmanager", "Mod Manager"), "", new LocalizedString[] { Text("button.cancel", "Cancel"), Text("button.apply", "Apply") }, new Vector2(0.78f, 0.88f));
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
        new GUIButton(new RectTransform(Vector2.One, buttonRow.RectTransform), Text("button.cancel", "Cancel"))
        {
            OnClicked = (_, _) =>
            {
                messageBox.Close();
                return true;
            }
        };
        new GUIButton(new RectTransform(Vector2.One, buttonRow.RectTransform), Text("button.apply", "Apply"))
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
            if (character != null && !character.Removed)
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
            UpdateAllViewerSpriteInfo();
            QueueWearableEditorRebuild();
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
}
