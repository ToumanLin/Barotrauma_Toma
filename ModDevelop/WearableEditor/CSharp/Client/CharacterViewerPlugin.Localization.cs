using Barotrauma;
using System;

namespace CharacterViewer;

public sealed partial class CharacterViewerPlugin
{
    private const string TextTagPrefix = "wearableeditor.";

    private static LocalizedString Text(string tag, string fallback)
    {
        return TextManager.Get(TextTagPrefix + tag).Fallback(fallback);
    }

    private static LocalizedString TextWithVariables(string tag, string fallback, params (string Key, string Value)[] replacements)
    {
        LocalizedString text = Text(tag, fallback);
        foreach ((string key, string value) in replacements)
        {
            text = text.Replace(key, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    private static LocalizedString LocalizedWindowTitle(string title)
    {
        return title switch
        {
            WindowTitleCharacterViewer => Text("window.characterviewer", WindowTitleCharacterViewer),
            WindowTitleClothingManager => Text("window.clothingmanager", WindowTitleClothingManager),
            WindowTitleBodySprite => Text("window.bodysprite", WindowTitleBodySprite),
            WindowTitleHeadSprite => Text("window.headsprite", WindowTitleHeadSprite),
            WindowTitleClothingSprite => Text("window.clothingsprite", WindowTitleClothingSprite),
            WindowTitleWearableSpriteList => Text("window.wearablespritelist", WindowTitleWearableSpriteList),
            _ => title
        };
    }
}
