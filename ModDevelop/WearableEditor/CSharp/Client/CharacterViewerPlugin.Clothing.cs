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

    private void CreateClothingWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow(WindowTitleClothingManager, new Point(470, 250), new Point(300, 15), out clothingWindow);

        searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.08f), content.RectTransform), searchText, style: "GUITextBox")
        {
            OnTextChangedDelegate = (_, text) =>
            {
                searchText = text ?? string.Empty;
                PopulateClothingDropDown();
                return true;
            }
        };

        clothingDropDown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.09f), content.RectTransform), Text("dropdown.selectclothing", "Select clothing"));
        clothingDropDown.OnSelected = (_, data) =>
        {
            if (suppressClothingSelection) { return true; }
            selectedClothingPrefab = data as ItemPrefab;
            EquipViewerClothing(selectedClothingPrefab);
            QueueWearableEditorRebuild();
            return true;
        };
        PopulateClothingDropDown();

        GUILayoutGroup navRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.09f), content.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        CreateButton(navRow, Text("button.prev", "Prev"), () => SelectAdjacentClothing(-1));
        CreateButton(navRow, Text("button.next", "Next"), () => SelectAdjacentClothing(1));
        CreateButton(navRow, Text("button.reload", "Reload"), () => EquipViewerClothing(selectedClothingPrefab));
        CreateButton(navRow, Text("button.remove", "Remove"), () =>
        {
            selectedClothingPrefab = null;
            ClearViewerClothing();
            UpdateClothingInfo(Text("message.noclothingselected", NoClothingSelectedText));
            UpdateAllViewerSpriteInfo();
            PopulateClothingDropDown();
            QueueWearableEditorRebuild();
        });

        clothingInfoText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.38f), content.RectTransform), "", wrap: true, font: GUIStyle.SmallFont);
        statusText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.08f), content.RectTransform), "", wrap: true, font: GUIStyle.SmallFont)
        {
            TextColor = GUIStyle.TextColorDim
        };

        UpdateClothingInfo();
    }

    private static void CreateButton(GUILayoutGroup parent, LocalizedString text, Action onClicked)
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
            clothingDropDown.Text = prefabs.Count == 0 ? Text("dropdown.nomatchingwearables", "No matching wearable items") : Text("dropdown.selectclothing", "Select clothing");
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
        QueueWearableEditorRebuild();
    }

    private void EquipViewerClothing(ItemPrefab prefab)
    {
        ClearViewerClothing();
        if (prefab == null)
        {
            UpdateClothingInfo(Text("message.noclothingselected", NoClothingSelectedText));
            UpdateAllViewerSpriteInfo();
            QueueWearableEditorRebuild();
            return;
        }

        Character character = CurrentCharacter;
        if (character?.Inventory == null)
        {
            UpdateClothingInfo(Text("message.nocharacterinventory", "No character inventory available."));
            UpdateAllViewerSpriteInfo();
            QueueWearableEditorRebuild();
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
                SafeRemoveItem(item);
                UpdateClothingInfo(Text("message.nowearablecomponent", "Selected item has no wearable component."));
                UpdateAllViewerSpriteInfo();
                QueueWearableEditorRebuild();
                return;
            }
            if (pickable == null)
            {
                SafeRemoveItem(item);
                UpdateClothingInfo(Text("message.nopickablecomponent", "Selected item has no pickable component."));
                UpdateAllViewerSpriteInfo();
                QueueWearableEditorRebuild();
                return;
            }

            List<InvSlotType> allowedSlots = item.GetComponents<Pickable>().Count() > 1 ?
                new List<InvSlotType>(wearable.AllowedSlots) :
                new List<InvSlotType>(item.AllowedSlots);
            allowedSlots.Remove(InvSlotType.Any);

            if (!character.Inventory.TryPutItem(item, null, allowedSlots, createNetworkEvent: false))
            {
                SafeRemoveItem(item);
                UpdateClothingInfo(Text("message.couldnotequip", "Could not equip the selected item in any allowed slot."));
                UpdateAllViewerSpriteInfo();
                QueueWearableEditorRebuild();
                return;
            }

            wearable.Equip(character);
            viewerEquippedItems.Add(item);
            UpdateClothingInfo(Text("status.equippedforpreview", "Equipped for preview."));
            UpdateAllViewerSpriteInfo();
            QueueWearableEditorRebuild();
        }
        catch (Exception ex)
        {
            SafeRemoveItem(item);
            LuaCsLogger.LogError($"CharacterViewer failed to equip {prefab.Identifier}: {ex}");
            UpdateClothingInfo(Text("message.failedtoequip", "Failed to equip selected clothing. See console for details."));
            UpdateAllViewerSpriteInfo();
            QueueWearableEditorRebuild();
        }
    }

    private void ClearViewerClothing()
    {
        Character character = CurrentCharacter is { Removed: false } currentCharacter ? currentCharacter : null;
        Item[] items = viewerEquippedItems.ToArray();
        viewerEquippedItems.Clear();
        foreach (Item item in items)
        {
            try
            {
                if (item == null || item.Removed) { continue; }
                item.GetComponent<Wearable>()?.Unequip(character);
                item.ParentInventory?.RemoveItem(item);
                SafeRemoveItem(item);
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"CharacterViewer failed to remove preview item: {ex}");
            }
        }
    }

    private static void SafeRemoveItem(Item item)
    {
        if (item == null || item.Removed) { return; }
        item.Remove();
    }

    private void UpdateClothingInfo(LocalizedString state = null)
    {
        if (clothingInfoText == null) { return; }
        if (selectedClothingPrefab == null)
        {
            clothingInfoText.Text = Text("message.noclothingselected", NoClothingSelectedText);
        }
        else
        {
            int spriteCount = selectedClothingPrefab.ConfigElement
                .GetChildElements("Wearable")
                .SelectMany(w => w.GetChildElements("sprite"))
                .Count();
            clothingInfoText.Text = TextWithVariables(
                "info.clothing",
                "Name: [name]\\nIdentifier: [identifier]\\nWearable sprites: [count]\\nXML: [xml]",
                ("[name]", selectedClothingPrefab.Name.Value),
                ("[identifier]", selectedClothingPrefab.Identifier.Value),
                ("[count]", spriteCount.ToString()),
                ("[xml]", selectedClothingPrefab.FilePath.Value));
        }
        if (statusText != null)
        {
            statusText.Text = state ?? "";
        }
    }
}
