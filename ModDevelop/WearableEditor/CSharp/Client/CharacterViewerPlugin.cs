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

public sealed partial class CharacterViewerPlugin : IAssemblyPlugin
{

    private const string HarmonyId = "CharacterViewer.Example";

    private const string WindowTitleCharacterViewer = "Character Viewer";

    private const string WindowTitleClothingManager = "Clothing Manager";

    private const string WindowTitleBodySprite = "Body Sprite";

    private const string WindowTitleHeadSprite = "Head Sprite";

    private const string WindowTitleClothingSprite = "Clothing Sprite";

    private const string WindowTitleWearableSpriteList = "Wearable Sprite List";

    private const string ResizeHandleUserData = "CharacterViewer.ResizeHandle";

    private const string ModManagerButtonUserData = "CharacterViewer.ModManager";

    private const string PanelToggleUserData = "CharacterViewer.PanelToggle";

    private const string WearableEditorToggleUserData = "CharacterViewer.WearableEditorToggle";

    private const string NoClothingSelectedText = "No clothing selected.";

    private sealed class FloatingWindowState
    {
        public FloatingWindowState(string title, Point defaultLogicalSize, Point defaultLogicalOffset)
        {
            Title = title;
            DefaultLogicalSize = defaultLogicalSize;
            DefaultLogicalOffset = defaultLogicalOffset;
            MinimumLogicalSize = defaultLogicalSize;
            SavedLogicalSize = defaultLogicalSize;
            SavedLogicalOffset = defaultLogicalOffset;
        }

        public string Title { get; }
        public Point DefaultLogicalSize { get; }
        public Point DefaultLogicalOffset { get; }
        public Point MinimumLogicalSize { get; set; }
        public Point SavedLogicalSize { get; set; }
        public Point SavedLogicalOffset { get; set; }
    }

    private static CharacterViewerPlugin instance;

    private readonly List<Item> viewerEquippedItems = new List<Item>();

    private readonly Dictionary<string, FloatingWindowState> floatingWindowStates = new Dictionary<string, FloatingWindowState>();

    private Harmony harmony;

    private GUIFrame headWindow;

    private GUIFrame clothingWindow;

    private GUIFrame bodySpriteWindow;

    private GUIFrame headSpriteWindow;

    private GUIFrame clothingSpriteWindow;

    private GUIFrame wearableSpriteListWindow;

    private GUITextBlock clothingInfoText;

    private GUITextBlock statusText;

    private GUITextBox searchBox;

    private GUIDropDown clothingDropDown;

    private GUIListBox bodySpriteInfoList;

    private GUIListBox headSpriteInfoList;

    private GUIListBox clothingSpriteInfoList;

private GUIListBox wearableSpriteListBox;

private GUICustomComponent spritePreviewTooltipOverlay;

private string spritePreviewTooltip;

private ItemPrefab selectedClothingPrefab;

    private string searchText = string.Empty;

    private Identifier selectedGender = Identifier.Empty;

    private float bodySpritePreviewZoom = 1.0f;

    private float headSpritePreviewZoom = 1.0f;

    private float clothingSpritePreviewZoom = 1.0f;

    private bool recreateGuiQueued = true;

    private bool applyingGender;

    private bool suppressClothingSelection;

    private bool panelsEnabled;

    private GUIFrame draggedWindow;

    private Vector2 draggedWindowOffset;

    private GUIFrame resizedWindow;

    private Point resizedWindowStartSize;

    private Point resizedWindowStartMouse;

    private Point lastGuiViewportSize;

    private float lastGuiScale = -1.0f;

    private static Character CurrentCharacter => GameMain.CharacterEditorScreen?.SpawnedCharacter;
}
