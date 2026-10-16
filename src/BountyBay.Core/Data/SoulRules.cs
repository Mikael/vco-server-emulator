using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class SoulRules
{
    private static readonly object Gate = new();
    private static SoulRules? _current;

    public int MaxLevel { get; set; } = 50;
    public string ChartItem { get; set; } = "CWCL0273";
    public string EssenceItem { get; set; } = "CWCL0274";
    public string ShardItem { get; set; } = "CWCL0271";
    public string SpoilsItem { get; set; } = "CWCL0272";
    public int ChartFeeGuess { get; set; } = 100000;
    public List<int> ChartGradeWeightsGuess { get; set; } = [60, 25, 12, 3];
    public int ExpPerEssenceGuess { get; set; } = 10;
    public int ExpBaseGuess { get; set; } = 20;
    public int ExpPerLevelGuess { get; set; } = 10;
    public int UpgradeFeePerLevelGuess { get; set; } = 1000;
    public int MoneyRow { get; set; } = 1;
    public int FuseShards { get; set; } = 8;
    public int FuseSpoils { get; set; } = 20;
    public int FuseFee { get; set; } = 5000000;
    public List<int> AdvanceShards { get; set; } = [16, 32, 48];
    public List<int> AdvanceSpoils { get; set; } = [40, 80, 100];
    public List<int> AdvanceFee { get; set; } = [25000000, 50000000, 100000000];
    public List<int> RebuildSpoilsGuess { get; set; } = [2, 4, 6, 8];
    public List<int> RebuildFeeGuess { get; set; } = [10000, 20000, 40000, 80000];
    public int DownEssencePercentGuess { get; set; } = 50;
    public List<int> DownShardsGuess { get; set; } = [4, 8, 16, 24];
    public int AttributesPerSoulGuess { get; set; } = 2;
    public List<int> RollPercentGuess { get; set; } = [80, 120];
    public List<string> FloatAttributes { get; set; } = [];
    public List<List<string>> LandAttributes { get; set; } = [];
    public List<List<string>> SeaAttributes { get; set; } = [];
    public Dictionary<string, List<int>> Tiers { get; set; } = [];
    public List<List<string>> LandCharts { get; set; } = [];
    public List<List<string>> SeaCharts { get; set; } = [];
    public Dictionary<string, string> Names { get; set; } = [];

    public const int Qualities = 4;
    public const int Elements = 8;

    public static SoulRules For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\souls.json"));
    }

    public static bool IsSoul(string id)
        => id.Length == 5 && (id.StartsWith("FS", StringComparison.Ordinal) || id.StartsWith("SS", StringComparison.Ordinal))
            && int.TryParse(id.AsSpan(2), out int number) && number >= 1 && number <= Qualities * Elements;

    public static bool IsSea(string id) => id.StartsWith("SS", StringComparison.Ordinal);

    public static int Number(string id) => int.TryParse(id.AsSpan(2), out int number) ? number : 0;

    public static int Quality(string id) => (Number(id) - 1) / Elements;

    public static int Element(string id) => (Number(id) - 1) % Elements;

    public static string SoulId(bool sea, int quality, int element) => $"{(sea ? "SS" : "FS")}{quality * Elements + element + 1:D3}";

    public string NameOf(string id) => Names.TryGetValue(id, out string? name) ? name : id;

    public int Charts(bool sea) => sea ? SeaCharts.Count : LandCharts.Count;

    public string SkillName(bool sea, int chart, int part)
    {
        List<List<string>> charts = sea ? SeaCharts : LandCharts;
        return chart >= 0 && chart < charts.Count && part >= 0 && part < charts[chart].Count ? charts[chart][part] : "";
    }

    public IReadOnlyList<string> Pool(string id)
    {
        List<List<string>> pools = IsSea(id) ? SeaAttributes : LandAttributes;
        int element = Element(id);
        return element < pools.Count ? pools[element] : [];
    }

    public int ExpToNext(int level) => ExpBaseGuess + ExpPerLevelGuess * level;

    public int Value(string attribute, int level, int quality, int roll)
    {
        int top = Tiers.TryGetValue(attribute, out List<int>? tiers) && tiers.Count >= 3 ? tiers[2] : 10;
        long value = (long)top * Math.Clamp(level, 1, MaxLevel) * (quality + 2) * roll / ((long)MaxLevel * (Qualities + 1) * 100);
        return (int)Math.Max(1, value);
    }

    public int Tier(string attribute, int value)
    {
        if (!Tiers.TryGetValue(attribute, out List<int>? tiers)) return 0;
        return Math.Min(Qualities - 1, tiers.Take(3).Count(threshold => value >= threshold));
    }

    public bool IsFloat(string attribute) => FloatAttributes.Contains(attribute);

    public static int At(List<int> values, int index) => values.Count == 0 ? 0 : values[Math.Clamp(index, 0, values.Count - 1)];

    private static SoulRules Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("Souls", $"game data file is missing: {path}");
            return new SoulRules();
        }
        try
        {
            SoulRules rules = JsonSerializer.Deserialize<SoulRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new SoulRules();
            Log.Info("Souls", $"{rules.Names.Count} star soul(s), {rules.LandCharts.Count} land and {rules.SeaCharts.Count} sea star chart(s) loaded");
            return rules;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Souls", $"Could not read {path}: {exception.Message}");
            return new SoulRules();
        }
    }
}
