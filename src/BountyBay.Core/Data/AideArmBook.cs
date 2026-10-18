using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class AideArmRules
{
    public int FreeRebuildsPerDay { get; set; } = 3;
    public List<string> Materials { get; set; } = [];
    public int Lines { get; set; } = 3;
}

public sealed class AideArmBook
{
    private static readonly object Gate = new();
    private static AideArmBook? _current;

    public AideArmRules Rules { get; private init; } = new();
    public List<string> Props { get; } = [];
    private readonly Dictionary<int, Dictionary<int, int[]>> _ranges = [];
    private readonly Dictionary<int, Dictionary<int, int[]>> _costs = [];

    public static AideArmBook For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Load(settings);
    }

    public static AideArmBook Shared
    {
        get
        {
            lock (Gate) return _current ??= Load(EmulatorSettings.Load());
        }
    }

    public int RollValue(int quality, int prop)
    {
        int[] range = Range(quality, prop);
        if (range.Length == 0) return 0;
        int low = range[0], high = range[^1];
        return high <= low ? low : Random.Shared.Next(low, high + 1);
    }

    public static bool IsGeneralEquipment(string item) => GeneralNumber(item) > 0;

    public static int QualityOf(string item) => GeneralNumber(item) is > 0 and var number ? Math.Min(3, (number - 1) / 5 + 1) : 0;

    private static int GeneralNumber(string item)
        => item.Length == 8 && item.StartsWith("FG", StringComparison.Ordinal) && int.TryParse(item.AsSpan(2), out int number) ? number : 0;

    public int[] Range(int quality, int prop)
        => _ranges.TryGetValue(quality - 1, out Dictionary<int, int[]>? rows) && rows.TryGetValue(prop, out int[]? range) ? range : [];

    public int Cost(int mode, int quality, int locked)
        => _costs.TryGetValue(mode, out Dictionary<int, int[]>? rows) && rows.TryGetValue(quality - 1, out int[]? costs) && costs.Length > 0
            ? costs[Math.Clamp(locked, 0, costs.Length - 1)] : 0;

    public string Material(int mode) => mode >= 0 && mode < Rules.Materials.Count ? Rules.Materials[mode] : "";

    private static AideArmBook Load(EmulatorSettings settings)
    {
        AideArmRules rules = new();
        string rulesPath = settings.Resolve("data\\game\\aidearm.json");
        try
        {
            if (File.Exists(rulesPath)) rules = JsonSerializer.Deserialize<AideArmRules>(File.ReadAllText(rulesPath), EmulatorSettings.Json) ?? rules;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("AideArm", $"Could not read {rulesPath}: {exception.Message}");
        }
        var book = new AideArmBook { Rules = rules };
        try
        {
            IniFile rebuild = IniFile.Load(Path.Combine(settings.ResourcePath, "ini", "FGRebuild.ini"));
            if (rebuild.Sections.FirstOrDefault(section => section.Name == "FGRefineProp") is { } props)
            {
                foreach ((string key, string? value) in props.Entries)
                {
                    if (!int.TryParse(key, out int index) || value == null) continue;
                    while (book.Props.Count <= index) book.Props.Add("");
                    book.Props[index] = value.Split(';')[0].Trim();
                }
            }
            for (int quality = 0; quality < 3; quality++)
            {
                if (rebuild.Sections.FirstOrDefault(section => section.Name == $"Range{quality}") is { } range)
                    book._ranges[quality] = Numbers(range, ',');
            }
            IniFile names = IniFile.Load(Path.Combine(settings.ResourcePath, "ini", "window", "numtostring.ini"));
            for (int mode = 0; mode < 2; mode++)
            {
                if (names.Sections.FirstOrDefault(section => section.Name == $"FGRebuildNum_{mode + 1}") is { } cost)
                    book._costs[mode] = Numbers(cost, ' ');
            }
        }
        catch (IOException exception)
        {
            Log.Error("AideArm", $"Could not read the client's General equipment data: {exception.Message}");
        }
        Log.Info("AideArm", $"{book.Props.Count} General equipment attribute(s) loaded");
        return book;
    }

    private static Dictionary<int, int[]> Numbers(IniSection section, char separator)
    {
        var rows = new Dictionary<int, int[]>();
        foreach ((string key, string? value) in section.Entries)
        {
            if (!int.TryParse(key.Trim(), out int index) || value == null) continue;
            rows[index] = value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.TryParse(part.TrimEnd(';'), out int number) ? number : 0).ToArray();
        }
        return rows;
    }
}
