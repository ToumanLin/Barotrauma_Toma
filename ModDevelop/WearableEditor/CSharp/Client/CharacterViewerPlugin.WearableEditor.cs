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

    private readonly Dictionary<XElement, XElement> originalWearableSpriteElements = new Dictionary<XElement, XElement>();

    private bool wearableEditorEnabled;

    private bool wearableEditorRebuildQueued;

    private bool wearableSpriteListRebuildQueued;

    private ItemPrefab wearableEditorPrefab;

    private XElement selectedWearableSpriteElement;

    private XElement pendingPreviewSelectedWearableSpriteElement;

    private WearableSpriteClipboard wearableSpriteClipboard;

    private GUIButton copyWearableSpriteButton;

    private GUIButton pasteWearableSpriteButton;

    private GUIButton deleteWearableSpriteButton;

    private sealed class WearableSpriteSelection
    {
        public Limb Limb;
        public WearableSprite Sprite;
        public XElement Element => Sprite?.SourceElement?.Element;
    }

    private sealed class WearableSpriteClipboard
    {
        public XElement Element;
        public string SourceXmlPath;
        public ContentPackage SourcePackage;
    }

    private void SetWearableEditorEnabled(bool enabled)
    {
        if (wearableEditorEnabled == enabled && !enabled) { return; }

        wearableEditorEnabled = enabled;
        SyncWearableEditorToggle();

        if (enabled)
        {
            DisableVanillaEditorModes();
            SetParamsEditorVisible(true);
            QueueWearableEditorRebuild();
            QueueWearableSpriteListRebuild();
        }
        else
        {
            wearableEditorPrefab = null;
            wearableEditorRebuildQueued = false;
            wearableSpriteListRebuildQueued = false;
            selectedWearableSpriteElement = null;
            ParamsEditor.Instance.Clear();
            RemoveWindow(wearableSpriteListWindow);
            wearableSpriteListWindow = null;
            wearableSpriteListBox = null;
            copyWearableSpriteButton = null;
            pasteWearableSpriteButton = null;
            deleteWearableSpriteButton = null;
            UpdateAllViewerSpriteInfo();
        }
    }

    private void UpdateWearableEditor()
    {
        if (!wearableEditorEnabled) { return; }

        DisableVanillaEditorModes();
        ApplyPendingPreviewSelection();

        if (wearableEditorPrefab != selectedClothingPrefab)
        {
            selectedWearableSpriteElement = null;
            QueueWearableEditorRebuild();
            QueueWearableSpriteListRebuild();
        }

        EnsureSelectedWearableSprite();

        if (wearableSpriteListRebuildQueued)
        {
            PopulateWearableSpriteList();
        }

        if (wearableEditorRebuildQueued)
        {
            PopulateWearableEditorParams();
        }
    }

    private void QueueWearableEditorRebuild()
    {
        wearableEditorRebuildQueued = true;
    }

    private void QueueWearableSpriteListRebuild()
    {
        wearableSpriteListRebuildQueued = true;
    }

    private void SyncWearableEditorToggle()
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        if (editor == null) { return; }

        GUIFrame modesPanel = AccessTools.Field(editor.GetType(), "modesPanel")?.GetValue(editor) as GUIFrame;
        var tickBox = modesPanel?.GetAllChildren().OfType<GUITickBox>().FirstOrDefault(c => c.UserData as string == WearableEditorToggleUserData);
        if (tickBox != null)
        {
            tickBox.Selected = wearableEditorEnabled;
        }
    }

    private void DisableVanillaEditorModes()
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        if (editor == null) { return; }

        foreach (string fieldName in new[] { "editCharacterInfo", "editRagdoll", "editAnimations", "editLimbs", "editJoints", "editIK" })
        {
            AccessTools.Field(editor.GetType(), fieldName)?.SetValue(editor, false);
        }

        foreach (string fieldName in new[] { "characterInfoToggle", "ragdollToggle", "animsToggle", "limbsToggle", "jointsToggle", "ikToggle" })
        {
            if (AccessTools.Field(editor.GetType(), fieldName)?.GetValue(editor) is GUITickBox toggle)
            {
                toggle.Selected = false;
            }
        }
    }

    private void SetParamsEditorVisible(bool visible)
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        if (editor == null) { return; }

        AccessTools.Field(editor.GetType(), "showParamsEditor")?.SetValue(editor, visible);
        if (AccessTools.Field(editor.GetType(), "paramsToggle")?.GetValue(editor) is GUITickBox paramsToggle)
        {
            paramsToggle.Selected = visible;
        }
    }
}
