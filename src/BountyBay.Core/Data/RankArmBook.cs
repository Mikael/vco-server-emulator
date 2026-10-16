using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class RankArmItem
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quality { get; set; }
    public int Slot { get; set; }
}

public sealed class RankArmLimit
{
    public string Key { get; set; } = "";
    public int Tier { get; set; }
    public string Prop { get; set; } = "";
    public int Weight { get; set; }
    public int MaxLevel { get; set; }
    public int Base { get; set; }
    public int Max { get; set; }
}

public sealed class RankArmStuff
{
    public int Level { get; set; }
    public int Tier { get; set; }
    public string Prop { get; set; } = "";
    public int Value { get; set; }
    public int Cost { get; set; }
    public int Split { get; set; }
}

public sealed class RankArmBook
{
    private static readonly object Gate = new();
    private static RankArmBook? _current;
    private Dictionary<string, RankArmStuff>? _stuff;

    public string StoneItem { get; set; } = "CWCL0302";
    public int StonesPerTemper { get; set; } = 10;
    public int LuckTempers { get; set; } = 10;
    public int LuckStopTier { get; set; } = 3;
    public int SaveBitSilver { get; set; } = 100000;
    public int InitialSaveBit { get; set; } = 1;
    public string ExchangeItem { get; set; } = "";
    public int ExchangeAmount { get; set; }
    public List<RankArmItem> Items { get; set; } = [];
    public List<RankArmLimit> Limits { get; set; } = [];
    public List<RankArmStuff> Stuff { get; set; } = [];

    public RankArmItem? Item(string key) => Items.FirstOrDefault(item => item.Key == key);

    public RankArmLimit? Limit(string key) => Limits.FirstOrDefault(limit => limit.Key == key);

    public RankArmStuff? StuffAt(int level, string key)
    {
        lock (Gate) _stuff ??= Stuff.GroupBy(entry => $"{entry.Level}-{entry.Tier}-{entry.Prop}").ToDictionary(group => group.Key, group => group.First());
        return _stuff.TryGetValue($"{level}-{key}", out RankArmStuff? found) ? found : null;
    }

    public int ValueOf(string key, int level)
        => StuffAt(level, key)?.Value ?? (Limit(key) is { } limit ? limit.Base + (limit.Max - limit.Base) * level / Math.Max(1, limit.MaxLevel) : 0);

    public int SplitOf(string key, int level) => StuffAt(level, key)?.Split ?? 0;

    public static RankArmBook For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\rankarm.json"));
    }

    private static RankArmBook Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("RankArm", $"game data file is missing: {path}");
            return new RankArmBook();
        }
        try
        {
            RankArmBook book = JsonSerializer.Deserialize<RankArmBook>(File.ReadAllText(path), EmulatorSettings.Json) ?? new RankArmBook();
            Log.Info("RankArm", $"{book.Items.Count} nobility item(s), {book.Limits.Count} tempering attribute(s) loaded");
            return book;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("RankArm", $"Could not read {path}: {exception.Message}");
            return new RankArmBook();
        }
    }
}
