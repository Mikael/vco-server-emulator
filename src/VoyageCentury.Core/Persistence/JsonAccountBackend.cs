using System.Text.Json;
using System.Text.Json.Nodes;
using VoyageCentury.Core.Configuration;
using VoyageCentury.Core.Logging;

namespace VoyageCentury.Core.Persistence;

public sealed class JsonAccountBackend : IAccountBackend
{
    private readonly string _path;
    private readonly string? _legacyPath;

    public JsonAccountBackend(string path, string? legacyPath = null)
    {
        _path = path;
        _legacyPath = legacyPath;
    }

    public Dictionary<string, AccountRecord> Load()
    {
        if (File.Exists(_path))
        {
            return JsonSerializer.Deserialize<Dictionary<string, AccountRecord>>(File.ReadAllText(_path), EmulatorSettings.Json) ?? [];
        }
        if (_legacyPath != null && File.Exists(_legacyPath))
        {
            try
            {
                Dictionary<string, AccountRecord> imported = ImportLegacy(_legacyPath);
                Log.Info("Accounts", $"Imported {imported.Values.Sum(a => a.Characters.Count)} character(s) from {_legacyPath}");
                Save(imported);
                return imported;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or FormatException)
            {
                Log.Warn("Accounts", $"Could not import {_legacyPath}: {exception.Message}");
            }
        }
        return [];
    }

    public void Save(Dictionary<string, AccountRecord> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(accounts, EmulatorSettings.Json));
        File.Move(temporary, _path, overwrite: true);
    }

    private static Dictionary<string, AccountRecord> ImportLegacy(string path)
    {
        var accounts = new Dictionary<string, AccountRecord>();
        JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
        if (root is JsonArray list)
        {
            accounts[AccountRepository.Unclaimed] = new AccountRecord { Characters = list.Select(ImportCharacter).ToList() };
        }
        else if (root is JsonObject byAccount)
        {
            foreach ((string account, JsonNode? characters) in byAccount)
            {
                if (characters is JsonArray array)
                    accounts[account] = new AccountRecord { Characters = array.Select(ImportCharacter).ToList() };
            }
        }
        return accounts;
    }

    private static CharacterRecord ImportCharacter(JsonNode? node)
    {
        JsonObject source = node!.AsObject();
        int Number(string key, int fallback) => source[key] is JsonValue value ? (int)value.GetValue<double>() : fallback;
        var record = new CharacterRecord
        {
            Name = source["name"]?.GetValue<string>() ?? "Player",
            Role = Number("role", 3001001),
            Face = Number("face", 3101001),
            Body = Number("body", 3401001),
            Hair = Number("hair", 0),
            HairColor = Number("hair_color", 0),
            City = Number("city", 0),
            Money = Number("money", -1),
            Bank = Number("bank", 0),
            Scene = Number("scene", 0),
        };
        if (source["pos"] is JsonArray position && position.Count >= 2)
        {
            float y = source["y"] is JsonValue height ? (float)height.GetValue<double>() : 0f;
            record.Position = [(float)position[0]!.GetValue<double>(), y, (float)position[1]!.GetValue<double>()];
        }
        if (source["skills"] is JsonArray skills)
        {
            foreach (JsonNode? skill in skills)
            {
                if (skill is JsonArray pair && pair.Count >= 2)
                    record.Skills.Add(new SkillEntry { Id = (int)pair[0]!.GetValue<double>(), Level = (int)pair[1]!.GetValue<double>() });
            }
        }
        if (source["items"] is JsonObject items) record.Items = ImportSlots(items);
        if (source["bags"] is JsonObject bags)
        {
            record.Bags = [];
            foreach ((string view, JsonNode? slots) in bags)
            {
                if (slots is JsonObject slotObject) record.Bags[int.Parse(view)] = ImportSlots(slotObject);
            }
        }
        if (source["shortcuts"] is JsonObject shortcuts)
        {
            foreach ((string slot, JsonNode? entry) in shortcuts)
            {
                if (entry is JsonArray pair && pair.Count >= 2)
                    record.Shortcuts[int.Parse(slot)] = new ShortcutEntry { Kind = (int)pair[0]!.GetValue<double>(), Name = pair[1]!.GetValue<string>() };
            }
        }
        foreach ((string key, SortedSet<string> target) in new[] { ("friends", record.Friends), ("blacklist", record.Blacklist) })
        {
            if (source[key] is JsonArray names)
            {
                foreach (JsonNode? name in names) target.Add(name!.GetValue<string>());
            }
        }
        return record;
    }

    private static Dictionary<int, ItemStack> ImportSlots(JsonObject slots)
    {
        var result = new Dictionary<int, ItemStack>();
        foreach ((string slot, JsonNode? item) in slots)
        {
            if (item is JsonArray parts && parts.Count >= 3)
            {
                result[int.Parse(slot)] = new ItemStack
                {
                    Name = parts[0]!.GetValue<string>(),
                    Photo = (int)parts[1]!.GetValue<double>(),
                    Amount = (int)parts[2]!.GetValue<double>(),
                };
            }
        }
        return result;
    }
}
