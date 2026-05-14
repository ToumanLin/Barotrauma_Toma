using Barotrauma;
using Barotrauma.CharacterEditor;
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
    private ItemPrefab wearableEditorPrefab;

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
        }
        else
        {
            wearableEditorPrefab = null;
            wearableEditorRebuildQueued = false;
            ParamsEditor.Instance.Clear();
        }
    }

    private void UpdateWearableEditor()
    {
        if (!wearableEditorEnabled) { return; }

        DisableVanillaEditorModes();

        if (wearableEditorPrefab != selectedClothingPrefab)
        {
            QueueWearableEditorRebuild();
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

        List<(Limb limb, WearableSprite sprite)> sprites = GetSelectedWearableSprites()
            .Where(static tuple => tuple.sprite?.SourceElement != null)
            .ToList();

        if (sprites.Count == 0)
        {
            CreateEditorMessage(list, "Selected clothing has no equipped sprite entries.");
            RefreshListScrollBar(list);
            return;
        }

        CreateEditorMessage(list, $"Wearable Editor: {selectedClothingPrefab.Name}");
        for (int i = 0; i < sprites.Count; i++)
        {
            CreateWearableSpriteEditor(list, sprites[i].limb, sprites[i].sprite, i);
        }

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

    private void CreateWearableSpriteEditor(GUIListBox list, Limb limb, WearableSprite wearableSprite, int index)
    {
        ContentXElement spriteElement = wearableSprite.SourceElement;
        XElement element = spriteElement.Element;
        if (!originalWearableSpriteElements.ContainsKey(element))
        {
            originalWearableSpriteElements[element] = new XElement(element);
        }

        int entryWidth = Math.Max(GUI.IntScale(340), list.Rect.Width - GUI.IntScale(34));
        var layout = new GUILayoutGroup(new RectTransform(new Point(entryWidth, GUI.IntScale(480)), list.Content.RectTransform), childAnchor: Anchor.TopLeft)
        {
            Stretch = false,
            AbsoluteSpacing = GUI.IntScale(4)
        };

        string title = spriteElement.GetAttributeString("name", null) ?? $"{selectedClothingPrefab.Name} {wearableSprite.Limb}";
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), layout.RectTransform) { MinSize = new Point(0, GUI.IntScale(24)) },
            $"{index + 1}. {title}",
            font: GUIStyle.SubHeadingFont,
            wrap: true)
        {
            CanBeFocused = false
        };

        GUITextBlock xmlPreview = null;

        CreateTextInputLine(layout, "name", spriteElement.GetAttributeString("name", ""), value =>
        {
            SetAttributeValue(spriteElement, "name", value);
            RefreshXmlPreview(xmlPreview, spriteElement);
        });
        CreateTextInputLine(layout, "texture", spriteElement.GetAttributeString("texture", Path.GetFileName(wearableSprite.SpritePath ?? wearableSprite.Sprite?.FilePath.Value ?? "")), value =>
        {
            SetAttributeValue(spriteElement, "texture", value);
            RefreshXmlPreview(xmlPreview, spriteElement);
        });

        var limbRow = CreateEditorRow(layout, "limb");
        CreateLimbDropdown(limbRow, "limb", spriteElement.GetAttributeString("limb", wearableSprite.Limb.ToString()), false, value =>
        {
            SetOptionalAttributeValue(spriteElement, "limb", value, "default");
            RefreshXmlPreview(xmlPreview, spriteElement);
            ReequipSelectedWearable();
        });
        CreateLimbDropdown(limbRow, "depthlimb", ValueOrDefault(spriteElement, "depthlimb", "default"), true, value =>
        {
            SetOptionalAttributeValue(spriteElement, "depthlimb", value, "default");
            RefreshXmlPreview(xmlPreview, spriteElement);
            ReequipSelectedWearable();
        });

        var boolRow = CreateEditorRow(layout, "flags");
        CreateBoolTickBox(boolRow, "hidelimb", spriteElement.GetAttributeBool("hidelimb", false), value =>
        {
            SetAttributeValue(spriteElement, "hidelimb", value.ToString().ToLowerInvariant());
            RefreshXmlPreview(xmlPreview, spriteElement);
            ReequipSelectedWearable();
        });
        string textureScaleAttribute = GetInheritTextureScaleAttribute(spriteElement);
        CreateBoolTickBox(boolRow, "inherittexturescale", spriteElement.GetAttributeBool(textureScaleAttribute, false), value =>
        {
            SetAttributeValue(spriteElement, textureScaleAttribute, value.ToString().ToLowerInvariant());
            RefreshXmlPreview(xmlPreview, spriteElement);
            ReequipSelectedWearable();
        });

        Rectangle sourceRect = wearableSprite.Sprite?.SourceRect ?? spriteElement.GetAttributeRect("sourcerect", Rectangle.Empty);
        Vector2 origin = wearableSprite.Sprite?.RelativeOrigin ?? spriteElement.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));

        var rectRow = CreateEditorRow(layout, "sourcerect");
        CreateIntInput(rectRow, "x", sourceRect.X, value => UpdateSourceRectValue(wearableSprite, r => { r.X = value; return r; }, xmlPreview));
        CreateIntInput(rectRow, "y", sourceRect.Y, value => UpdateSourceRectValue(wearableSprite, r => { r.Y = value; return r; }, xmlPreview));
        CreateIntInput(rectRow, "w", sourceRect.Width, value => UpdateSourceRectValue(wearableSprite, r => { r.Width = Math.Max(1, value); return r; }, xmlPreview));
        CreateIntInput(rectRow, "h", sourceRect.Height, value => UpdateSourceRectValue(wearableSprite, r => { r.Height = Math.Max(1, value); return r; }, xmlPreview));

        var originRow = CreateEditorRow(layout, "origin");
        CreateFloatInput(originRow, "x", origin.X, value => UpdateOriginValue(wearableSprite, o => { o.X = value; return o; }, xmlPreview));
        CreateFloatInput(originRow, "y", origin.Y, value => UpdateOriginValue(wearableSprite, o => { o.Y = value; return o; }, xmlPreview));

        var inheritRow = CreateEditorRow(layout, "inherit");
        CreateDefaultBoolDropdown(inheritRow, "inheritsourcerect", ValueOrDefault(spriteElement, "inheritsourcerect", "default:false"), value =>
        {
            SetDefaultBoolAttribute(spriteElement, "inheritsourcerect", value);
            RefreshXmlPreview(xmlPreview, spriteElement);
            ReequipSelectedWearable();
        });
        CreateDefaultBoolDropdown(inheritRow, "inheritorigin", ValueOrDefault(spriteElement, "inheritorigin", "default:false"), value =>
        {
            SetDefaultBoolAttribute(spriteElement, "inheritorigin", value);
            RefreshXmlPreview(xmlPreview, spriteElement);
            ReequipSelectedWearable();
        });

        xmlPreview = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), layout.RectTransform) { MinSize = new Point(0, GUI.IntScale(104)) },
            "",
            wrap: true,
            font: GUIStyle.SmallFont)
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
        });
    }

    private static GUILayoutGroup CreateEditorRow(GUILayoutGroup parent, string label)
    {
        var row = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform) { MinSize = new Point(0, GUI.IntScale(32)) }, isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.01f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.18f, 1.0f), row.RectTransform), label, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft)
        {
            CanBeFocused = false
        };
        return row;
    }

    private static void CreateReadOnlyLine(GUILayoutGroup parent, string label, string value)
    {
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform) { MinSize = new Point(0, GUI.IntScale(22)) },
            $"{label} [{value}]",
            wrap: true,
            font: GUIStyle.SmallFont)
        {
            CanBeFocused = false
        };
    }

    private static void CreateTextInputLine(GUILayoutGroup parent, string label, string value, Action<string> onChanged)
    {
        var row = CreateEditorRow(parent, label);
        var input = new GUITextBox(new RectTransform(new Vector2(0.78f, 1.0f), row.RectTransform), value ?? "", style: "GUITextBox")
        {
            Font = GUIStyle.SmallFont,
            OnTextChangedDelegate = (_, text) =>
            {
                onChanged(text);
                return true;
            }
        };
        input.Text = value ?? "";
    }

    private static void CreateLimbDropdown(GUILayoutGroup row, string label, string value, bool allowDefault, Action<string> onChanged)
    {
        var holder = new GUILayoutGroup(new RectTransform(new Vector2(0.41f, 1.0f), row.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.35f, 1.0f), holder.RectTransform), label, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft)
        {
            CanBeFocused = false
        };
        var dropDown = new GUIDropDown(new RectTransform(new Vector2(0.62f, 1.0f), holder.RectTransform), string.IsNullOrWhiteSpace(value) ? "default" : value);
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

    private static void CreateBoolTickBox(GUILayoutGroup row, string label, bool value, Action<bool> onChanged)
    {
        var tickBox = new GUITickBox(new RectTransform(new Vector2(0.41f, 1.0f), row.RectTransform), label, font: GUIStyle.SmallFont)
        {
            Selected = value,
            OnSelected = box =>
            {
                onChanged(box.Selected);
                return true;
            }
        };
        tickBox.TextBlock.Wrap = true;
    }

    private static void CreateDefaultBoolDropdown(GUILayoutGroup row, string label, string value, Action<string> onChanged)
    {
        var holder = new GUILayoutGroup(new RectTransform(new Vector2(0.41f, 1.0f), row.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.48f, 1.0f), holder.RectTransform), label, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft)
        {
            CanBeFocused = false
        };
        string selected = string.IsNullOrWhiteSpace(value) ? "default:false" : value;
        var dropDown = new GUIDropDown(new RectTransform(new Vector2(0.49f, 1.0f), holder.RectTransform), selected);
        foreach (string option in new[] { "default:false", "false", "true" })
        {
            dropDown.AddItem(option, option);
        }
        dropDown.SelectItem(selected);
        dropDown.OnSelected = (_, data) =>
        {
            if (data is string selectedValue)
            {
                onChanged(selectedValue);
            }
            return true;
        };
    }

    private static void CreateIntInput(GUILayoutGroup row, string label, int value, Action<int> onChanged)
    {
        var holder = new GUILayoutGroup(new RectTransform(new Vector2(0.205f, 1.0f), row.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.25f, 1.0f), holder.RectTransform), label, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft);
        var input = new GUINumberInput(new RectTransform(new Vector2(0.72f, 1.0f), holder.RectTransform), NumberType.Int, relativeButtonAreaWidth: 0.2f)
        {
            IntValue = value,
            MinValueInt = 0,
            Font = GUIStyle.SmallFont
        };
        input.OnValueChanged += numberInput => onChanged(numberInput.IntValue);
    }

    private static void CreateFloatInput(GUILayoutGroup row, string label, float value, Action<float> onChanged)
    {
        var holder = new GUILayoutGroup(new RectTransform(new Vector2(0.41f, 1.0f), row.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.18f, 1.0f), holder.RectTransform), label, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft);
        var input = new GUINumberInput(new RectTransform(new Vector2(0.78f, 1.0f), holder.RectTransform), NumberType.Float, relativeButtonAreaWidth: 0.2f)
        {
            FloatValue = value,
            MinValueFloat = 0.0f,
            MaxValueFloat = 1.0f,
            ValueStep = 0.001f,
            DecimalsToDisplay = 4,
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

    private void UpdateSourceRectValue(WearableSprite wearableSprite, Func<Rectangle, Rectangle> edit, GUITextBlock xmlPreview)
    {
        if (wearableSprite?.Sprite == null) { return; }

        Rectangle rect = wearableSprite.Sprite.SourceRect;
        rect = edit(rect);
        rect.Width = Math.Max(1, rect.Width);
        rect.Height = Math.Max(1, rect.Height);

        wearableSprite.SourceElement.SetAttributeValue("sourcerect", $"{rect.X},{rect.Y},{rect.Width},{rect.Height}");
        wearableSprite.Sprite.SourceRect = rect;
        wearableSprite.Sprite.RelativeOrigin = wearableSprite.Sprite.RelativeOrigin;

        RefreshXmlPreview(xmlPreview, wearableSprite.SourceElement);
        UpdateAllViewerSpriteInfo();
    }

    private void UpdateOriginValue(WearableSprite wearableSprite, Func<Vector2, Vector2> edit, GUITextBlock xmlPreview)
    {
        if (wearableSprite?.Sprite == null) { return; }

        Vector2 origin = wearableSprite.Sprite.RelativeOrigin;
        origin = edit(origin);
        origin = Vector2.Clamp(origin, Vector2.Zero, Vector2.One);

        wearableSprite.SourceElement.SetAttributeValue("origin", $"{FormatFloat(origin.X)},{FormatFloat(origin.Y)}");
        wearableSprite.Sprite.RelativeOrigin = origin;

        RefreshXmlPreview(xmlPreview, wearableSprite.SourceElement);
        UpdateAllViewerSpriteInfo();
    }

    private void ReequipSelectedWearable()
    {
        if (selectedClothingPrefab == null) { return; }
        EquipViewerClothing(selectedClothingPrefab);
        QueueWearableEditorRebuild();
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
            XDocument document = spriteElement.Document ?? selectedClothingPrefab?.ConfigElement?.Document;
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
        textBlock.Text = $"XML code: [{element.Element.ToString(SaveOptions.DisableFormatting)}]";
    }

    private static string ValueOrDefault(ContentXElement element, string attribute, string defaultText)
    {
        string value = element.GetAttributeString(attribute, null);
        return string.IsNullOrWhiteSpace(value) ? defaultText : value;
    }

    private static string GetInheritTextureScaleText(ContentXElement element)
    {
        string inheritScale = element.GetAttributeString("inheritscale", null);
        if (!string.IsNullOrWhiteSpace(inheritScale)) { return $"inheritscale:{inheritScale}"; }
        return ValueOrDefault(element, "inherittexturescale", "default:false");
    }

    private static string GetInheritTextureScaleAttribute(ContentXElement element)
    {
        return element.GetAttributeString("inheritscale", null) == null ? "inherittexturescale" : "inheritscale";
    }

    private static void SetAttributeValue(ContentXElement element, string attribute, string value)
    {
        element?.Element?.SetAttributeValue(attribute, value ?? "");
    }

    private static void SetOptionalAttributeValue(ContentXElement element, string attribute, string value, string defaultValue)
    {
        if (element?.Element == null) { return; }
        element.Element.SetAttributeValue(attribute, string.Equals(value, defaultValue, StringComparison.OrdinalIgnoreCase) ? null : value);
    }

    private static void SetDefaultBoolAttribute(ContentXElement element, string attribute, string value)
    {
        if (element?.Element == null) { return; }
        element.Element.SetAttributeValue(attribute, string.Equals(value, "default:false", StringComparison.OrdinalIgnoreCase) ? null : value);
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
