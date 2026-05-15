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
    private sealed class WearableSpriteSelection
    {
        public Limb Limb;
        public WearableSprite Sprite;
        public XElement Element => Sprite?.SourceElement?.Element;
    }

    private readonly Dictionary<XElement, XElement> originalWearableSpriteElements = new Dictionary<XElement, XElement>();

    private bool wearableEditorEnabled;
    private bool wearableEditorRebuildQueued;
    private bool wearableSpriteListRebuildQueued;
    private ItemPrefab wearableEditorPrefab;
    private XElement selectedWearableSpriteElement;
    private XElement pendingPreviewSelectedWearableSpriteElement;

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
        var tickBox = modesPanel?.GetAllChildren().OfType<GUITickBox>().FirstOrDefault(c => c.UserData as string == "CharacterViewer.WearableEditorToggle");
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

    private void CreateWearableSpriteListWindow()
    {
        GUILayoutGroup content = CreateFloatingWindow("Wearable Sprite List", new Point(470, 260), new Point(780, 15), out wearableSpriteListWindow);
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
        List<WearableSpriteSelection> entries = GetWearableSpriteSelections();
        if (entries.Count == 0)
        {
            CreateEditorMessage(wearableSpriteListBox, selectedClothingPrefab == null ? "No clothing selected." : "Selected clothing has no sprite entries.");
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

        RefreshListScrollBar(wearableSpriteListBox);
    }

    private void SelectWearableSprite(XElement element)
    {
        if (element == null || selectedWearableSpriteElement == element) { return; }
        selectedWearableSpriteElement = element;
        QueueWearableEditorRebuild();
        QueueWearableSpriteListRebuild();
        UpdateAllViewerSpriteInfo();
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
            CreateEditorMessage(list, "No clothing selected.");
            RefreshListScrollBar(list);
            return;
        }

        WearableSpriteSelection selection = GetSelectedWearableSpriteSelection();
        if (selection?.Sprite == null)
        {
            CreateEditorMessage(list, "Selected clothing has no equipped sprite entries.");
            RefreshListScrollBar(list);
            return;
        }

        CreateSelectedWearableSpriteEditor(list, selection.Sprite);
        RefreshListScrollBar(list);
    }

    private static void CreateEditorMessage(GUIListBox list, string text)
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

        CreateStringInputLine(layout, "name", spriteElement, "name", "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "name", value);
            if (wearableSprite.Sprite != null) { wearableSprite.Sprite.Name = value; }
            directRefresh();
        });
        CreateStringInputLine(layout, "texture", spriteElement, "texture", wearableSprite.SpritePath ?? "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "texture", value);
            reEquipRefresh();
        });
        CreateVector2IntLine(layout, "sourcerect", GetEffectiveSourceRect(wearableSprite), (x, y, w, h) =>
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
        CreatePointLine(layout, "sheetindex", spriteElement.GetAttributePoint("sheetindex", new Point(-1, -1)), value =>
        {
            SetPointAttribute(spriteElement, "sheetindex", value);
            reEquipRefresh();
        });
        CreatePointLine(layout, "sheetelementsize", spriteElement.GetAttributePoint("sheetelementsize", Point.Zero), value =>
        {
            SetPointAttribute(spriteElement, "sheetelementsize", value);
            reEquipRefresh();
        });
        CreateVector2FloatLine(layout, "origin", GetEffectiveOrigin(wearableSprite), 0.001f, 4, value =>
        {
            value = Vector2.Clamp(value, Vector2.Zero, Vector2.One);
            spriteElement.SetAttributeValue("origin", $"{FormatFloat(value.X)},{FormatFloat(value.Y)}");
            if (wearableSprite.Sprite != null) { wearableSprite.Sprite.RelativeOrigin = value; }
            directRefresh();
        });
        CreateVector2FloatLine(layout, "size", spriteElement.GetAttributeVector2("size", Vector2.One), 0.01f, 3, value =>
        {
            spriteElement.SetAttributeValue("size", $"{FormatFloat(value.X)},{FormatFloat(value.Y)}");
            ApplySpriteSize(wearableSprite, value);
            directRefresh();
        });
        CreateFloatLine(layout, "depth", spriteElement.GetAttributeFloat("depth", wearableSprite.Sprite?.Depth ?? 0.001f), 0.001f, 4, value =>
        {
            value = MathHelper.Clamp(value, 0.001f, 0.999f);
            spriteElement.SetAttributeValue("depth", FormatFloat(value));
            if (wearableSprite.Sprite != null) { wearableSprite.Sprite.Depth = value; }
            directRefresh();
        });
        CreateBoolLine(layout, "compress", spriteElement, "compress", true, reEquipRefresh);
        CreateLimbDropdownLine(layout, "limb", spriteElement.GetAttributeString("limb", wearableSprite.Limb.ToString()), false, value =>
        {
            SetOptionalAttributeValue(spriteElement, "limb", value, "default");
            reEquipRefresh();
        });
        CreateBoolLine(layout, "hidelimb", spriteElement, "hidelimb", false, reEquipRefresh);
        CreateBoolLine(layout, "hideotherwearables", spriteElement, "hideotherwearables", false, reEquipRefresh);
        CreateBoolLine(layout, "alphaclipotherwearables", spriteElement, "alphaclipotherwearables", false, reEquipRefresh);
        CreateBoolLine(layout, "canbehiddenbyotherwearables", spriteElement, "canbehiddenbyotherwearables", true, reEquipRefresh);
        CreateStringInputLine(layout, "canbehiddenbyitem", spriteElement, "canbehiddenbyitem", "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "canbehiddenbyitem", value);
            reEquipRefresh();
        });
        CreateStringInputLine(layout, "hidewearablesoftype", spriteElement, "hidewearablesoftype", "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "hidewearablesoftype", value);
            reEquipRefresh();
        });
        CreateBoolLine(layout, "inheritlimbdepth", spriteElement, "inheritlimbdepth", true, reEquipRefresh);
        CreateLimbDropdownLine(layout, "depthlimb", ValueOrDefault(spriteElement, "depthlimb", "default"), true, value =>
        {
            SetOptionalAttributeValue(spriteElement, "depthlimb", value, "default");
            reEquipRefresh();
        });
        string scaleAttribute = GetInheritTextureScaleAttribute(spriteElement);
        CreateBoolLine(layout, "inheritscale", spriteElement, scaleAttribute, false, reEquipRefresh);
        CreateBoolLine(layout, "ignorelimbscale", spriteElement, "ignorelimbscale", false, reEquipRefresh);
        CreateBoolLine(layout, "ignoretexturescale", spriteElement, "ignoretexturescale", false, reEquipRefresh);
        CreateBoolLine(layout, "ignoreragdollscale", spriteElement, "ignoreragdollscale", false, reEquipRefresh);
        CreateBoolLine(layout, "inheritorigin", spriteElement, "inheritorigin", false, reEquipRefresh);
        CreateBoolLine(layout, "inheritsourcerect", spriteElement, "inheritsourcerect", false, reEquipRefresh);
        CreateFloatLine(layout, "scale", spriteElement.GetAttributeFloat("scale", 1.0f), 0.01f, 3, value =>
        {
            spriteElement.SetAttributeValue("scale", FormatFloat(value));
            SetWearableSpriteProperty(wearableSprite, "Scale", value);
            directRefresh();
        });
        CreateFloatLine(layout, "rotation", spriteElement.GetAttributeFloat("rotation", 0.0f), 0.1f, 2, value =>
        {
            spriteElement.SetAttributeValue("rotation", FormatFloat(value));
            SetWearableSpriteProperty(wearableSprite, "Rotation", MathHelper.ToRadians(value));
            directRefresh();
        });
        CreateStringInputLine(layout, "sound", spriteElement, "sound", "", value =>
        {
            SetOptionalStringAttribute(spriteElement, "sound", value);
            reEquipRefresh();
        });

        CreateNestedPlaceholder(layout, spriteElement);

        xmlPreview = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), layout.RectTransform)
        {
            MinSize = GetXmlPreviewSize(spriteElement)
        }, "", wrap: true, font: GUIStyle.SmallFont)
        {
            CanBeFocused = false
        };
        RefreshXmlPreview(xmlPreview, spriteElement);

        var buttonRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), layout.RectTransform) { MinSize = new Point(0, GUI.IntScale(32)) }, isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        CreateEditorButton(buttonRow, "Save", () => SaveWearableXml(spriteElement));
        CreateEditorButton(buttonRow, "Revert", () =>
        {
            RevertWearableSprite(wearableSprite);
            QueueWearableEditorRebuild();
            QueueWearableSpriteListRebuild();
        });
    }

    private static GUILayoutGroup CreateEditorRow(GUILayoutGroup parent, string label)
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

    private static void CreateStringInputLine(GUILayoutGroup parent, string label, ContentXElement element, string attribute, string defaultValue, Action<string> onChanged)
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

    private static void CreateBoolLine(GUILayoutGroup parent, string label, ContentXElement element, string attribute, bool defaultValue, Action onChanged)
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
        new GUIButton(new RectTransform(new Vector2(0.36f, 1.0f), row.RectTransform), "Default", style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                element.Element.SetAttributeValue(attribute, null);
                onChanged();
                return true;
            }
        };
    }

    private static void CreateLimbDropdownLine(GUILayoutGroup parent, string label, string value, bool allowDefault, Action<string> onChanged)
    {
        var row = CreateEditorRow(parent, label);
        CreateLimbDropdown(row, label, value, allowDefault, onChanged);
    }

    private static void CreateLimbDropdown(GUILayoutGroup row, string label, string value, bool allowDefault, Action<string> onChanged)
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

    private static void CreatePointLine(GUILayoutGroup parent, string label, Point value, Action<Point> onChanged)
    {
        int x = value.X;
        int y = value.Y;
        var row = CreateEditorRow(parent, label);
        CreateIntInput(row, "x", x, newValue => { x = newValue; onChanged(new Point(x, y)); });
        CreateIntInput(row, "y", y, newValue => { y = newValue; onChanged(new Point(x, y)); });
    }

    private static void CreateVector2IntLine(GUILayoutGroup parent, string label, Rectangle value, Action<int, int, int, int> onChanged)
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

    private static void CreateVector2FloatLine(GUILayoutGroup parent, string label, Vector2 value, float step, int decimals, Action<Vector2> onChanged)
    {
        float x = value.X;
        float y = value.Y;
        var row = CreateEditorRow(parent, label);
        CreateFloatInput(row, "x", x, step, decimals, newValue => { x = newValue; onChanged(new Vector2(x, y)); });
        CreateFloatInput(row, "y", y, step, decimals, newValue => { y = newValue; onChanged(new Vector2(x, y)); });
    }

    private static void CreateFloatLine(GUILayoutGroup parent, string label, float value, float step, int decimals, Action<float> onChanged)
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

    private static void CreateEditorButton(GUILayoutGroup row, string text, Action onClicked)
    {
        new GUIButton(new RectTransform(Vector2.One, row.RectTransform), text, style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                onClicked();
                return true;
            }
        };
    }

    private static void CreateNestedPlaceholder(GUILayoutGroup parent, ContentXElement spriteElement)
    {
        int lightCount = spriteElement.Elements().Count(e => e.Name.ToString().Equals("LightComponent", StringComparison.OrdinalIgnoreCase));
        int overrideCount = spriteElement.Elements().Count(e => e.Name.ToString().Equals("override", StringComparison.OrdinalIgnoreCase));
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform) { MinSize = new Point(0, GUI.IntScale(44)) },
            $"Nested nodes: LightComponent [{lightCount}]  override [{overrideCount}]\nNested node editing: planned",
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

    private void SaveWearableXml(ContentXElement spriteElement)
    {
        if (!TryGetSelectedWearableXmlPath(spriteElement, out string path)) { return; }
        if (!IsPathInLocalMods(path))
        {
            new GUIMessageBox("Wearable Editor", $"Only XML files in LocalMods can be saved.\n\n{path}");
            return;
        }

        try
        {
            XDocument document = GetWritableWearableDocument(spriteElement, path);
            if (document == null)
            {
                new GUIMessageBox("Wearable Editor", "Could not find the XML document for this wearable.");
                return;
            }

            var settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = false
            };
            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                document.Save(writer);
            }
            GUI.AddMessage($"Wearable XML saved to {path}", GUIStyle.Green, font: GUIStyle.Font, lifeTime: 5);
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError($"CharacterViewer failed to save wearable XML: {ex}");
            new GUIMessageBox("Wearable Editor", $"Failed to save XML.\n\n{ex.Message}");
        }
    }

    private XDocument GetWritableWearableDocument(ContentXElement spriteElement, string path)
    {
        XDocument document = spriteElement.Document ?? selectedClothingPrefab?.ConfigElement?.Document;
        if (document != null)
        {
            return document;
        }

        document = XMLExtensions.TryLoadXml(path);
        if (document == null)
        {
            return null;
        }

        if (TryFindWritableSpriteElement(document, spriteElement, out XElement writableElement))
        {
            writableElement.ReplaceWith(new XElement(spriteElement.Element));
            return document;
        }

        LuaCsLogger.LogError($"CharacterViewer could not find matching sprite element in {path}.");
        return null;
    }

    private bool TryFindWritableSpriteElement(XDocument document, ContentXElement spriteElement, out XElement writableElement)
    {
        writableElement = null;
        if (document?.Root == null || spriteElement?.Element == null) { return false; }

        if (originalWearableSpriteElements.TryGetValue(spriteElement.Element, out XElement original))
        {
            string originalText = original.ToString(SaveOptions.DisableFormatting);
            writableElement = document
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("sprite", StringComparison.OrdinalIgnoreCase) &&
                                     e.ToString(SaveOptions.DisableFormatting) == originalText);
            if (writableElement != null) { return true; }
        }

        int spriteIndex = GetSelectedPrefabSpriteIndex(spriteElement.Element);
        if (spriteIndex < 0) { return false; }

        XElement prefabElement = document
            .Descendants()
            .FirstOrDefault(e =>
                e.Elements().Any(child => child.Name.LocalName.Equals("Wearable", StringComparison.OrdinalIgnoreCase)) &&
                (e.GetAttributeString("identifier", null) == selectedClothingPrefab.Identifier.Value ||
                 e.GetAttributeString("name", null) == selectedClothingPrefab.Name.ToString()));
        IEnumerable<XElement> spriteElements = (prefabElement ?? document.Root)
            .Descendants()
            .Where(e => e.Name.LocalName.Equals("sprite", StringComparison.OrdinalIgnoreCase));
        writableElement = spriteElements.ElementAtOrDefault(spriteIndex);
        return writableElement != null;
    }

    private int GetSelectedPrefabSpriteIndex(XElement element)
    {
        if (selectedClothingPrefab?.ConfigElement == null || element == null) { return -1; }
        List<ContentXElement> sprites = selectedClothingPrefab.ConfigElement
            .GetChildElements("Wearable")
            .SelectMany(static wearable => wearable.GetChildElements("sprite"))
            .ToList();
        return sprites.FindIndex(sprite => sprite.Element == element);
    }

    private bool TryGetSelectedWearableXmlPath(ContentXElement spriteElement, out string path)
    {
        path = selectedClothingPrefab?.FilePath?.FullPath;
        if (!string.IsNullOrWhiteSpace(spriteElement?.BaseUri))
        {
            path = Uri.TryCreate(spriteElement.BaseUri, UriKind.Absolute, out Uri uri) && uri.IsFile
                ? uri.LocalPath
                : Path.GetFullPath(spriteElement.BaseUri);
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            new GUIMessageBox("Wearable Editor", "No wearable XML file is selected.");
            return false;
        }
        return true;
    }

    private static bool IsPathInLocalMods(string path)
    {
        string fullPath = Path.GetFullPath(path).CleanUpPathCrossPlatform(correctFilenameCase: false);
        string localModsPath = Path.GetFullPath(ContentPackage.LocalModsDir).CleanUpPathCrossPlatform(correctFilenameCase: false);
        if (!localModsPath.EndsWith("/", StringComparison.Ordinal))
        {
            localModsPath += "/";
        }
        return fullPath.StartsWith(localModsPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void RefreshXmlPreview(GUITextBlock textBlock, ContentXElement element)
    {
        if (textBlock == null || element == null) { return; }
        textBlock.Text = $"XML code:\n{element.Element.ToString(SaveOptions.None)}";
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
