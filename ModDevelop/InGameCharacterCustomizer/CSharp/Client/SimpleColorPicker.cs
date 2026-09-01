using Barotrauma;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace InGameCharacterCustomizer;

internal sealed class SimpleColorPicker : IDisposable
{
    private static readonly Regex RgbPattern = new Regex(
        @"^\s*\[?\s*(\d{1,3})\s*\]?\s*,\s*\[?\s*(\d{1,3})\s*\]?\s*,\s*\[?\s*(\d{1,3})\s*\]?\s*$",
        RegexOptions.Compiled);

    private readonly GUIFrame root;
    private readonly GUIColorPicker picker;
    private readonly GUITextBox colorInput;
    private readonly GUIFrame pendingPreview;
    private readonly Color?[] swatches;
    private readonly GUIButton[] swatchButtons = new GUIButton[7];
    private readonly Action<Color> onApply;
    private readonly Action<SimpleColorPicker> onClosed;

    private Color pendingColor;
    private bool disposed;

    public SimpleColorPicker(
        GUIComponent parent,
        LocalizedString title,
        Color currentColor,
        Color?[] swatches,
        Action<Color> onApply,
        Action<SimpleColorPicker> onClosed)
    {
        this.swatches = swatches ?? throw new ArgumentNullException(nameof(swatches));
        this.onApply = onApply;
        this.onClosed = onClosed;
        pendingColor = Opaque(currentColor);

        root = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform), style: null, color: Color.Black * 0.72f)
        {
            CanBeFocused = true
        };

        var window = new GUIFrame(new RectTransform(new Vector2(0.48f, 0.70f), root.RectTransform, Anchor.Center)
        {
            MinSize = new Point(440, 510),
            MaxSize = new Point(720, 760)
        }, style: "GUIFrame")
        {
            CanBeFocused = true
        };

        var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.92f, 0.92f), window.RectTransform, Anchor.Center))
        {
            Stretch = true,
            AbsoluteSpacing = GUI.IntScale(6f)
        };

        new GUITextBlock(new RectTransform(new Vector2(1f, 0.07f), layout.RectTransform), title,
            font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);

        picker = new GUIColorPicker(new RectTransform(new Vector2(1f, 0.46f), layout.RectTransform));
        SetPickerColor(pendingColor);

        var comparisonRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.03f
        };
        CreateColorPreview(comparisonRow, currentColor, "Current");
        pendingPreview = CreateColorPreview(comparisonRow, pendingColor, "New");

        var inputRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.09f), layout.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.03f
        };
        new GUITextBlock(new RectTransform(new Vector2(0.25f, 1f), inputRow.RectTransform), "RGB / Hex", textAlignment: Alignment.CenterLeft);
        colorInput = new GUITextBox(new RectTransform(new Vector2(0.75f, 1f), inputRow.RectTransform), ToHex(pendingColor), createPenIcon: false)
        {
            OverflowClip = true,
            ToolTip = "#RRGGBB or [R],[G],[B]"
        };

        new GUITextBlock(new RectTransform(new Vector2(1f, 0.045f), layout.RectTransform),
            "Color slots — left: load, right: store", font: GUIStyle.SmallFont, textAlignment: Alignment.Center);

        var swatchRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.085f), layout.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.015f
        };
        for (int i = 0; i < swatchButtons.Length; i++)
        {
            int slot = i;
            GUIButton button = new GUIButton(new RectTransform(new Vector2(1f / swatchButtons.Length, 1f), swatchRow.RectTransform), (i + 1).ToString(), style: "GUIButtonSmall")
            {
                ToolTip = "Left click: use color (empty slot stores it). Right click: store current color.",
                OnClicked = (_, _) =>
                {
                    if (this.swatches[slot].HasValue)
                    {
                        SetPendingColor(this.swatches[slot].Value, updateInput: true, updatePicker: true);
                    }
                    else
                    {
                        StoreSwatch(slot);
                    }
                    return true;
                }
            };
            button.OnSecondaryClicked += (_, _) =>
            {
                StoreSwatch(slot);
                return true;
            };
            swatchButtons[i] = button;
            RefreshSwatch(i);
        }

        var buttonRow = new GUILayoutGroup(new RectTransform(new Vector2(0.62f, 0.09f), layout.RectTransform, Anchor.Center), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.05f
        };
        new GUIButton(new RectTransform(new Vector2(0.5f, 1f), buttonRow.RectTransform), "Apply", style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                onApply?.Invoke(pendingColor);
                Dispose();
                return true;
            }
        };
        new GUIButton(new RectTransform(new Vector2(0.5f, 1f), buttonRow.RectTransform), "Cancel", style: "GUIButtonSmall")
        {
            OnClicked = (_, _) =>
            {
                Dispose();
                return true;
            }
        };

        picker.OnColorSelected = (_, color) =>
        {
            SetPendingColor(color, updateInput: true, updatePicker: false);
            return true;
        };
        colorInput.OnEnterPressed = (_, text) =>
        {
            ApplyTextInput(text);
            return true;
        };
        colorInput.OnDeselected += (_, _) => ApplyTextInput(colorInput.Text);

        layout.Recalculate();
        comparisonRow.Recalculate();
        inputRow.Recalculate();
        swatchRow.Recalculate();
        buttonRow.Recalculate();
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        picker.Dispose();
        root.RectTransform.Parent = null;
        onClosed?.Invoke(this);
    }

    private static GUIFrame CreateColorPreview(GUILayoutGroup parent, Color color, string label)
    {
        var frame = new GUIFrame(new RectTransform(new Vector2(0.5f, 1f), parent.RectTransform), style: null, color: Opaque(color))
        {
            OutlineColor = Color.White * 0.65f
        };
        new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform), label, textAlignment: Alignment.Center,
            textColor: GetContrastingTextColor(color));
        return frame;
    }

    private void ApplyTextInput(string text)
    {
        if (TryParseColor(text, out Color color))
        {
            colorInput.TextBlock.TextColor = GUIStyle.TextColorNormal;
            SetPendingColor(color, updateInput: true, updatePicker: true);
        }
        else
        {
            colorInput.TextBlock.TextColor = GUIStyle.Red;
        }
    }

    private void SetPendingColor(Color color, bool updateInput, bool updatePicker)
    {
        pendingColor = Opaque(color);
        pendingPreview.Color = pendingColor;
        pendingPreview.HoverColor = pendingColor;
        GUITextBlock label = pendingPreview.GetChild<GUITextBlock>();
        if (label != null) { label.TextColor = GetContrastingTextColor(pendingColor); }
        if (updateInput) { colorInput.Text = ToHex(pendingColor); }
        if (updatePicker) { SetPickerColor(pendingColor); }
    }

    private void SetPickerColor(Color color)
    {
        Vector3 hsv = ToolBox.RGBToHSV(color);
        bool hueChanged = !MathUtils.NearlyEqual(picker.SelectedHue, float.IsNaN(hsv.X) ? 0f : hsv.X);
        picker.SelectedHue = float.IsNaN(hsv.X) ? 0f : hsv.X;
        picker.SelectedSaturation = hsv.Y;
        picker.SelectedValue = hsv.Z;
        picker.CurrentColor = color;
        if (hueChanged) { picker.RefreshHue(); }
    }

    private void StoreSwatch(int slot)
    {
        swatches[slot] = pendingColor;
        RefreshSwatch(slot);
    }

    private void RefreshSwatch(int slot)
    {
        Color color = swatches[slot] ?? Color.DarkGray;
        swatchButtons[slot].Color = color;
        swatchButtons[slot].HoverColor = Color.Lerp(color, Color.White, 0.25f);
        swatchButtons[slot].TextColor = GetContrastingTextColor(color);
    }

    private static bool TryParseColor(string text, out Color color)
    {
        color = Color.White;
        if (string.IsNullOrWhiteSpace(text)) { return false; }

        string trimmed = text.Trim();
        if (trimmed.Length == 7 && trimmed[0] == '#' &&
            int.TryParse(trimmed.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
        {
            color = new Color((hex >> 16) & 0xff, (hex >> 8) & 0xff, hex & 0xff);
            return true;
        }

        Match match = RgbPattern.Match(trimmed);
        if (!match.Success) { return false; }

        int red = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int green = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        int blue = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (red > byte.MaxValue || green > byte.MaxValue || blue > byte.MaxValue) { return false; }

        color = new Color(red, green, blue);
        return true;
    }

    private static Color Opaque(Color color) => new Color(color.R, color.G, color.B, byte.MaxValue);

    private static string ToHex(Color color) => $"#{(color.R << 16 | color.G << 8 | color.B):X6}";

    private static Color GetContrastingTextColor(Color color)
    {
        float luminance = color.R * 0.299f + color.G * 0.587f + color.B * 0.114f;
        return luminance > 150f ? Color.Black : Color.White;
    }
}
