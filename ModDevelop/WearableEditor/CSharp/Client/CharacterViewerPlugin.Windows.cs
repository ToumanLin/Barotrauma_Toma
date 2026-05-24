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

    private void AddWindowsToGuiUpdateList()
    {
        if (Screen.Selected is not CharacterEditorScreen) { return; }
        RefreshGuiLayoutIfNeeded();
        EnsureEditorPanelControls();
        if ((!panelsEnabled && !wearableEditorEnabled) || IsBlockingGameMenuOpen())
        {
            RemoveWindows();
            return;
        }

        if (panelsEnabled &&
            (recreateGuiQueued || headWindow == null || clothingWindow == null ||
             bodySpriteWindow == null || headSpriteWindow == null || clothingSpriteWindow == null))
        {
            RecreateWindows();
        }
        else if (!panelsEnabled && (headWindow != null || clothingWindow != null ||
            bodySpriteWindow != null || headSpriteWindow != null || clothingSpriteWindow != null))
        {
            RemoveViewerWindows();
        }

        if (wearableEditorEnabled && wearableSpriteListWindow == null)
        {
            CreateWearableSpriteListWindow();
        }
        else if (!wearableEditorEnabled && wearableSpriteListWindow != null)
        {
            RemoveWindow(wearableSpriteListWindow);
            wearableSpriteListWindow = null;
            wearableSpriteListBox = null;
        }

        if (panelsEnabled)
        {
            foreach (GUIFrame window in GetViewerWindows())
            {
                window?.AddToGUIUpdateList(ignoreChildren: false, order: 1);
            }
        }
        wearableSpriteListWindow?.AddToGUIUpdateList(ignoreChildren: false, order: 1);
        if (panelsEnabled)
        {
            spritePreviewTooltip = null;
            EnsureSpritePreviewTooltipOverlay();
            spritePreviewTooltipOverlay?.AddToGUIUpdateList(ignoreChildren: false, order: 100);
        }
    }

    private void OnCharacterEditorUpdated(float deltaTime)
    {
        if (Screen.Selected is not CharacterEditorScreen) { return; }

        RefreshGuiLayoutIfNeeded();
        EnsureEditorPanelControls();
        UpdateShortcuts();
        UpdateWearableEditor();
        UpdateInGameBehavior(deltaTime);

        if ((panelsEnabled || wearableEditorEnabled) && !IsBlockingGameMenuOpen())
        {
            UpdateWindowDragging();
        }
    }

    private void UpdateShortcuts()
    {
        if (Screen.Selected is not CharacterEditorScreen) { return; }
        if (GUI.KeyboardDispatcher.Subscriber != null) { return; }
        if (PlayerInput.KeyHit(Keys.D6))
        {
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
        else if (PlayerInput.KeyHit(Keys.D7))
        {
            SetWearableEditorEnabled(!wearableEditorEnabled);
        }
        else if (PlayerInput.KeyHit(Keys.H))
        {
            SetInGameBehaviorEnabled(!inGameBehaviorEnabled);
        }
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
        CreateBodySpriteWindow();
        CreateHeadSpriteWindow();
        CreateClothingSpriteWindow();
        UpdateAllViewerSpriteInfo();
    }

    private void RemoveWindows()
    {
        RemoveViewerWindows();
        RemoveWindow(wearableSpriteListWindow);
        wearableSpriteListWindow = null;
        wearableSpriteListBox = null;
        RemoveSpritePreviewTooltipOverlay();
        draggedWindow = null;
        resizedWindow = null;
    }

    private void RemoveViewerWindows()
    {
        RemoveWindow(headWindow);
        RemoveWindow(clothingWindow);
        RemoveWindow(bodySpriteWindow);
        RemoveWindow(headSpriteWindow);
        RemoveWindow(clothingSpriteWindow);
        headWindow = null;
        clothingWindow = null;
        bodySpriteWindow = null;
        headSpriteWindow = null;
        clothingSpriteWindow = null;
        clothingInfoText = null;
        statusText = null;
        searchBox = null;
        clothingDropDown = null;
        bodySpriteInfoList = null;
        headSpriteInfoList = null;
        clothingSpriteInfoList = null;
        spriteListsPendingScrollReset.Clear();
        spriteHorizontalScrollBars.Clear();
        spriteHorizontalScrollOffsets.Clear();
        spriteCanvasWidths.Clear();
        spriteCanvasHeights.Clear();
        RemoveSpritePreviewTooltipOverlay();
        draggedWindow = null;
        resizedWindow = null;
    }

    private static void RemoveWindow(GUIFrame window)
    {
        if (window == null) { return; }
        window.RemoveFromGUIUpdateList();
        window.RectTransform.Parent = null;
    }

    private void EnsureSpritePreviewTooltipOverlay()
    {
        if (spritePreviewTooltipOverlay != null) { return; }

        spritePreviewTooltipOverlay = new GUICustomComponent(
            new RectTransform(Vector2.One, GUI.Canvas),
            onDraw: (spriteBatch, _) =>
            {
                if (string.IsNullOrEmpty(spritePreviewTooltip)) { return; }
                GUIComponent.DrawToolTip(spriteBatch, spritePreviewTooltip, PlayerInput.MousePosition + new Vector2(GUI.IntScale(20), GUI.IntScale(20)));
            })
        {
            CanBeFocused = false
        };
    }

    private void RemoveSpritePreviewTooltipOverlay()
    {
        if (spritePreviewTooltipOverlay == null) { return; }
        spritePreviewTooltipOverlay.RemoveFromGUIUpdateList();
        spritePreviewTooltipOverlay.RectTransform.Parent = null;
        spritePreviewTooltipOverlay = null;
        spritePreviewTooltip = null;
    }

    private GUILayoutGroup CreateFloatingWindow(string title, Point size, Point defaultOffset, out GUIFrame window)
    {
        FloatingWindowState state = GetFloatingWindowState(title, size, defaultOffset);
        state.SavedLogicalSize = ClampWindowSize(state.SavedLogicalSize, state.MinimumLogicalSize);
        state.SavedLogicalOffset = ClampWindowOffset(state.SavedLogicalOffset, state.SavedLogicalSize);
        window = new GUIFrame(
            new RectTransform(state.SavedLogicalSize.Multiply(GUI.Scale), GUI.Canvas, Anchor.TopLeft, Pivot.TopLeft)
            {
                AbsoluteOffset = state.SavedLogicalOffset.Multiply(GUI.Scale),
                MinSize = state.MinimumLogicalSize.Multiply(GUI.Scale)
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

        GUIFrame header = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), outer.RectTransform)
        {
            MinSize = new Point(0, GUI.IntScale(38)),
            MaxSize = new Point(int.MaxValue, GUI.IntScale(38))
        }, style: "GUIFrameListBox");
        new GUITextBlock(new RectTransform(new Vector2(0.94f, 1.0f), header.RectTransform, Anchor.CenterLeft), title, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
        {
            CanBeFocused = false
        };

        GUILayoutGroup content = new GUILayoutGroup(new RectTransform(new Vector2(0.96f, 1.0f), outer.RectTransform, Anchor.Center)
        {
            MinSize = new Point(0, GUI.IntScale(40))
        }, isHorizontal: false, childAnchor: Anchor.TopLeft)
        {
            Stretch = true,
            AbsoluteSpacing = GUI.IntScale(5)
        };

        var resizeHandle = new GUIFrame(new RectTransform(new Point(18, 18).Multiply(GUI.Scale), window.RectTransform, Anchor.BottomRight, Pivot.BottomRight), style: "GUIFrameListBox")
        {
            UserData = ResizeHandleUserData,
            ToolTip = "Resize"
        };
        new GUITextBlock(new RectTransform(Vector2.One, resizeHandle.RectTransform), "/", font: GUIStyle.SmallFont, textAlignment: Alignment.Center)
        {
            CanBeFocused = false
        };

        return content;
    }

    private void RefreshGuiLayoutIfNeeded()
    {
        Point viewportSize = GetGuiViewportSize();
        float guiScale = GUI.Scale;
        if (lastGuiViewportSize == Point.Zero || lastGuiScale < 0.0f)
        {
            lastGuiViewportSize = viewportSize;
            lastGuiScale = guiScale;
            return;
        }

        if (lastGuiViewportSize == viewportSize && Math.Abs(lastGuiScale - guiScale) < 0.001f)
        {
            return;
        }

        StoreCurrentWindowLayouts();

        lastGuiViewportSize = viewportSize;
        lastGuiScale = guiScale;
        draggedWindow = null;
        resizedWindow = null;

        RemoveWindows();
        QueueGuiRecreate();
        if (wearableEditorEnabled)
        {
            QueueWearableSpriteListRebuild();
            QueueWearableEditorRebuild();
        }
    }

    private static Point GetGuiViewportSize()
    {
        Rectangle canvasRect = GUI.Canvas.Rect;
        return new Point(canvasRect.Width, canvasRect.Height);
    }

    private static Point GetGuiViewportNonScaledSize()
    {
        Point viewportSize = GetGuiViewportSize();
        float scale = Math.Max(0.001f, GUI.Scale);
        return new Point((int)(viewportSize.X / scale), (int)(viewportSize.Y / scale));
    }

    private static Point ToGuiScaleIndependentSize(Point size)
    {
        float scale = Math.Max(0.001f, GUI.Scale);
        return new Point((int)(size.X / scale), (int)(size.Y / scale));
    }

    private FloatingWindowState GetFloatingWindowState(string title, Point defaultLogicalSize, Point defaultLogicalOffset)
    {
        if (!floatingWindowStates.TryGetValue(title, out FloatingWindowState state))
        {
            state = new FloatingWindowState(title, defaultLogicalSize, defaultLogicalOffset);
            floatingWindowStates[title] = state;
        }
        state.MinimumLogicalSize = defaultLogicalSize;
        return state;
    }

    private static Point ClampWindowSize(Point requestedSize, Point minimumSize)
    {
        Point viewportSize = GetGuiViewportNonScaledSize();
        int maxWidth = Math.Max(minimumSize.X, viewportSize.X);
        int maxHeight = Math.Max(minimumSize.Y, viewportSize.Y);
        return new Point(
            Math.Min(Math.Max(requestedSize.X, minimumSize.X), maxWidth),
            Math.Min(Math.Max(requestedSize.Y, minimumSize.Y), maxHeight));
    }

    private static Point ClampWindowOffset(Point requestedOffset, Point windowSize)
    {
        Point viewportSize = GetGuiViewportNonScaledSize();
        int maxX = Math.Max(0, viewportSize.X - windowSize.X);
        int maxY = Math.Max(0, viewportSize.Y - windowSize.Y);
        return new Point(
            MathHelper.Clamp(requestedOffset.X, 0, maxX),
            MathHelper.Clamp(requestedOffset.Y, 0, maxY));
    }

    private void StoreCurrentWindowLayout(GUIFrame window)
    {
        if (window?.UserData is not string title) { return; }
        if (!floatingWindowStates.TryGetValue(title, out FloatingWindowState state)) { return; }

        float scale = Math.Max(0.001f, lastGuiScale > 0.0f ? lastGuiScale : GUI.Scale);
        Point absoluteOffset = window.RectTransform.AbsoluteOffset;
        Point screenOffset = window.RectTransform.ScreenSpaceOffset;
        state.SavedLogicalOffset = new Point(
            (int)((absoluteOffset.X + screenOffset.X) / scale),
            (int)((absoluteOffset.Y + screenOffset.Y) / scale));
    }

    private void StoreCurrentWindowLayouts()
    {
        foreach (GUIFrame window in GetAllFloatingWindows())
        {
            StoreCurrentWindowLayout(window);
        }
    }

    private IEnumerable<GUIFrame> GetViewerWindows()
    {
        yield return headWindow;
        yield return clothingWindow;
        yield return bodySpriteWindow;
        yield return headSpriteWindow;
        yield return clothingSpriteWindow;
    }

    private IEnumerable<GUIFrame> GetAllFloatingWindows()
    {
        foreach (GUIFrame window in GetViewerWindows())
        {
            yield return window;
        }
        yield return wearableSpriteListWindow;
    }

    private void UpdateWindowDragging()
    {
        if (UpdateWindowResizing())
        {
            return;
        }

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
            if (!floatingWindowStates.TryGetValue(title, out FloatingWindowState state))
            {
                draggedWindow = null;
                return;
            }
            Point absoluteOffset = draggedWindow.RectTransform.AbsoluteOffset;
            Point screenOffset = draggedWindow.RectTransform.ScreenSpaceOffset;
            Point storedSize = ToGuiScaleIndependentSize(draggedWindow.RectTransform.NonScaledSize);
            Point storedOffset = new Point(
                (int)((absoluteOffset.X + screenOffset.X) / GUI.Scale),
                (int)((absoluteOffset.Y + screenOffset.Y) / GUI.Scale));
            state.SavedLogicalOffset = ClampWindowOffset(storedOffset, storedSize);
            draggedWindow.RectTransform.AbsoluteOffset += screenOffset;
            draggedWindow.RectTransform.ScreenSpaceOffset = Point.Zero;
            draggedWindow.RectTransform.AbsoluteOffset = state.SavedLogicalOffset.Multiply(GUI.Scale);
            draggedWindow = null;
        }
    }

    private bool UpdateWindowResizing()
    {
        GUIFrame hoverWindow = GetResizeHoveredWindow();
        if (PlayerInput.PrimaryMouseButtonDown() && hoverWindow != null)
        {
            resizedWindow = hoverWindow;
            resizedWindowStartSize = ToGuiScaleIndependentSize(resizedWindow.RectTransform.NonScaledSize);
            resizedWindowStartMouse = PlayerInput.MousePosition.ToPoint();
        }

        if (PlayerInput.PrimaryMouseButtonHeld() && resizedWindow != null)
        {
            GUI.MouseCursor = CursorState.Dragging;
            string title = resizedWindow.UserData as string;
            Point minSize = title != null && floatingWindowStates.TryGetValue(title, out FloatingWindowState state) ? state.MinimumLogicalSize : resizedWindowStartSize;
            Point mouseDelta = PlayerInput.MousePosition.ToPoint() - resizedWindowStartMouse;
            Point newSize = new Point(
                Math.Max(minSize.X, resizedWindowStartSize.X + (int)(mouseDelta.X / GUI.Scale)),
                Math.Max(minSize.Y, resizedWindowStartSize.Y + (int)(mouseDelta.Y / GUI.Scale)));
            Point viewportSize = GetGuiViewportNonScaledSize();
            newSize.X = Math.Min(newSize.X, Math.Max(minSize.X, viewportSize.X));
            newSize.Y = Math.Min(newSize.Y, Math.Max(minSize.Y, viewportSize.Y));
            Point scaledMinSize = minSize.Multiply(GUI.Scale);
            Point scaledNewSize = newSize.Multiply(GUI.Scale);
            scaledNewSize.X = Math.Max(scaledMinSize.X, scaledNewSize.X);
            scaledNewSize.Y = Math.Max(scaledMinSize.Y, scaledNewSize.Y);
            resizedWindow.RectTransform.MinSize = scaledMinSize;
            resizedWindow.RectTransform.Resize(scaledNewSize, resizeChildren: true);
            return true;
        }

        if (resizedWindow?.UserData is string resizedTitle)
        {
            if (!floatingWindowStates.TryGetValue(resizedTitle, out FloatingWindowState state))
            {
                resizedWindow = null;
                QueueGuiRecreate();
                return true;
            }
            Point storedSize = ToGuiScaleIndependentSize(resizedWindow.RectTransform.NonScaledSize);
            state.SavedLogicalSize = ClampWindowSize(storedSize, state.MinimumLogicalSize);
            state.SavedLogicalOffset = ClampWindowOffset(state.SavedLogicalOffset, state.SavedLogicalSize);
            resizedWindow = null;
            QueueGuiRecreate();
            return true;
        }

        return false;
    }

    private GUIFrame GetHoveredWindow()
    {
        foreach (GUIFrame window in GetAllFloatingWindows())
        {
            if (IsWindowHeaderHovered(window)) { return window; }
        }
        return null;
    }

    private GUIFrame GetResizeHoveredWindow()
    {
        foreach (GUIFrame window in GetAllFloatingWindows())
        {
            if (IsResizeHandleHovered(window)) { return window; }
        }
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

    private static bool IsResizeHandleHovered(GUIFrame window)
    {
        if (window == null) { return false; }
        int handleSize = GUI.IntScale(24);
        Rectangle handleRect = new Rectangle(window.Rect.Right - handleSize, window.Rect.Bottom - handleSize, handleSize, handleSize);
        return handleRect.Contains(PlayerInput.MousePosition);
    }

    private static bool IsInteractiveChild(GUIComponent component)
    {
        return component is GUIButton or GUIDropDown or GUITextBox or GUIListBox or GUITickBox;
    }

    private static bool IsBlockingGameMenuOpen()
    {
        return GUIMessageBox.VisibleBox != null || GUI.PauseMenuOpen || GUI.SettingsMenuOpen;
    }
}
