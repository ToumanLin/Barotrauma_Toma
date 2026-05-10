using Barotrauma;
using Barotrauma.LuaCs;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace UiOverlayExample;

public sealed class UiOverlayExamplePlugin : IAssemblyPlugin
{
    private static UiOverlayExamplePlugin instance;

    private Harmony harmony;
    private UpdatingFrame overlayRoot;
    private GUITextBlock screenText;
    private GUITextBlock characterText;
    private bool isHidden;

    public void PreInitPatching()
    {
    }

    public void Initialize()
    {
        instance = this;
        CreateOverlay();
        PatchGuiUpdateLists();
        LuaCsLogger.Log("UiOverlayExample loaded.");
    }

    public void OnLoadCompleted()
    {
    }

    public void Dispose()
    {
        if (overlayRoot != null)
        {
            overlayRoot.RemoveFromGUIUpdateList();
            overlayRoot.RectTransform.Parent = null;
            overlayRoot = null;
        }

        harmony?.UnpatchSelf();
        harmony = null;
        if (instance == this)
        {
            instance = null;
        }

        screenText = null;
        characterText = null;
        LuaCsLogger.Log("UiOverlayExample disposed.");
    }

    private void PatchGuiUpdateLists()
    {
        harmony = new Harmony("UiOverlayExample.GuiUpdateList");
        MethodInfo postfix = AccessTools.Method(typeof(UiOverlayExamplePlugin), nameof(AddOverlayToGuiUpdateList));

        PatchAddToGuiUpdateList("Barotrauma.MainMenuScreen", postfix);
        PatchAddToGuiUpdateList("Barotrauma.GameScreen", postfix);
        PatchAddToGuiUpdateList("Barotrauma.SubEditorScreen", postfix);
        PatchAddToGuiUpdateList("Barotrauma.NetLobbyScreen", postfix);
        PatchAddToGuiUpdateList("Barotrauma.CharacterEditor.CharacterEditorScreen", postfix);
    }

    private void PatchAddToGuiUpdateList(string typeName, MethodInfo postfix)
    {
        var targetType = AccessTools.TypeByName(typeName);
        var targetMethod = targetType == null ? null : AccessTools.Method(targetType, "AddToGUIUpdateList");
        if (targetMethod == null)
        {
            LuaCsLogger.LogError($"UiOverlayExample could not patch {typeName}.AddToGUIUpdateList.");
            return;
        }

        harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
    }

    private static void AddOverlayToGuiUpdateList()
    {
        instance?.AddOverlayToCurrentGuiUpdateList();
    }

    private void AddOverlayToCurrentGuiUpdateList()
    {
        if (overlayRoot == null || isHidden) { return; }

        overlayRoot.AddToGUIUpdateList(ignoreChildren: false, order: 1);
    }

    private void CreateOverlay()
    {
        overlayRoot = new UpdatingFrame(
            new RectTransform(new Point(320, 230), GUI.Canvas, Anchor.TopRight, Pivot.TopRight)
            {
                AbsoluteOffset = new Point(-20, 20)
            },
            UpdateOverlay,
            style: "GUIFrame")
        {
            CanBeFocused = false
        };

        var layout = new GUILayoutGroup(
            new RectTransform(new Vector2(0.92f, 0.86f), overlayRoot.RectTransform, Anchor.Center),
            isHorizontal: false,
            childAnchor: Anchor.TopLeft)
        {
            Stretch = true,
            AbsoluteSpacing = 4
        };

        new GUITextBlock(
            new RectTransform(new Vector2(1.0f, 0.24f), layout.RectTransform),
            "LuaCs UI Example",
            textColor: Color.Cyan,
            textAlignment: Alignment.CenterLeft);

        screenText = new GUITextBlock(
            new RectTransform(new Vector2(1.0f, 0.22f), layout.RectTransform),
            "Screen: unknown",
            textColor: Color.White,
            textAlignment: Alignment.CenterLeft);

        characterText = new GUITextBlock(
            new RectTransform(new Vector2(1.0f, 0.22f), layout.RectTransform),
            "Character: none",
            textColor: Color.White,
            textAlignment: Alignment.CenterLeft);

        var hideButton = new GUIButton(
            new RectTransform(new Vector2(0.42f, 0.26f), layout.RectTransform),
            "Hide",
            textAlignment: Alignment.Center)
        {
            OnClicked = (_, _) =>
            {
                isHidden = true;
                overlayRoot.RemoveFromGUIUpdateList();
                return true;
            }
        };

        hideButton.ToolTip = "Hide this example overlay until the mod is reloaded.";
    }

    private void UpdateOverlay(float deltaTime)
    {
        if (screenText == null || characterText == null) { return; }

        string screenName = Screen.Selected?.GetType().Name ?? "none";
        string characterName = Character.Controlled?.DisplayName ?? "none";

        screenText.Text = $"Screen: {screenName}";
        characterText.Text = $"Character: {characterName}";
    }

    private sealed class UpdatingFrame : GUIFrame
    {
        private readonly System.Action<float> onUpdate;

        public UpdatingFrame(RectTransform rectT, System.Action<float> onUpdate, string style = "", Color? color = null)
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
