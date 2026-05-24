using Barotrauma;
using Barotrauma.LuaCs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace XPathItemReplace;

public sealed class XPathItemReplacePlugin : IAssemblyPlugin
{
    private static readonly Regex SegmentPattern = new Regex(
        @"^(?<name>[A-Za-z_][A-Za-z0-9_.-]*)(\[@(?<attr>[A-Za-z_][A-Za-z0-9_.-]*)=(?<quote>['""])(?<value>.*?)\k<quote>\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<ItemPrefab> addedPrefabs = new List<ItemPrefab>();

    public void PreInitPatching()
    {
    }

    public void Initialize()
    {
    }

    public void OnLoadCompleted()
    {
        ApplyPatchFiles();
    }

    public void Dispose()
    {
        foreach (ItemPrefab prefab in addedPrefabs)
        {
            ItemPrefab.Prefabs.Remove(prefab);
        }
        addedPrefabs.Clear();
        LuaCsLogger.Log("XPathItemReplace disposed.");
    }

    private void ApplyPatchFiles()
    {
        int fileCount = 0;
        int itemCount = 0;

        foreach (OtherFile patchFile in FindPatchFiles())
        {
            if (!TryLoadPatchDocument(patchFile, out XDocument document)) { continue; }

            fileCount++;
            foreach (XElement itemPatch in document.Root!.Elements().Where(e => NameEquals(e, "item")))
            {
                if (ApplyItemPatch(itemPatch, patchFile))
                {
                    itemCount++;
                }
            }
        }

        if (fileCount > 0)
        {
            ItemPrefab.Prefabs.SortAll();
            LuaCsLogger.Log($"XPathItemReplace applied {itemCount} item patch(es) from {fileCount} file(s).");
        }
    }

    private static IEnumerable<OtherFile> FindPatchFiles()
    {
        return ContentPackageManager.EnabledPackages.All
            .SelectMany(package => package.Files)
            .OfType<OtherFile>()
            .Where(file => file.Path.Value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryLoadPatchDocument(OtherFile patchFile, out XDocument document)
    {
        document = null;
        try
        {
            document = XDocument.Load(patchFile.Path.Value, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError($"XPathItemReplace failed to load \"{patchFile.Path}\": {ex.Message}");
            return false;
        }

        if (document.Root == null || !NameEquals(document.Root, "override"))
        {
            return false;
        }

        if (!document.Root.Elements().Any(e => NameEquals(e, "item") && e.Elements().Any(r => NameEquals(r, "replace"))))
        {
            return false;
        }

        return true;
    }

    private bool ApplyItemPatch(XElement itemPatch, OtherFile patchFile)
    {
        Identifier identifier = GetAttributeValue(itemPatch, "identifier").ToIdentifier();
        if (identifier.IsEmpty)
        {
            LuaCsLogger.LogError($"XPathItemReplace item patch in \"{patchFile.Path}\" has no identifier.");
            return false;
        }

        if (!ItemPrefab.Prefabs.TryGet(identifier, out ItemPrefab sourcePrefab))
        {
            LuaCsLogger.LogError($"XPathItemReplace could not find item prefab \"{identifier}\" for \"{patchFile.Path}\".");
            return false;
        }

        ContentPath runtimePath = ContentPath.FromRaw(patchFile.ContentPackage, sourcePrefab.ContentFile.Path.Value);
        ContentFile runtimeFile = new ItemFile(patchFile.ContentPackage, runtimePath);
        XElement patchedElement = new XElement(sourcePrefab.ConfigElement.Element);
        int replacements = 0;

        foreach (XElement replaceElement in itemPatch.Elements().Where(e => NameEquals(e, "replace")))
        {
            string selector = GetAttributeValue(replaceElement, "sel");
            string value = replaceElement.Value.Trim();
            if (ApplyReplace(patchedElement, selector, value, identifier, runtimeFile))
            {
                replacements++;
            }
        }

        if (replacements == 0)
        {
            LuaCsLogger.Log($"XPathItemReplace skipped \"{identifier}\" because no replacements were applied.");
            return false;
        }

        ContentXElement contentElement = new ContentXElement(runtimeFile.ContentPackage, patchedElement);
        ItemPrefab patchedPrefab = new ItemPrefab(contentElement, (ItemFile)runtimeFile);
        ItemPrefab.Prefabs.Add(patchedPrefab, isOverride: true);
        addedPrefabs.Add(patchedPrefab);
        LuaCsLogger.Log($"XPathItemReplace registered override for \"{identifier}\" with {replacements} replacement operation(s).");
        return true;
    }

    private static bool ApplyReplace(XElement root, string selector, string value, Identifier itemIdentifier, ContentFile file)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            LuaCsLogger.LogError($"XPathItemReplace empty selector in item \"{itemIdentifier}\" from \"{file.Path}\".");
            return false;
        }

        if (!TryResolve(root, selector.Trim(), out ImmutableArray<XObject> matches, out string error))
        {
            LuaCsLogger.LogError($"XPathItemReplace unsupported selector \"{selector}\" in item \"{itemIdentifier}\" from \"{file.Path}\": {error}");
            return false;
        }

        if (matches.Length == 0)
        {
            LuaCsLogger.Log($"XPathItemReplace selector \"{selector}\" matched 0 nodes in item \"{itemIdentifier}\".");
            return false;
        }

        foreach (XObject match in matches)
        {
            switch (match)
            {
                case XAttribute attribute:
                    attribute.Value = value;
                    break;
                case XElement element:
                    element.Value = value;
                    break;
                default:
                    LuaCsLogger.LogError($"XPathItemReplace selector \"{selector}\" matched unsupported XML object {match.GetType().Name}.");
                    return false;
            }
        }

        if (matches.Length > 1)
        {
            LuaCsLogger.Log($"XPathItemReplace selector \"{selector}\" matched {matches.Length} nodes in item \"{itemIdentifier}\".");
        }
        return true;
    }

    private static bool TryResolve(XElement root, string selector, out ImmutableArray<XObject> matches, out string error)
    {
        matches = ImmutableArray<XObject>.Empty;
        error = string.Empty;

        if (selector == ".")
        {
            matches = ImmutableArray<XObject>.Empty.Add(root);
            return true;
        }

        if (!selector.StartsWith("./", StringComparison.Ordinal))
        {
            error = "selector must be \".\" or start with \"./\"";
            return false;
        }

        string path = selector.Substring(2);
        if (path.Length == 0)
        {
            error = "selector path is empty";
            return false;
        }

        string[] segments = path.Split('/');
        IEnumerable<XElement> current = new[] { root };

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (string.IsNullOrWhiteSpace(segment))
            {
                error = "empty path segments are not supported";
                return false;
            }

            if (segment.StartsWith("@", StringComparison.Ordinal))
            {
                if (i != segments.Length - 1)
                {
                    error = "attribute selectors are only supported at the end of the path";
                    return false;
                }

                string attrName = segment.Substring(1);
                if (!IsValidXmlNameToken(attrName))
                {
                    error = $"invalid attribute name \"{attrName}\"";
                    return false;
                }

                List<XObject> attributes = new List<XObject>();
                foreach (XElement element in current)
                {
                    XAttribute attribute = GetAttribute(element, attrName);
                    if (attribute == null && ReferenceEquals(element, root))
                    {
                        attribute = new XAttribute(attrName, string.Empty);
                        element.Add(attribute);
                    }
                    if (attribute != null)
                    {
                        attributes.Add(attribute);
                    }
                }

                matches = attributes.ToImmutableArray();
                return true;
            }

            if (!TryParseElementSegment(segment, out string elementName, out string predicateAttribute, out string predicateValue, out error))
            {
                return false;
            }

            current = current
                .SelectMany(element => element.Elements().Where(child => NameEquals(child, elementName)))
                .Where(element =>
                    predicateAttribute == null ||
                    string.Equals(GetAttribute(element, predicateAttribute)?.Value, predicateValue, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        matches = current.Cast<XObject>().ToImmutableArray();
        return true;
    }

    private static bool TryParseElementSegment(string segment, out string elementName, out string predicateAttribute, out string predicateValue, out string error)
    {
        elementName = null;
        predicateAttribute = null;
        predicateValue = null;
        error = string.Empty;

        Match match = SegmentPattern.Match(segment);
        if (!match.Success)
        {
            error = $"unsupported path segment \"{segment}\"";
            return false;
        }

        elementName = match.Groups["name"].Value;
        if (match.Groups["attr"].Success)
        {
            predicateAttribute = match.Groups["attr"].Value;
            predicateValue = match.Groups["value"].Value;
        }
        return true;
    }

    private static bool IsValidXmlNameToken(string value)
    {
        return !string.IsNullOrEmpty(value) &&
               Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);
    }

    private static string GetAttributeValue(XElement element, string attributeName)
    {
        return GetAttribute(element, attributeName)?.Value ?? string.Empty;
    }

    private static XAttribute GetAttribute(XElement element, string attributeName)
    {
        return element.Attributes().FirstOrDefault(attr => string.Equals(attr.Name.LocalName, attributeName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool NameEquals(XElement element, string name)
    {
        return string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
    }
}
