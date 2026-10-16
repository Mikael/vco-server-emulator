using System.Text.Json;
using System.Text.RegularExpressions;
using BountyBay.Core.Configuration;

namespace BountyBay.Core.Data;

public sealed class NpcMenuEntry
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Vars { get; set; } = [];
}

public sealed class NpcMenu
{
    public string Role { get; set; } = "";
    public List<NpcMenuEntry> Entries { get; set; } = [];
}

public sealed class NpcMenuBook
{
    private static readonly object Gate = new();
    private static NpcMenuBook? _loaded;
    private static readonly Regex Variable = new(@"\$\{\d+:([A-Za-z_]+)\}", RegexOptions.Compiled);

    private readonly Dictionary<string, NpcMenu> _menus;

    private NpcMenuBook(Dictionary<string, NpcMenu> menus) => _menus = menus;

    public static NpcMenuBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;
            string path = settings.Resolve("data\\game\\bbo\\npc_menus.json");
            var menus = new Dictionary<string, NpcMenu>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("Menus", out JsonElement list))
                {
                    foreach (JsonProperty menu in list.EnumerateObject())
                        menus[menu.Name] = menu.Value.Deserialize<NpcMenu>(EmulatorSettings.Json) ?? new NpcMenu();
                }
            }
            return _loaded = new NpcMenuBook(menus);
        }
    }

    public NpcMenu? Find(string chatSet) => chatSet.Length > 0 && _menus.TryGetValue(chatSet, out NpcMenu? menu) ? menu : null;

    public static (string Key, Dictionary<string, string> Values)? MatchText(NpcMenu menu, string text)
    {
        foreach (NpcMenuEntry entry in menu.Entries.Where(entry => !entry.Key.StartsWith('-')))
        {
            var names = new List<string>();
            string pattern = "^" + Variable.Replace(Regex.Escape(entry.Text).Replace(@"\$\{", "${").Replace(@"\}", "}"), match =>
            {
                names.Add(match.Groups[1].Value);
                return "(.*?)";
            }) + "$";
            Match found = Regex.Match(text.Trim(), pattern, RegexOptions.Singleline);
            if (!found.Success) continue;
            var values = new Dictionary<string, string>();
            for (int i = 0; i < names.Count; i++) values[names[i]] = found.Groups[i + 1].Value;
            return (entry.Key, values);
        }
        return null;
    }
}
