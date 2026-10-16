using System.Text;
using System.Text.Json;
using BountyBay.Core.Configuration;

namespace BountyBay.Core.Data;

public static class SceneNames
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> CityAliases = new(StringComparer.OrdinalIgnoreCase) { ["Alexander"] = "Alexandria" };
    private static Dictionary<int, string>? _cityKeys;

    public static string CityKey(EmulatorSettings settings, int scene)
    {
        lock (Gate)
        {
            _cityKeys ??= Load(settings);
            return _cityKeys.GetValueOrDefault(scene / 1000, "");
        }
    }

    private static readonly Dictionary<int, string> MapNames = [];

    public static string MapNameKey(EmulatorSettings settings, int scene)
    {
        lock (Gate)
        {
            if (MapNames.TryGetValue(scene, out string? cached)) return cached;
            string path = Path.Combine(settings.ResourcePath, "ini", "window", "map", scene + ".ini");
            string name = File.Exists(path) ? IniFile.Load(path).Header["name"] ?? "" : "";
            MapNames[scene] = name;
            return name;
        }
    }

    private static Dictionary<int, string> Load(EmulatorSettings settings)
    {
        var keys = new Dictionary<int, string>();
        string markers = settings.Resolve("data\\game\\bbo\\world_markers.json");
        string names = Path.Combine(settings.ResourcePath, "UI", "lang", "china", "mapped", "namelist.idres");
        if (!File.Exists(markers) || !File.Exists(names)) return keys;

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        foreach (string raw in File.ReadLines(names, Encoding.UTF8))
        {
            string line = raw.Trim().Replace('\u0003', ' ');
            if (line.StartsWith('[') && line.EndsWith(']')) section = line[1..^1];
            else if (section != null && section.StartsWith("IDName", StringComparison.Ordinal) && line.StartsWith("name=", StringComparison.Ordinal))
                byName.TryAdd(line[5..].Trim(), section);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(markers));
        if (!document.RootElement.TryGetProperty("Cities", out JsonElement cities)) return keys;
        foreach (JsonProperty city in cities.EnumerateObject())
        {
            string name = city.Value.GetString() ?? "";
            if (CityAliases.TryGetValue(name, out string? alias)) name = alias;
            if (int.TryParse(city.Name, out int id) && byName.TryGetValue(name, out string? key)) keys[id] = key;
        }
        return keys;
    }
}
