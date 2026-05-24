using Barotrauma;
using Barotrauma.CharacterEditor;
using Barotrauma.Extensions;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace CharacterViewer;

public sealed partial class CharacterViewerPlugin
{

    private void CreateWearableSpriteListWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow(WindowTitleWearableSpriteList, new Point(470, 260), new Point(780, 15), out wearableSpriteListWindow);
        wearableSpriteListBox = new GUIListBox(new RectTransform(Vector2.One, content.RectTransform), style: null)
        {
            AutoHideScrollBar = false
        };
        QueueWearableSpriteListRebuild();
    }

    private void PopulateWearableSpriteList()
    {
        wearableSpriteListRebuildQueued = false;
        if (wearableSpriteListBox == null) { return; }

        wearableSpriteListBox.ClearChildren();
        CreateWearableSpriteListActions();
        List<WearableSpriteSelection> entries = GetWearableSpriteSelections();
        if (entries.Count == 0)
        {
            CreateEditorMessage(wearableSpriteListBox, selectedClothingPrefab == null ? Text("message.noclothingselected", NoClothingSelectedText) : Text("message.nospriteentries", "Selected clothing has no sprite entries."));
            RefreshWearableSpriteListActionState();
            RefreshListScrollBar(wearableSpriteListBox);
            return;
        }

        foreach (WearableSpriteSelection entry in entries)
        {
            ContentXElement element = entry.Sprite.SourceElement;
            Rectangle rect = GetEffectiveSourceRect(entry.Sprite);
            string name = element.GetAttributeString("name", "");
            string label = $"{name}, {entry.Sprite.Limb}, {rect.X},{rect.Y},{rect.Width},{rect.Height}";
            bool selected = entry.Element == selectedWearableSpriteElement;
            var button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.0f), wearableSpriteListBox.Content.RectTransform)
            {
                MinSize = new Point(0, GUI.IntScale(28))
            }, label, style: "GUIButtonSmall")
            {
                UserData = entry.Element,
                TextColor = selected ? GUIStyle.Green : GUIStyle.TextColorNormal,
                Selected = selected,
                OnClicked = (_, data) =>
                {
                    SelectWearableSprite(data as XElement);
                    return true;
                }
            };
            button.ToolTip = element.Element.ToString(SaveOptions.DisableFormatting);
        }

        RefreshWearableSpriteListActionState();
        RefreshListScrollBar(wearableSpriteListBox);
    }

    private void CreateWearableSpriteListActions()
    {
        var row = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), wearableSpriteListBox.Content.RectTransform)
        {
            MinSize = new Point(0, GUI.IntScale(30))
        }, isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };

        copyWearableSpriteButton = CreateEditorButton(row, Text("button.copy", "Copy"), CopySelectedWearableSprite);
        pasteWearableSpriteButton = CreateEditorButton(row, Text("button.paste", "Paste"), PasteCopiedWearableSprite);
        deleteWearableSpriteButton = CreateEditorButton(row, Text("button.delete", "Delete"), ConfirmDeleteSelectedWearableSprite);
    }

    private void RefreshWearableSpriteListActionState()
    {
        bool hasSelection = GetSelectedWearableSpriteSelection()?.Sprite?.SourceElement != null;
        SetEditorButtonEnabled(copyWearableSpriteButton, hasSelection);
        SetEditorButtonEnabled(deleteWearableSpriteButton, hasSelection);
        SetEditorButtonEnabled(pasteWearableSpriteButton, wearableSpriteClipboard?.Element != null);
    }

    private void SelectWearableSprite(XElement element)
    {
        if (element == null || selectedWearableSpriteElement == element) { return; }
        selectedWearableSpriteElement = element;
        QueueWearableEditorRebuild();
        QueueWearableSpriteListRebuild();
        UpdateAllViewerSpriteInfo();
    }

    private void CopySelectedWearableSprite()
    {
        WearableSpriteSelection selection = GetSelectedWearableSpriteSelection();
        ContentXElement sourceElement = selection?.Sprite?.SourceElement;
        if (sourceElement?.Element == null) { return; }

        TryGetWearableXmlPath(sourceElement, out string sourceXmlPath, showWarning: false);
        sourceXmlPath = GetWearableSpriteTextureBaseXmlPath(sourceXmlPath);
        XElement clone = new XElement(sourceElement.Element);
        NormalizeCopiedTexturePath(clone, sourceElement, sourceXmlPath);
        wearableSpriteClipboard = new WearableSpriteClipboard
        {
            Element = clone,
            SourceXmlPath = sourceXmlPath,
            SourcePackage = sourceElement.ContentPackage ?? selectedClothingPrefab?.ContentPackage
        };
        QueueWearableSpriteListRebuild();
        GUI.AddMessage(Text("message.spritecopied", "Wearable sprite copied."), GUIStyle.Green, font: GUIStyle.Font, lifeTime: 3);
    }

    private void PasteCopiedWearableSprite()
    {
        if (wearableSpriteClipboard?.Element == null) { return; }
        if (!TryGetSelectedWearableParent(out ContentXElement wearableParent)) { return; }

        XElement pastedElement = new XElement(wearableSpriteClipboard.Element);
        NormalizePastedTexturePath(pastedElement);
        wearableParent.Add(new ContentXElement(selectedClothingPrefab.ContentPackage, pastedElement));
        selectedWearableSpriteElement = pastedElement;
        originalWearableSpriteElements[pastedElement] = new XElement(pastedElement);
        ReequipSelectedWearable();
        QueueWearableEditorRebuild();
        QueueWearableSpriteListRebuild();
        UpdateAllViewerSpriteInfo();
        GUI.AddMessage(Text("message.spritepasted", "Wearable sprite pasted."), GUIStyle.Green, font: GUIStyle.Font, lifeTime: 3);
    }

    private void ConfirmDeleteSelectedWearableSprite()
    {
        WearableSpriteSelection selection = GetSelectedWearableSpriteSelection();
        XElement element = selection?.Element;
        if (element == null) { return; }

        string name = element.GetAttributeString("name", "");
        string label = string.IsNullOrWhiteSpace(name) ? element.GetAttributeString("limb", Text("value.selectedsprite", "selected sprite").Value) : name;
        var messageBox = new GUIMessageBox(
            Text("messagebox.deletewearablesprite.title", "Delete Wearable Sprite"),
            TextWithVariables("messagebox.deletewearablesprite.body", "Delete sprite entry \"[label]\"?\\n\\nThis changes the editor state only. Use Save to write it to XML.", ("[label]", label)),
            new LocalizedString[] { Text("button.cancel", "Cancel"), Text("button.delete", "Delete") },
            type: GUIMessageBox.Type.Warning);
        messageBox.Buttons[0].OnClicked = (_, _) =>
        {
            messageBox.Close();
            return true;
        };
        messageBox.Buttons[1].OnClicked = (_, _) =>
        {
            messageBox.Close();
            DeleteSelectedWearableSprite(element);
            return true;
        };
    }

    private void DeleteSelectedWearableSprite(XElement element)
    {
        List<WearableSpriteSelection> entries = GetWearableSpriteSelections();
        int index = entries.FindIndex(e => e.Element == element);
        XElement nextSelection = entries
            .Where(e => e.Element != element)
            .Select(e => e.Element)
            .ElementAtOrDefault(Math.Max(0, Math.Min(index, entries.Count - 2)));

        originalWearableSpriteElements.Remove(element);
        element.Remove();
        selectedWearableSpriteElement = nextSelection;
        ReequipSelectedWearable();
        QueueWearableEditorRebuild();
        QueueWearableSpriteListRebuild();
        UpdateAllViewerSpriteInfo();
        GUI.AddMessage(Text("message.spritedeleted", "Wearable sprite deleted. Use Save to write the XML."), GUIStyle.Orange, font: GUIStyle.Font, lifeTime: 5);
    }

    private bool TryGetSelectedWearableParent(out ContentXElement wearableParent)
    {
        wearableParent = selectedClothingPrefab?.ConfigElement?.GetChildElement("Wearable");
        if (wearableParent != null) { return true; }

        new GUIMessageBox(Text("window.wearableeditor", "Wearable Editor"), Text("message.nowearableparent", "Selected clothing has no Wearable element to paste into."));
        return false;
    }

    private string GetWearableSpriteTextureBaseXmlPath(string fallbackPath)
    {
        ItemPrefab basePrefab = selectedClothingPrefab?.ParentPrefab ?? selectedClothingPrefab;
        return basePrefab?.FilePath?.FullPath ?? fallbackPath;
    }

    private bool IsSelectedWearableSpriteElement(ContentXElement element)
    {
        return element?.Element != null && element.Element == selectedWearableSpriteElement;
    }

    private void SelectWearableSpriteFromPreview(ContentXElement element)
    {
        if (!wearableEditorEnabled || element?.Element == null) { return; }
        pendingPreviewSelectedWearableSpriteElement = element.Element;
    }

    private void ApplyPendingPreviewSelection()
    {
        if (pendingPreviewSelectedWearableSpriteElement == null) { return; }

        XElement element = pendingPreviewSelectedWearableSpriteElement;
        pendingPreviewSelectedWearableSpriteElement = null;
        if (GetWearableSpriteSelections().None(selection => selection.Element == element)) { return; }
        SelectWearableSprite(element);
    }

    private void EnsureSelectedWearableSprite()
    {
        List<WearableSpriteSelection> entries = GetWearableSpriteSelections();
        if (entries.Count == 0)
        {
            selectedWearableSpriteElement = null;
            return;
        }

        if (selectedWearableSpriteElement == null || entries.None(e => e.Element == selectedWearableSpriteElement))
        {
            selectedWearableSpriteElement = entries[0].Element;
            QueueWearableEditorRebuild();
            QueueWearableSpriteListRebuild();
            UpdateAllViewerSpriteInfo();
        }
    }

    private WearableSpriteSelection GetSelectedWearableSpriteSelection()
    {
        EnsureSelectedWearableSprite();
        return GetWearableSpriteSelections().FirstOrDefault(e => e.Element == selectedWearableSpriteElement);
    }

    private List<WearableSpriteSelection> GetWearableSpriteSelections()
    {
        return GetSelectedWearableSprites()
            .Where(static tuple => tuple.sprite?.SourceElement != null)
            .Select(tuple => new WearableSpriteSelection { Limb = tuple.limb, Sprite = tuple.sprite })
            .ToList();
    }
}
