using Barotrauma;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace InGameCharacterCustomizer;

internal enum CustomizePermissionMode : byte
{
    CurrentCrew = 0,
    AnyCrew = 1,
    None = 2
}

internal sealed class CustomizePermissionStore
{
    private const string PermissionFilePath = "Data/InGameCharacterCustomizerPermissions.xml";

    private sealed class Entry
    {
        public string Name;
        public string AccountId;
        public string Address;
        public CustomizePermissionMode Mode;
    }

    private readonly List<Entry> entries = new();

    public void Load()
    {
        entries.Clear();
        if (!File.Exists(PermissionFilePath)) { return; }

        try
        {
            XDocument document = XDocument.Load(PermissionFilePath);
            foreach (XElement element in document.Root?.Elements("Client") ?? Enumerable.Empty<XElement>())
            {
                string accountId = element.Attribute("accountid")?.Value;
                string address = element.Attribute("address")?.Value;
                if (string.IsNullOrWhiteSpace(accountId) && string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                if (!Enum.TryParse(element.Attribute("mode")?.Value, ignoreCase: true, out CustomizePermissionMode mode) ||
                    !Enum.IsDefined(typeof(CustomizePermissionMode), mode))
                {
                    LuaCsLogger.LogError($"Ignoring invalid customize permission in {PermissionFilePath}.");
                    continue;
                }

                entries.Add(new Entry
                {
                    Name = element.Attribute("name")?.Value ?? string.Empty,
                    AccountId = accountId,
                    Address = address,
                    Mode = mode
                });
            }
        }
        catch (Exception e)
        {
            LuaCsLogger.LogError($"Failed to load {PermissionFilePath}: {e.Message}");
        }
    }

    public CustomizePermissionMode GetMode(Client client)
    {
        return Find(client)?.Mode ?? CustomizePermissionMode.CurrentCrew;
    }

    public void SetMode(Client client, CustomizePermissionMode mode)
    {
        if (client == null) { return; }

        Entry entry = Find(client);
        if (entry == null)
        {
            entry = new Entry();
            entries.Add(entry);
        }

        entry.Name = client.Name ?? string.Empty;
        entry.AccountId = GetAccountId(client);
        entry.Address = GetAddress(client);
        entry.Mode = mode;
        Save();
    }

    private Entry Find(Client client)
    {
        if (client == null) { return null; }

        string accountId = GetAccountId(client);
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            Entry accountEntry = entries.FirstOrDefault(entry =>
                string.Equals(entry.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
            if (accountEntry != null) { return accountEntry; }
        }

        string address = GetAddress(client);
        return string.IsNullOrWhiteSpace(address)
            ? null
            : entries.FirstOrDefault(entry =>
                string.Equals(entry.Address, address, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetAccountId(Client client)
    {
        if (client == null || !client.AccountId.TryUnwrap(out AccountId accountId))
        {
            return string.Empty;
        }

        return accountId.StringRepresentation;
    }

    private static string GetAddress(Client client)
    {
        return client?.Connection?.Endpoint?.Address?.StringRepresentation ?? string.Empty;
    }

    private void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(PermissionFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            XDocument document = new XDocument(new XElement("CustomizePermissions"));
            foreach (Entry entry in entries)
            {
                var clientElement = new XElement("Client",
                    new XAttribute("name", entry.Name ?? string.Empty),
                    new XAttribute("mode", entry.Mode));

                if (!string.IsNullOrWhiteSpace(entry.AccountId))
                {
                    clientElement.SetAttributeValue("accountid", entry.AccountId);
                }
                if (!string.IsNullOrWhiteSpace(entry.Address))
                {
                    clientElement.SetAttributeValue("address", entry.Address);
                }
                document.Root.Add(clientElement);
            }

            document.Save(PermissionFilePath);
        }
        catch (Exception e)
        {
            LuaCsLogger.LogError($"Failed to save {PermissionFilePath}: {e.Message}");
        }
    }
}
