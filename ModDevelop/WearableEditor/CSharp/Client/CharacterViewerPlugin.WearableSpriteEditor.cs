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

    private void PopulateWearableEditorParams()
    {
        wearableEditorRebuildQueued = false;
        wearableEditorPrefab = selectedClothingPrefab;

        ParamsEditor editor = ParamsEditor.Instance;
        editor.Clear();

        GUIListBox list = editor.EditorBox;
        list.AutoHideScrollBar = false;
        if (selectedClothingPrefab == null)
        {
            CreateEditorMessage(list, Text("message.noclothingselected", NoClothingSelectedText));
            RefreshListScrollBar(list);
            return;
        }

        WearableSpriteSelection selection = GetSelectedWearableSpriteSelection();
        if (selection?.Sprite == null)
        {
            CreateEditorMessage(list, Text("message.noequippedspriteentries", "Selected clothing has no equipped sprite entries."));
            RefreshListScrollBar(list);
            return;
        }

        CreateSelectedWearableSpriteEditor(list, selection.Sprite);
        RefreshListScrollBar(list);
    }

    private static void CreateEditorMessage(GUIListBox list, LocalizedString text)
    {
        new GUITextBlock(new RectTransform(new Point(Math.Max(GUI.IntScale(320), list.Rect.Width - GUI.IntScale(30)), GUI.IntScale(34)), list.Content.RectTransform),
            text,
            wrap: true,
            font: GUIStyle.SmallFont)
        {
            CanBeFocused = false
        };
    }

    private static void RefreshListScrollBar(GUIListBox list)
    {
        if (list == null) { return; }
        list.RecalculateChildren();
        list.UpdateScrollBarSize();
        list.ScrollBarVisible = list.BarSize < 1.0f;
        list.ScrollBar.Enabled = list.ScrollBarVisible;
    }

    private void CreateSelectedWearableSpriteEditor(GUIListBox list, WearableSprite wearableSprite)
    {
        ContentXElement spriteElement = wearableSprite.SourceElement;
        XElement element = spriteElement.Element;
        if (!originalWearableSpriteElements.ContainsKey(element))
        {
            originalWearableSpriteElements[element] = new XElement(element);
        }

        int entryWidth = Math.Max(GUI.IntScale(340), list.Rect.Width - GUI.IntScale(34));
        var layout = new GUILayoutGroup(new RectTransform(new Point(entryWidth, GUI.IntScale(1280)), list.Content.RectTransform), childAnchor: Anchor.TopLeft)
        {
            Stretch = false,
            AbsoluteSpacing = GUI.IntScale(4)
        };

        GUITextBlock xmlPreview = null;
        Action refreshXml = () => RefreshXmlPreview(xmlPreview, spriteElement);
        Action directRefresh = () =>
        {
            refreshXml();
            QueueWearableSpriteListRebuild();
            UpdateAllViewerSpriteInfo();
        };
        Action reEquipRefresh = () =>
        {
            refreshXml();
            ReequipSelectedWearable();
        };

        string title = spriteElement.GetAttributeString("name", null) ?? $"{selectedClothingPrefab.Name} {wearableSprite.Limb}";
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), layout.RectTransform) { MinSize = new Point(0, GUI.IntScale(28)) },
            title,
            font: GUIStyle.SubHeadingFont,
            wrap: true)
        {
            CanBeFocused = false
        };

        CreateStringInputLine(layout, Text("field.name", "name"), spriteElement, "name", "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "name", value);
            if (wearableSprite.Sprite != null) { wearableSprite.Sprite.Name = value; }
            directRefresh();
        });
        CreateStringInputLine(layout, Text("field.texture", "texture"), spriteElement, "texture", wearableSprite.SpritePath ?? "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "texture", value);
            reEquipRefresh();
        });
        CreateVector2IntLine(layout, Text("field.sourcerect", "sourcerect"), GetEffectiveSourceRect(wearableSprite), (x, y, w, h) =>
        {
            Rectangle rect = new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
            spriteElement.SetAttributeValue("sourcerect", $"{rect.X},{rect.Y},{rect.Width},{rect.Height}");
            if (wearableSprite.Sprite != null)
            {
                wearableSprite.Sprite.SourceRect = rect;
                wearableSprite.Sprite.RelativeOrigin = wearableSprite.Sprite.RelativeOrigin;
            }
            directRefresh();
        });
        CreatePointLine(layout, Text("field.sheetindex", "sheetindex"), spriteElement.GetAttributePoint("sheetindex", new Point(-1, -1)), value =>
        {
            SetPointAttribute(spriteElement, "sheetindex", value);
            reEquipRefresh();
        });
        CreatePointLine(layout, Text("field.sheetelementsize", "sheetelementsize"), spriteElement.GetAttributePoint("sheetelementsize", Point.Zero), value =>
        {
            SetPointAttribute(spriteElement, "sheetelementsize", value);
            reEquipRefresh();
        });
        CreateVector2FloatLine(layout, Text("field.origin", "origin"), GetEffectiveOrigin(wearableSprite), 0.001f, 4, value =>
        {
            value = Vector2.Clamp(value, Vector2.Zero, Vector2.One);
            spriteElement.SetAttributeValue("origin", $"{FormatFloat(value.X)},{FormatFloat(value.Y)}");
            if (wearableSprite.Sprite != null) { wearableSprite.Sprite.RelativeOrigin = value; }
            directRefresh();
        });
        CreateVector2FloatLine(layout, Text("field.size", "size"), spriteElement.GetAttributeVector2("size", Vector2.One), 0.01f, 3, value =>
        {
            spriteElement.SetAttributeValue("size", $"{FormatFloat(value.X)},{FormatFloat(value.Y)}");
            ApplySpriteSize(wearableSprite, value);
            directRefresh();
        });
        CreateFloatLine(layout, Text("field.depth", "depth"), spriteElement.GetAttributeFloat("depth", wearableSprite.Sprite?.Depth ?? 0.001f), 0.001f, 4, value =>
        {
            value = MathHelper.Clamp(value, 0.001f, 0.999f);
            spriteElement.SetAttributeValue("depth", FormatFloat(value));
            if (wearableSprite.Sprite != null) { wearableSprite.Sprite.Depth = value; }
            directRefresh();
        });
        CreateBoolLine(layout, Text("field.compress", "compress"), spriteElement, "compress", true, reEquipRefresh);
        CreateLimbDropdownLine(layout, Text("field.limb", "limb"), spriteElement.GetAttributeString("limb", wearableSprite.Limb.ToString()), false, value =>
        {
            SetOptionalAttributeValue(spriteElement, "limb", value, "default");
            reEquipRefresh();
        });
        CreateBoolLine(layout, Text("field.hidelimb", "hidelimb"), spriteElement, "hidelimb", false, reEquipRefresh);
        CreateBoolLine(layout, Text("field.hideotherwearables", "hideotherwearables"), spriteElement, "hideotherwearables", false, reEquipRefresh);
        CreateBoolLine(layout, Text("field.alphaclipotherwearables", "alphaclipotherwearables"), spriteElement, "alphaclipotherwearables", false, reEquipRefresh);
        CreateBoolLine(layout, Text("field.canbehiddenbyotherwearables", "canbehiddenbyotherwearables"), spriteElement, "canbehiddenbyotherwearables", true, reEquipRefresh);
        CreateStringInputLine(layout, Text("field.canbehiddenbyitem", "canbehiddenbyitem"), spriteElement, "canbehiddenbyitem", "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "canbehiddenbyitem", value);
            reEquipRefresh();
        });
        CreateStringInputLine(layout, Text("field.hidewearablesoftype", "hidewearablesoftype"), spriteElement, "hidewearablesoftype", "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "hidewearablesoftype", value);
            reEquipRefresh();
        });
        CreateBoolLine(layout, Text("field.inheritlimbdepth", "inheritlimbdepth"), spriteElement, "inheritlimbdepth", true, reEquipRefresh);
        CreateLimbDropdownLine(layout, Text("field.depthlimb", "depthlimb"), ValueOrDefault(spriteElement, "depthlimb", "default"), true, value =>
        {
            SetOptionalAttributeValue(spriteElement, "depthlimb", value, "default");
            reEquipRefresh();
        });
        string scaleAttribute = GetInheritTextureScaleAttribute(spriteElement);
        CreateBoolLine(layout, Text("field.inheritscale", "inheritscale"), spriteElement, scaleAttribute, false, reEquipRefresh);
        CreateBoolLine(layout, Text("field.ignorelimbscale", "ignorelimbscale"), spriteElement, "ignorelimbscale", false, reEquipRefresh);
        CreateBoolLine(layout, Text("field.ignoretexturescale", "ignoretexturescale"), spriteElement, "ignoretexturescale", false, reEquipRefresh);
        CreateBoolLine(layout, Text("field.ignoreragdollscale", "ignoreragdollscale"), spriteElement, "ignoreragdollscale", false, reEquipRefresh);
        CreateBoolLine(layout, Text("field.inheritorigin", "inheritorigin"), spriteElement, "inheritorigin", false, reEquipRefresh);
        CreateBoolLine(layout, Text("field.inheritsourcerect", "inheritsourcerect"), spriteElement, "inheritsourcerect", false, reEquipRefresh);
        CreateFloatLine(layout, Text("field.scale", "scale"), spriteElement.GetAttributeFloat("scale", 1.0f), 0.01f, 3, value =>
        {
            spriteElement.SetAttributeValue("scale", FormatFloat(value));
            SetWearableSpriteProperty(wearableSprite, "Scale", value);
            directRefresh();
        });
        CreateFloatLine(layout, Text("field.rotation", "rotation"), spriteElement.GetAttributeFloat("rotation", 0.0f), 0.1f, 2, value =>
        {
            spriteElement.SetAttributeValue("rotation", FormatFloat(value));
            SetWearableSpriteProperty(wearableSprite, "Rotation", MathHelper.ToRadians(value));
            directRefresh();
        });
        CreateStringInputLine(layout, Text("field.sound", "sound"), spriteElement, "sound", "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "sound", value);
            reEquipRefresh();
        });

        CreateNestedPlaceholder(layout, spriteElement);

        xmlPreview = CreateXmlPreviewBlock(layout, spriteElement);
        RefreshXmlPreview(xmlPreview, spriteElement);
        CreateWearableSpriteActionButtons(layout, spriteElement, wearableSprite);
    }

    private static GUITextBlock CreateXmlPreviewBlock(GUILayoutGroup layout, ContentXElement spriteElement)
    {
        return new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), layout.RectTransform)
        {
            MinSize = GetXmlPreviewSize(spriteElement)
        }, "", wrap: true, font: GUIStyle.SmallFont)
        {
            CanBeFocused = false
        };
    }

    private void CreateWearableSpriteActionButtons(GUILayoutGroup layout, ContentXElement spriteElement, WearableSprite wearableSprite)
    {
        var buttonRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), layout.RectTransform) { MinSize = new Point(0, GUI.IntScale(32)) }, isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        CreateEditorButton(buttonRow, Text("button.save", "Save"), () => SaveWearableXml(spriteElement));
        CreateEditorButton(buttonRow, Text("button.revert", "Revert"), () =>
        {
            RevertWearableSprite(wearableSprite);
            QueueWearableEditorRebuild();
            QueueWearableSpriteListRebuild();
        });
    }

    private static GUILayoutGroup CreateEditorRow(GUILayoutGroup parent, LocalizedString label)
    {
        var row = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform) { MinSize = new Point(0, GUI.IntScale(28)) }, isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.01f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.34f, 1.0f), row.RectTransform), label, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft)
        {
            CanBeFocused = false
        };
        return row;
    }

    private static void CreateStringInputLine(GUILayoutGroup parent, LocalizedString label, ContentXElement element, string attribute, string defaultValue, Action<string> onChanged)
    {
        var row = CreateEditorRow(parent, label);
        string value = element.GetAttributeString(attribute, defaultValue) ?? "";
        var input = new GUITextBox(new RectTransform(new Vector2(0.64f, 1.0f), row.RectTransform), value, style: "GUITextBox")
        {
            Font = GUIStyle.SmallFont,
            OnTextChangedDelegate = (_, text) =>
            {
                onChanged(text ?? "");
                return true;
            }
        };
    }

    private static void CreateBoolLine(GUILayoutGroup parent, LocalizedString label, ContentXElement element, string attribute, bool defaultValue, Action onChanged)
    {
        var row = CreateEditorRow(parent, label);
        var tickBox = new GUITickBox(new RectTransform(new Vector2(0.26f, 1.0f), row.RectTransform), "", font: GUIStyle.SmallFont)
        {
            Selected = element.GetAttributeBool(attribute, defaultValue),
            OnSelected = box =>
            {
                element.Element.SetAttributeValue(attribute, box.Selected.ToString().ToLowerInvariant());
                onChanged();
                return true;
            }
        };
        new GUIButton(new RectTransform(new Vector2(0.36f, 1.0f), row.RectTransform), Text("button.default", "Default"), style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                element.Element.SetAttributeValue(attribute, null);
                onChanged();
                return true;
            }
        };
    }

    private static void CreateLimbDropdownLine(GUILayoutGroup parent, LocalizedString label, string value, bool allowDefault, Action<string> onChanged)
    {
        var row = CreateEditorRow(parent, label);
        CreateLimbDropdown(row, label, value, allowDefault, onChanged);
    }

    private static void CreateLimbDropdown(GUILayoutGroup row, LocalizedString label, string value, bool allowDefault, Action<string> onChanged)
    {
        var dropDown = new GUIDropDown(new RectTransform(new Vector2(0.64f, 1.0f), row.RectTransform), string.IsNullOrWhiteSpace(value) ? "default" : value);
        if (allowDefault)
        {
            dropDown.AddItem("default", "default");
        }
        foreach (LimbType limbType in Enum.GetValues<LimbType>())
        {
            string limbName = limbType.ToString();
            dropDown.AddItem(limbName, limbName);
        }
        dropDown.SelectItem(string.IsNullOrWhiteSpace(value) ? "default" : value);
        dropDown.OnSelected = (_, data) =>
        {
            if (data is string selectedValue)
            {
                onChanged(selectedValue);
            }
            return true;
        };
    }

    private static void CreatePointLine(GUILayoutGroup parent, LocalizedString label, Point value, Action<Point> onChanged)
    {
        int x = value.X;
        int y = value.Y;
        var row = CreateEditorRow(parent, label);
        CreateIntInput(row, "x", x, newValue => { x = newValue; onChanged(new Point(x, y)); });
        CreateIntInput(row, "y", y, newValue => { y = newValue; onChanged(new Point(x, y)); });
    }

    private static void CreateVector2IntLine(GUILayoutGroup parent, LocalizedString label, Rectangle value, Action<int, int, int, int> onChanged)
    {
        int x = value.X;
        int y = value.Y;
        int w = value.Width;
        int h = value.Height;
        var row = CreateEditorRow(parent, label);
        CreateIntInput(row, "x", x, newValue => { x = newValue; onChanged(x, y, w, h); });
        CreateIntInput(row, "y", y, newValue => { y = newValue; onChanged(x, y, w, h); });
        CreateIntInput(row, "w", w, newValue => { w = Math.Max(1, newValue); onChanged(x, y, w, h); });
        CreateIntInput(row, "h", h, newValue => { h = Math.Max(1, newValue); onChanged(x, y, w, h); });
    }

    private static void CreateVector2FloatLine(GUILayoutGroup parent, LocalizedString label, Vector2 value, float step, int decimals, Action<Vector2> onChanged)
    {
        float x = value.X;
        float y = value.Y;
        var row = CreateEditorRow(parent, label);
        CreateFloatInput(row, "x", x, step, decimals, newValue => { x = newValue; onChanged(new Vector2(x, y)); });
        CreateFloatInput(row, "y", y, step, decimals, newValue => { y = newValue; onChanged(new Vector2(x, y)); });
    }

    private static void CreateFloatLine(GUILayoutGroup parent, LocalizedString label, float value, float step, int decimals, Action<float> onChanged)
    {
        var row = CreateEditorRow(parent, label);
        var input = new GUINumberInput(new RectTransform(new Vector2(0.64f, 1.0f), row.RectTransform), NumberType.Float, relativeButtonAreaWidth: 0.14f)
        {
            FloatValue = value,
            ValueStep = step,
            DecimalsToDisplay = decimals,
            Font = GUIStyle.SmallFont
        };
        input.OnValueChanged += numberInput => onChanged(numberInput.FloatValue);
    }

    private static void CreateIntInput(GUILayoutGroup row, string label, int value, Action<int> onChanged)
    {
        var holder = new GUILayoutGroup(new RectTransform(new Vector2(0.16f, 1.0f), row.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.25f, 1.0f), holder.RectTransform), label, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft);
        var input = new GUINumberInput(new RectTransform(new Vector2(0.72f, 1.0f), holder.RectTransform), NumberType.Int, relativeButtonAreaWidth: 0.2f)
        {
            IntValue = value,
            Font = GUIStyle.SmallFont
        };
        input.OnValueChanged += numberInput => onChanged(numberInput.IntValue);
    }

    private static void CreateFloatInput(GUILayoutGroup row, string label, float value, float step, int decimals, Action<float> onChanged)
    {
        var holder = new GUILayoutGroup(new RectTransform(new Vector2(0.32f, 1.0f), row.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.18f, 1.0f), holder.RectTransform), label, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft);
        var input = new GUINumberInput(new RectTransform(new Vector2(0.78f, 1.0f), holder.RectTransform), NumberType.Float, relativeButtonAreaWidth: 0.2f)
        {
            FloatValue = value,
            ValueStep = step,
            DecimalsToDisplay = decimals,
            Font = GUIStyle.SmallFont
        };
        input.OnValueChanged += numberInput => onChanged(numberInput.FloatValue);
    }

    private static GUIButton CreateEditorButton(GUILayoutGroup row, LocalizedString text, Action onClicked)
    {
        return new GUIButton(new RectTransform(Vector2.One, row.RectTransform), text, style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                onClicked();
                return true;
            }
        };
    }

    private static void SetEditorButtonEnabled(GUIButton button, bool enabled)
    {
        if (button == null) { return; }
        button.Enabled = enabled;
        button.TextColor = enabled ? GUIStyle.TextColorNormal : GUIStyle.TextColorDim;
    }

    private static void CreateNestedPlaceholder(GUILayoutGroup parent, ContentXElement spriteElement)
    {
        int lightCount = spriteElement.Elements().Count(e => e.Name.ToString().Equals("LightComponent", StringComparison.OrdinalIgnoreCase));
        int overrideCount = spriteElement.Elements().Count(e => e.Name.ToString().Equals("override", StringComparison.OrdinalIgnoreCase));
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform) { MinSize = new Point(0, GUI.IntScale(44)) },
            TextWithVariables(
                "message.nestednodes",
                "Nested nodes: LightComponent [[lightcount]]  override [[overridecount]]\\nNested node editing: planned",
                ("[lightcount]", lightCount.ToString()),
                ("[overridecount]", overrideCount.ToString())),
            wrap: true,
            font: GUIStyle.SmallFont)
        {
            CanBeFocused = false,
            TextColor = GUIStyle.TextColorDim
        };
    }

    private void ReequipSelectedWearable()
    {
        if (selectedClothingPrefab == null) { return; }
        XElement selected = selectedWearableSpriteElement;
        EquipViewerClothing(selectedClothingPrefab);
        selectedWearableSpriteElement = selected;
        EnsureSelectedWearableSprite();
        QueueWearableEditorRebuild();
        QueueWearableSpriteListRebuild();
        UpdateAllViewerSpriteInfo();
    }

    private void RevertWearableSprite(WearableSprite wearableSprite)
    {
        if (wearableSprite?.SourceElement?.Element == null) { return; }
        XElement element = wearableSprite.SourceElement.Element;
        if (!originalWearableSpriteElements.TryGetValue(element, out XElement original)) { return; }

        XElement clone = new XElement(original);
        element.ReplaceAttributes(clone.Attributes());
        element.ReplaceNodes(clone.Nodes());
        ApplyWearableSpriteElementToLiveSprite(wearableSprite);
        UpdateAllViewerSpriteInfo();
    }

    private static void ApplyWearableSpriteElementToLiveSprite(WearableSprite wearableSprite)
    {
        if (wearableSprite?.Sprite == null || wearableSprite.SourceElement == null) { return; }

        wearableSprite.Sprite.SourceRect = wearableSprite.SourceElement.GetAttributeRect("sourcerect", wearableSprite.Sprite.SourceRect);
        wearableSprite.Sprite.RelativeOrigin = wearableSprite.SourceElement.GetAttributeVector2("origin", wearableSprite.Sprite.RelativeOrigin);
        wearableSprite.Sprite.Depth = wearableSprite.SourceElement.GetAttributeFloat("depth", wearableSprite.Sprite.Depth);
        ApplySpriteSize(wearableSprite, wearableSprite.SourceElement.GetAttributeVector2("size", Vector2.One));
        SetWearableSpriteProperty(wearableSprite, "Scale", wearableSprite.SourceElement.GetAttributeFloat("scale", 1.0f));
        SetWearableSpriteProperty(wearableSprite, "Rotation", MathHelper.ToRadians(wearableSprite.SourceElement.GetAttributeFloat("rotation", 0.0f)));
    }

    private static void RefreshXmlPreview(GUITextBlock textBlock, ContentXElement element)
    {
        if (textBlock == null || element == null) { return; }
        textBlock.Text = TextWithVariables("label.xmlcode", "XML code:\\n[xml]", ("[xml]", element.Element.ToString(SaveOptions.None)));
        textBlock.RectTransform.MinSize = GetXmlPreviewSize(element);
    }

    private static Point GetXmlPreviewSize(ContentXElement element)
    {
        string text = element?.Element?.ToString(SaveOptions.None) ?? "";
        int lines = 1; // Account for the "XML code:" header line
        foreach (string line in text.Split('\n'))
        {
            lines += 1 + (line.Length / 50);
        }
        return new Point(0, Math.Max(GUI.IntScale(18 + lines * 16), GUI.IntScale(76)));
    }

    private static Rectangle GetEffectiveSourceRect(WearableSprite wearableSprite)
    {
        return wearableSprite?.Sprite?.SourceRect ?? wearableSprite?.SourceElement?.GetAttributeRect("sourcerect", Rectangle.Empty) ?? Rectangle.Empty;
    }

    private static Vector2 GetEffectiveOrigin(WearableSprite wearableSprite)
    {
        return wearableSprite?.Sprite?.RelativeOrigin ?? wearableSprite?.SourceElement?.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f)) ?? new Vector2(0.5f, 0.5f);
    }

    private static void ApplySpriteSize(WearableSprite wearableSprite, Vector2 relativeSize)
    {
        if (wearableSprite?.Sprite == null) { return; }
        Rectangle rect = wearableSprite.Sprite.SourceRect;
        wearableSprite.Sprite.size = new Vector2(relativeSize.X * rect.Width, relativeSize.Y * rect.Height);
    }

    private static void SetWearableSpriteProperty(WearableSprite wearableSprite, string propertyName, object value)
    {
        if (wearableSprite == null) { return; }
        AccessTools.Property(wearableSprite.GetType(), propertyName)?.SetValue(wearableSprite, value);
    }

    private static void SetPointAttribute(ContentXElement element, string attribute, Point value)
    {
        element?.Element?.SetAttributeValue(attribute, $"{value.X},{value.Y}");
    }

    private static void SetOptionalStringAttribute(ContentXElement element, string attribute, string value)
    {
        if (element?.Element == null) { return; }
        element.Element.SetAttributeValue(attribute, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    private static void SetOptionalAttributeValue(ContentXElement element, string attribute, string value, string defaultValue)
    {
        if (element?.Element == null) { return; }
        element.Element.SetAttributeValue(attribute, string.Equals(value, defaultValue, StringComparison.OrdinalIgnoreCase) ? null : value);
    }

    private static string ValueOrDefault(ContentXElement element, string attribute, string defaultText)
    {
        string value = element.GetAttributeString(attribute, null);
        return string.IsNullOrWhiteSpace(value) ? defaultText : value;
    }

    private static string GetInheritTextureScaleAttribute(ContentXElement element)
    {
        return element.GetAttributeString("inheritscale", null) == null && element.GetAttributeString("inherittexturescale", null) != null
            ? "inherittexturescale"
            : "inheritscale";
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
