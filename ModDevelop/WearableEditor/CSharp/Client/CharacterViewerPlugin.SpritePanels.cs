using Barotrauma;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CharacterViewer;

public sealed partial class CharacterViewerPlugin
{
    private readonly HashSet<GUIListBox> spriteListsPendingScrollReset = new HashSet<GUIListBox>();
    private readonly Dictionary<GUIListBox, GUIScrollBar> spriteHorizontalScrollBars = new Dictionary<GUIListBox, GUIScrollBar>();
    private readonly Dictionary<GUIListBox, float> spriteHorizontalScrollOffsets = new Dictionary<GUIListBox, float>();
    private readonly Dictionary<GUIListBox, int> spriteCanvasWidths = new Dictionary<GUIListBox, int>();
    private readonly Dictionary<GUIListBox, int> spriteCanvasHeights = new Dictionary<GUIListBox, int>();

    private sealed class ViewerSpriteEntry
    {
        public string Title;
        public string Subtitle;
        public Sprite Sprite;
        public Rectangle SourceRect;
        public Vector2 RelativeOrigin;
        public float Scale = 1.0f;
        public bool InheritSourceRect;
        public bool InheritOrigin;
        public string FilePath;
        public ContentXElement SourceElement;

        public string Tooltip =>
            TextWithVariables(
                "tooltip.spriteentry",
                "[title]\\nFile: [file]\\nRect: [rect][inheritedrect]\\nOrigin: [origin][inheritedorigin]\\nScale: [scale]",
                ("[title]", Title),
                ("[file]", Path.GetFileName(FilePath)),
                ("[rect]", $"{SourceRect.X}, {SourceRect.Y}, {SourceRect.Width}, {SourceRect.Height}"),
                ("[inheritedrect]", InheritSourceRect ? Text("suffix.inherited", " (inherited)").Value : string.Empty),
                ("[origin]", $"{RelativeOrigin.X:0.####}, {RelativeOrigin.Y:0.####}"),
                ("[inheritedorigin]", InheritOrigin ? Text("suffix.inherited", " (inherited)").Value : string.Empty),
                ("[scale]", $"{Scale:0.###}")).Value;
    }

