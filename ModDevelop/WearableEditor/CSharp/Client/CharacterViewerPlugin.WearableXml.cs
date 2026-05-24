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

    private void SaveWearableXml(ContentXElement spriteElement, bool allowNonLocalPath = false)
    {
        if (!TryGetSelectedWearableXmlPath(spriteElement, out string path)) { return; }
        if (!allowNonLocalPath && !IsPathInLocalMods(path))
        {
            ShowNonLocalSaveWarning(spriteElement, path);
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

    private void ShowNonLocalSaveWarning(ContentXElement spriteElement, string path)
    {
        var messageBox = new GUIMessageBox(
            "Wearable Editor",
            $"Only XML files in LocalMods can be saved by default.\n\n{path}\n\nSaving anyway may change a workshop mod file or Vanilla item. Steam can overwrite it.",
            new LocalizedString[] { "Cancel", "Just save it" },
            type: GUIMessageBox.Type.Warning);
        messageBox.Buttons[0].OnClicked = (_, _) =>
        {
            messageBox.Close();
            return true;
        };
        messageBox.Buttons[1].OnClicked = (_, _) =>
        {
            messageBox.Close();
            SaveWearableXml(spriteElement, allowNonLocalPath: true);
            return true;
        };
    }

    private XDocument GetWritableWearableDocument(ContentXElement spriteElement, string path)
    {
        XDocument document = spriteElement.Document;
        if (IsDocumentForPath(document, path) || IsSelectedPrefabDocumentForPath(document, path))
        {
            return document;
        }

        document = selectedClothingPrefab?.ConfigElement?.Document;
        if (IsDocumentForPath(document, path) || IsSelectedPrefabDocumentForPath(document, path))
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

    private static bool IsDocumentForPath(XDocument document, string path)
    {
        if (document == null || string.IsNullOrWhiteSpace(path)) { return false; }
        return TryGetPathFromBaseUri(document.BaseUri, out string documentPath) && AreSamePath(documentPath, path);
    }

    private bool IsSelectedPrefabDocumentForPath(XDocument document, string path)
    {
        return document != null &&
               document == selectedClothingPrefab?.ConfigElement?.Document &&
               AreSamePath(selectedClothingPrefab?.FilePath?.FullPath, path);
    }

    private static bool TryGetPathFromBaseUri(string baseUri, out string path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(baseUri)) { return false; }

        try
        {
            path = Uri.TryCreate(baseUri, UriKind.Absolute, out Uri uri) && uri.IsFile
                ? uri.LocalPath
                : Path.GetFullPath(baseUri);
            return !string.IsNullOrWhiteSpace(path);
        }
        catch
        {
            path = null;
            return false;
        }
    }

    private static bool AreSamePath(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) { return false; }
        string normalizedFirst = Path.GetFullPath(first).CleanUpPathCrossPlatform(correctFilenameCase: false);
        string normalizedSecond = Path.GetFullPath(second).CleanUpPathCrossPlatform(correctFilenameCase: false);
        return string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
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

    private void NormalizeCopiedTexturePath(XElement clone, ContentXElement sourceElement, string sourceXmlPath)
    {
        string texture = clone.GetAttributeString("texture", null);
        if (string.IsNullOrWhiteSpace(texture) || !sourceElement.Element.DoesAttributeReferenceFileNameAlone("texture")) { return; }

        if (TryResolveTexturePath(texture, sourceElement.ContentPackage ?? selectedClothingPrefab?.ContentPackage, sourceXmlPath, out string resolvedPath))
        {
            clone.SetAttributeValue("texture", resolvedPath);
        }
    }

    private void NormalizePastedTexturePath(XElement pastedElement)
    {
        string texture = pastedElement.GetAttributeString("texture", null);
        if (string.IsNullOrWhiteSpace(texture)) { return; }

        if (!TryResolveTexturePath(texture, wearableSpriteClipboard.SourcePackage, wearableSpriteClipboard.SourceXmlPath, out string resolvedPath))
        {
            return;
        }

        if (TryConvertToModDirPath(resolvedPath, selectedClothingPrefab?.ContentPackage, out string portablePath))
        {
            pastedElement.SetAttributeValue("texture", portablePath);
            return;
        }

        pastedElement.SetAttributeValue("texture", resolvedPath);
        new GUIMessageBox("Wearable Editor", $"Pasted sprite texture could not be matched to a content package, so an absolute path was used.\n\n{resolvedPath}");
    }

    private static bool TryResolveTexturePath(string texture, ContentPackage sourcePackage, string sourceXmlPath, out string resolvedPath)
    {
        resolvedPath = null;
        if (string.IsNullOrWhiteSpace(texture)) { return false; }

        try
        {
            if (!texture.Contains("/") && !texture.Contains("%ModDir", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(texture))
            {
                string baseDirectory = !string.IsNullOrWhiteSpace(sourceXmlPath) ? Path.GetDirectoryName(sourceXmlPath) : sourcePackage?.Dir;
                if (string.IsNullOrWhiteSpace(baseDirectory)) { return false; }
                resolvedPath = Path.GetFullPath(Path.Combine(baseDirectory, texture)).CleanUpPathCrossPlatform(correctFilenameCase: false);
                return true;
            }

            ContentPath contentPath = ContentPath.FromRaw(sourcePackage, texture);
            if (string.IsNullOrWhiteSpace(contentPath.FullPath)) { return false; }
            resolvedPath = contentPath.FullPath.CleanUpPathCrossPlatform(correctFilenameCase: false);
            return true;
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError($"CharacterViewer failed to resolve copied sprite texture path \"{texture}\": {ex}");
            return false;
        }
    }

    private static bool TryConvertToModDirPath(string fullPath, ContentPackage targetPackage, out string modDirPath)
    {
        modDirPath = null;
        if (string.IsNullOrWhiteSpace(fullPath)) { return false; }

        string normalizedFullPath = Path.GetFullPath(fullPath).CleanUpPathCrossPlatform(correctFilenameCase: false);
        ContentPackage package = ContentPackageManager.AllPackages
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Dir))
            .OrderByDescending(p => p.Dir.Length)
            .FirstOrDefault(p => IsPathInsideDirectory(normalizedFullPath, p.Dir));
        if (package == null) { return false; }

        string packageDir = Path.GetFullPath(package.Dir).CleanUpPathCrossPlatform(correctFilenameCase: false).TrimEnd('/', '\\');
        string relative = Path.GetRelativePath(packageDir, normalizedFullPath).CleanUpPathCrossPlatform(correctFilenameCase: false).Replace("\\", "/");
        string token = package == targetPackage ? ContentPath.ModDirStr : string.Format(ContentPath.OtherModDirFmt, package.Name);
        modDirPath = $"{token}/{relative}";
        return true;
    }

    private static bool IsPathInsideDirectory(string fullPath, string directory)
    {
        string normalizedDirectory = Path.GetFullPath(directory).CleanUpPathCrossPlatform(correctFilenameCase: false).TrimEnd('/', '\\') + "/";
        return fullPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetSelectedWearableXmlPath(ContentXElement spriteElement, out string path)
    {
        return TryGetWearableXmlPath(spriteElement, out path, showWarning: true);
    }

    private bool TryGetWearableXmlPath(ContentXElement spriteElement, out string path, bool showWarning)
    {
        string prefabPath = selectedClothingPrefab?.FilePath?.FullPath;
        string sourcePath = null;
        if (!string.IsNullOrWhiteSpace(spriteElement?.BaseUri))
        {
            TryGetPathFromBaseUri(spriteElement.BaseUri, out sourcePath);
        }

        path = ShouldPreferSelectedPrefabPath(spriteElement, prefabPath, sourcePath) ? prefabPath : sourcePath ?? prefabPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            if (showWarning)
            {
                new GUIMessageBox("Wearable Editor", "No wearable XML file is selected.");
            }
            return false;
        }
        return true;
    }

    private bool ShouldPreferSelectedPrefabPath(ContentXElement spriteElement, string prefabPath, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath)) { return false; }
        if (string.IsNullOrWhiteSpace(sourcePath)) { return true; }
        if (AreSamePath(prefabPath, sourcePath)) { return true; }
        if (!IsPathInLocalMods(prefabPath) || IsPathInLocalMods(sourcePath)) { return false; }
        if (spriteElement?.ContentPackage != null && spriteElement.ContentPackage == selectedClothingPrefab?.ContentPackage) { return true; }

        XElement prefabElement = selectedClothingPrefab?.ConfigElement?.Element;
        XElement element = spriteElement?.Element;
        if (prefabElement == null || element == null) { return false; }

        return element == prefabElement || element.Ancestors().Contains(prefabElement);
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
}