    private void CreateBodySpriteWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow(WindowTitleBodySprite, new Point(470, 380), new Point(300, 275), out bodySpriteWindow);
        bodySpriteInfoList = CreateSpritePreviewPanel(content, Text("spritepanel.bodysprites", "Body Sprites"), bodySpritePreviewZoom, value => bodySpritePreviewZoom = value);
    }

    private void CreateHeadSpriteWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow(WindowTitleHeadSprite, new Point(470, 380), new Point(300, 305), out headSpriteWindow);
        headSpriteInfoList = CreateSpritePreviewPanel(content, Text("spritepanel.headsprites", "Head Sprites"), headSpritePreviewZoom, value => headSpritePreviewZoom = value);
    }

    private void CreateClothingSpriteWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow(WindowTitleClothingSprite, new Point(470, 400), new Point(300, 335), out clothingSpriteWindow);
        clothingSpriteInfoList = CreateSpritePreviewPanel(content, Text("spritepanel.clothingsprites", "Clothing Sprites"), clothingSpritePreviewZoom, value => clothingSpritePreviewZoom = value);
    }

    private GUIListBox CreateSpritePreviewPanel(GUILayoutGroup content, LocalizedString label, float zoom, Action<float> setZoom)
    {
        var panel = new GUIFrame(new RectTransform(Vector2.One, content.RectTransform), style: null);
        panel.RectTransform.MinSize = new Point(0, GUI.IntScale(334));
        var panelLayout = new GUILayoutGroup(new RectTransform(Vector2.One, panel.RectTransform), childAnchor: Anchor.TopLeft)
        {
            Stretch = true,
            AbsoluteSpacing = GUI.IntScale(2)
        };

        var zoomRow = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), panelLayout.RectTransform, Anchor.TopLeft, Pivot.TopLeft)
        {
            MinSize = new Point(0, GUI.IntScale(28)),
            MaxSize = new Point(int.MaxValue, GUI.IntScale(28))
        }, style: null);
        var zoomLabel = new GUITextBlock(
            new RectTransform(new Vector2(0.22f, 1.0f), zoomRow.RectTransform, Anchor.CenterLeft),
            FormatZoomText(zoom),
            font: GUIStyle.SmallFont,
            textAlignment: Alignment.CenterLeft)
        {
            CanBeFocused = false
        };
        var slider = new GUIScrollBar(
            new RectTransform(new Vector2(0.36f, 0.55f), zoomRow.RectTransform, Anchor.CenterLeft)
            {
                RelativeOffset = new Vector2(0.23f, 0.0f),
                MinSize = new Point(GUI.IntScale(120), GUI.IntScale(14))
            },
            barSize: 0.12f,
            isHorizontal: true)
        {
            Range = new Vector2(0.25f, 2.0f),
            StepValue = 0.05f,
            BarScrollValue = zoom
        };
        slider.OnMoved = (scrollBar, _) =>
        {
            float newZoom = scrollBar.BarScrollValue;
            setZoom(newZoom);
            zoomLabel.Text = FormatZoomText(newZoom);
            UpdateAllViewerSpriteInfo();
            return true;
        };
        new GUITextBlock(
            new RectTransform(new Vector2(0.38f, 1.0f), zoomRow.RectTransform, Anchor.CenterRight),
            label,
            font: GUIStyle.SmallFont,
            textAlignment: Alignment.CenterRight)
        {
            CanBeFocused = false
        };

        GUIListBox list = null;
        list = new GUIListBox(
            new RectTransform(new Vector2(1.0f, 1.0f), panelLayout.RectTransform, Anchor.TopLeft, Pivot.TopLeft)
            {
                MinSize = new Point(0, GUI.IntScale(180))
            },
            style: null);
        list.Padding = Vector4.Zero;
        list.AutoHideScrollBar = false;
        list.OnAddedToGUIUpdateList = component =>
        {
            if (component is GUIListBox listBox && spriteListsPendingScrollReset.Remove(listBox))
            {
                ResetSpriteListScroll(listBox);
            }
            UpdateSpriteHorizontalScrollBar(list);
            UpdateSpriteListVerticalScrollBar(list);
        };
        var horizontalScrollBar = new GUIScrollBar(
            new RectTransform(new Vector2(1.0f, 0.0f), panel.RectTransform, Anchor.BottomLeft, Pivot.BottomLeft)
            {
                MinSize = new Point(0, GUI.IntScale(18)),
                MaxSize = new Point(int.MaxValue, GUI.IntScale(18))
            },
            barSize: 1.0f,
            isHorizontal: true)
        {
            Range = new Vector2(0.0f, 1.0f),
            Visible = false,
            Enabled = false,
            IgnoreLayoutGroups = true
        };
        horizontalScrollBar.OnMoved = (scrollBar, _) =>
        {
            if (list != null)
            {
                spriteHorizontalScrollOffsets[list] = scrollBar.BarScrollValue;
            }
            return true;
        };
        spriteHorizontalScrollBars[list] = horizontalScrollBar;
        spriteHorizontalScrollOffsets[list] = 0.0f;
        return list;
    }

    private static LocalizedString FormatZoomText(float zoom)
    {
        return TextWithVariables("label.zoom", "Zoom: [percent]%", ("[percent]", ((int)(zoom * 100)).ToString()));
    }

    private void UpdateAllViewerSpriteInfo()
    {
        UpdateBodySpriteInfo();
        UpdateHeadSpriteInfo();
        UpdateClothingSpriteInfo();
    }

    private void UpdateBodySpriteInfo()
    {
        if (bodySpriteInfoList == null) { return; }

        var entries = new List<ViewerSpriteEntry>();
        IEnumerable<Limb> limbs = CurrentCharacter?.AnimController?.Limbs ?? Array.Empty<Limb>();
        foreach (Limb limb in limbs)
        {
            if (limb?.ActiveSprite == null || limb.type == LimbType.Head) { continue; }
            entries.Add(CreateSpriteEntry(limb.type.ToString(), string.Empty, limb.ActiveSprite));
        }
        PopulateSpritePreviewList(bodySpriteInfoList, entries, bodySpritePreviewZoom, Text("message.nobodysprites", "No body sprites available.").Value);
    }

    private void UpdateHeadSpriteInfo()
    {
        if (headSpriteInfoList == null) { return; }

        var entries = new List<ViewerSpriteEntry>();
        Character character = CurrentCharacter;
        if (character?.Info?.HeadSprite != null)
        {
            entries.Add(CreateSpriteEntry(Text("spriteentry.head", "Head").Value, TextWithVariables("spriteentry.tags", "tags [tags]", ("[tags]", string.Join(", ", character.Info.Head.Preset.TagSet.Select(static tag => tag.Value)))).Value, character.Info.HeadSprite));
        }

        Limb head = character?.AnimController?.GetLimb(LimbType.Head);
        if (head != null)
        {
            foreach (WearableSprite wearableSprite in head.OtherWearables.Where(static w => w?.Sprite != null))
            {
                entries.Add(CreateSpriteEntry($"{wearableSprite.Type} / {Text("spriteentry.head", "Head").Value}", wearableSprite.Limb.ToString(), wearableSprite, head, character));
            }
        }

        PopulateSpritePreviewList(headSpriteInfoList, entries, headSpritePreviewZoom, Text("message.noheadsprites", "No head sprites available.").Value);
    }

    private void UpdateClothingSpriteInfo()
    {
        if (clothingSpriteInfoList == null) { return; }
        PopulateSpritePreviewList(
            clothingSpriteInfoList,
            GetSelectedClothingSpriteEntries(),
            clothingSpritePreviewZoom,
            selectedClothingPrefab == null ? Text("message.noclothingselected", NoClothingSelectedText).Value : Text("message.novisiblesprites", "Selected clothing has no visible sprites.").Value);
    }

    private List<ViewerSpriteEntry> GetSelectedClothingSpriteEntries()
    {
        return GetSelectedWearableSprites()
            .Where(static tuple => tuple.sprite?.Sprite != null)
            .Select(tuple => CreateSpriteEntry($"{tuple.sprite.WearableComponent.Item.Prefab.Name} {tuple.sprite.Limb}", tuple.sprite.Type.ToString(), tuple.sprite, tuple.limb, CurrentCharacter))
            .ToList();
    }

    private List<(Limb limb, WearableSprite sprite)> GetSelectedWearableSprites()
    {
        return CurrentCharacter?.AnimController?.Limbs
            .Where(static limb => limb != null)
            .SelectMany(static limb => limb.WearingItems.Select(sprite => (limb, sprite)))
            .Where(tuple => tuple.sprite?.WearableComponent?.Item?.Prefab == selectedClothingPrefab)
            .Distinct()
            .ToList() ?? new List<(Limb limb, WearableSprite sprite)>();
    }

    private ViewerSpriteEntry CreateSpriteEntry(string title, string subtitle, WearableSprite wearableSprite, Limb limb, Character character)
    {
        ViewerSpriteEntry entry = CreateSpriteEntry(title, subtitle, wearableSprite.Sprite, wearableSprite.Scale);
        entry.InheritSourceRect = wearableSprite.InheritSourceRect;
        entry.InheritOrigin = wearableSprite.InheritOrigin;
        entry.SourceElement = wearableSprite.SourceElement;
        ResolveInheritedSpriteValues(entry, wearableSprite, limb, character);
        return entry;
    }

    private static ViewerSpriteEntry CreateSpriteEntry(string title, string subtitle, Sprite sprite, float scale = 1.0f)
    {
        return new ViewerSpriteEntry
        {
            Title = title,
            Subtitle = subtitle,
            Sprite = sprite,
            SourceRect = sprite.SourceRect,
            RelativeOrigin = sprite.RelativeOrigin,
            Scale = scale,
            FilePath = sprite.FilePath.Value
        };
    }

    private void PopulateSpritePreviewList(GUIListBox list, IReadOnlyList<ViewerSpriteEntry> entries, float zoom, string emptyText)
    {
        list.ClearChildren();
        int bottomPadding = GetSpritePreviewBottomPadding();
        if (entries.None())
        {
            new GUITextBlock(new RectTransform(new Point(0, GUI.IntScale(24)), list.Content.RectTransform, Anchor.TopLeft, Pivot.TopLeft), emptyText, font: GUIStyle.SmallFont)
            {
                CanBeFocused = false
            };
            spriteCanvasWidths[list] = 0;
            spriteCanvasHeights[list] = GUI.IntScale(24) + bottomPadding;
            spriteHorizontalScrollOffsets[list] = 0.0f;
            UpdateSpriteHorizontalScrollBar(list);
            RequestSpriteListScrollReset(list);
            UpdateSpriteListVerticalScrollBar(list);
            return;
        }

        int padding = GUI.IntScale(4);
        int fileLabelHeight = GUI.IntScale(16);
        int y = 0;
        int width = list.Rect.Width - GUI.IntScale(28);
        var groups = entries.GroupBy(static entry => entry.FilePath).ToList();
        foreach (var group in groups)
        {
            int textureWidth = group.First().Sprite.Texture?.Width ?? group.Max(static entry => entry.SourceRect.Right);
            int textureHeight = group.First().Sprite.Texture?.Height ?? group.Max(static entry => entry.SourceRect.Bottom);
            width = Math.Max(width, padding * 2 + (int)(textureWidth * zoom));
            y += fileLabelHeight + (int)(textureHeight * zoom) + padding;
        }

        int canvasHeight = Math.Max(y + bottomPadding, GUI.IntScale(24));
        bool allowPreviewSelection = list == clothingSpriteInfoList;
        new GUICustomComponent(
            new RectTransform(new Point(width, canvasHeight), list.Content.RectTransform, Anchor.TopLeft, Pivot.TopLeft, isFixedSize: true),
            onDraw: (spriteBatch, component) => DrawSpritePreviewCanvas(spriteBatch, component, entries, zoom, GetSpriteHorizontalScrollOffset(list)),
            onUpdate: (_, component) => UpdateSpritePreviewSelection(component, entries, zoom, GetSpriteHorizontalScrollOffset(list), allowPreviewSelection))
        {
            CanBeFocused = true,
            HideElementsOutsideFrame = false
        };
        spriteCanvasWidths[list] = width;
        spriteCanvasHeights[list] = canvasHeight;
        UpdateSpriteHorizontalScrollBar(list);
        RequestSpriteListScrollReset(list);
        UpdateSpriteListVerticalScrollBar(list);
    }

    private static int GetSpritePreviewBottomPadding()
    {
        return GUI.IntScale(30);
    }

    private void RequestSpriteListScrollReset(GUIListBox list)
    {
        ResetSpriteListScroll(list);
        spriteListsPendingScrollReset.Add(list);
    }

    private static void ResetSpriteListScroll(GUIListBox list)
    {
        list.UpdateScrollBarSize();
        list.BarScroll = 0.0f;
        list.ScrollBar.BarScroll = 0.0f;
        list.RecalculateChildren();
    }

    private void UpdateSpriteListVerticalScrollBar(GUIListBox list)
    {
        if (list == null) { return; }
        list.RecalculateChildren();
        list.UpdateScrollBarSize();
        int canvasHeight = spriteCanvasHeights.TryGetValue(list, out int storedHeight) ? storedHeight : 0;
        list.ScrollBarVisible = canvasHeight > list.Content.Rect.Height + GUI.IntScale(2);
        list.ScrollBar.Enabled = list.ScrollBarVisible;
    }

    private float GetSpriteHorizontalScrollOffset(GUIListBox list)
    {
        return spriteHorizontalScrollOffsets.TryGetValue(list, out float offset) ? offset : 0.0f;
    }

    private void UpdateSpriteHorizontalScrollBar(GUIListBox list)
    {
        if (list == null || !spriteHorizontalScrollBars.TryGetValue(list, out GUIScrollBar scrollBar)) { return; }

        int viewportWidth = Math.Max(1, list.Content.Rect.Width);
        int canvasWidth = spriteCanvasWidths.TryGetValue(list, out int storedWidth) ? storedWidth : 0;
        int maxOffset = Math.Max(0, canvasWidth - viewportWidth);
        bool needsHorizontalScroll = maxOffset > GUI.IntScale(2);

        scrollBar.Visible = needsHorizontalScroll;
        scrollBar.Enabled = needsHorizontalScroll;
        scrollBar.Range = new Vector2(0.0f, Math.Max(1.0f, maxOffset));
        scrollBar.BarSize = needsHorizontalScroll ? MathHelper.Clamp(viewportWidth / (float)Math.Max(viewportWidth, canvasWidth), 0.05f, 1.0f) : 1.0f;

        float offset = spriteHorizontalScrollOffsets.TryGetValue(list, out float storedOffset) ? storedOffset : 0.0f;
        offset = MathHelper.Clamp(offset, 0.0f, maxOffset);
        spriteHorizontalScrollOffsets[list] = offset;
        scrollBar.BarScrollValue = needsHorizontalScroll ? offset : 0.0f;
    }

    private void UpdateSpritePreviewSelection(GUICustomComponent component, IReadOnlyList<ViewerSpriteEntry> entries, float zoom, float horizontalOffset, bool allowPreviewSelection)
    {
        if (!allowPreviewSelection || !PlayerInput.PrimaryMouseButtonClicked()) { return; }
        ViewerSpriteEntry hoveredEntry = GetHoveredSpritePreviewEntry(component, entries, zoom, horizontalOffset);
        if (hoveredEntry?.SourceElement == null) { return; }
        SelectWearableSpriteFromPreview(hoveredEntry.SourceElement);
    }

    private ViewerSpriteEntry GetHoveredSpritePreviewEntry(GUICustomComponent component, IReadOnlyList<ViewerSpriteEntry> entries, float zoom, float horizontalOffset)
    {
        int padding = GUI.IntScale(8);
        int fileLabelHeight = GUI.IntScale(16);
        int y = component.Rect.Y;
        int x = component.Rect.X - (int)horizontalOffset;
        Point mousePos = PlayerInput.MousePosition.ToPoint();

        foreach (var group in entries.GroupBy(static entry => entry.FilePath))
        {
            ViewerSpriteEntry first = group.First();
            Texture2D texture = first.Sprite.Texture;
            if (texture == null) { continue; }

            y += fileLabelHeight;
            Rectangle sheetRect = new Rectangle(x + padding, y, (int)(texture.Width * zoom), (int)(texture.Height * zoom));
            foreach (ViewerSpriteEntry entry in group)
            {
                Rectangle source = entry.SourceRect;
                Rectangle dest = new Rectangle(
                    sheetRect.X + (int)(source.X * zoom),
                    sheetRect.Y + (int)(source.Y * zoom),
                    Math.Max(1, (int)(source.Width * zoom)),
                    Math.Max(1, (int)(source.Height * zoom)));
                if (dest.Contains(mousePos))
                {
                    return entry;
                }
            }

            y += sheetRect.Height + padding;
        }

        return null;
    }

    private void DrawSpritePreviewCanvas(SpriteBatch spriteBatch, GUICustomComponent component, IReadOnlyList<ViewerSpriteEntry> entries, float zoom, float horizontalOffset)
    {
        int padding = GUI.IntScale(8);
        int fileLabelHeight = GUI.IntScale(16);
        int y = component.Rect.Y;
        int x = component.Rect.X - (int)horizontalOffset;
        Point mousePos = PlayerInput.MousePosition.ToPoint();
        string tooltip = null;

        foreach (var group in entries.GroupBy(static entry => entry.FilePath))
        {
            ViewerSpriteEntry first = group.First();
            Texture2D texture = first.Sprite.Texture;
            if (texture == null) { continue; }

            string fileName = Path.GetFileName(first.FilePath);
            GUI.DrawString(spriteBatch, new Vector2(x + padding, y), fileName, GUIStyle.TextColorNormal, font: GUIStyle.SmallFont);
            y += fileLabelHeight;

            Rectangle sheetRect = new Rectangle(x + padding, y, (int)(texture.Width * zoom), (int)(texture.Height * zoom));
            spriteBatch.Draw(texture, sheetRect, Color.White);
            GUI.DrawRectangle(spriteBatch, sheetRect, GUIStyle.TextColorDim, isFilled: false);

            foreach (ViewerSpriteEntry entry in group)
            {
                Rectangle source = entry.SourceRect;
                Rectangle dest = new Rectangle(
                    sheetRect.X + (int)(source.X * zoom),
                    sheetRect.Y + (int)(source.Y * zoom),
                    Math.Max(1, (int)(source.Width * zoom)),
                    Math.Max(1, (int)(source.Height * zoom)));
                bool isHovered = dest.Contains(mousePos);
                bool isSelected = IsSelectedWearableSpriteElement(entry.SourceElement);
                Color outline = isSelected ? Color.Cyan : isHovered ? Color.Yellow : Color.Red;
                GUI.DrawRectangle(spriteBatch, dest, outline, isFilled: false, thickness: isSelected || isHovered ? 2 : 1);
                if (dest.Contains(mousePos))
                {
                    tooltip = entry.Tooltip;
                }
            }

            y += sheetRect.Height + padding;
        }

        component.ToolTip = string.Empty;
        if (!string.IsNullOrEmpty(tooltip))
        {
            spritePreviewTooltip = tooltip;
        }
    }

    private static void ResolveInheritedSpriteValues(ViewerSpriteEntry entry, WearableSprite wearableSprite, Limb limb, Character character)
    {
        Sprite activeSprite = limb?.ActiveSprite;
        if (activeSprite == null) { return; }

        if (wearableSprite.InheritSourceRect)
        {
            if (wearableSprite.SheetIndex.HasValue)
            {
                entry.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(activeSprite, wearableSprite.SheetIndex.Value), activeSprite.SourceRect.Size);
            }
            else if (limb.type == LimbType.Head && character?.Info?.Head != null)
            {
                entry.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(activeSprite, character.Info.Head.SheetIndex.ToPoint()), activeSprite.SourceRect.Size);
            }
            else
            {
                entry.SourceRect = activeSprite.SourceRect;
            }
        }

        if (wearableSprite.InheritOrigin)
        {
            entry.RelativeOrigin = activeSprite.RelativeOrigin;
        }
    }
}
