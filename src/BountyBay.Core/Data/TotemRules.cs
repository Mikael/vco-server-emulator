using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class TotemPropDefinition
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public List<int> Values { get; set; } = [];

    public int ValueAt(int points) => Values.Count == 0 ? 0 : Values[Math.Clamp(points, 0, Values.Count - 1)];
}

public sealed class TotemSkillDefinition
{
    public string Book { get; set; } = "";
    public string Key { get; set; } = "";
    public int Level { get; set; }
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public string Stat { get; set; } = "";
    public int Attribute { get; set; } = -1;
    public int Value { get; set; }
}

public sealed class TotemRules
{
    private static readonly object Gate = new();
    private static TotemRules? _current;

    public List<string> Items { get; set; } = [];
    public List<int> MaxPoint { get; set; } = [];
    public List<int> LevelCaps { get; set; } = [];
    public List<int> LevelExp { get; set; } = [];
    public List<int[]> GrowthRanges { get; set; } = [];
    public int[] MainInherit { get; set; } = [1500, 3000];
    public int[] OtherInherit { get; set; } = [1000, 2500];
    public int ChargeMax { get; set; } = 90;
    public List<int> ChargeStageStarts { get; set; } = [];
    public List<int> ChargePercents { get; set; } = [];
    public List<int> SkillGridPercents { get; set; } = [];
    public int MaxSkills { get; set; } = 5;
    public int AutoSkillMax { get; set; } = 2;
    public int AutoSkillPercent { get; set; }
    public int ActiveReadySeconds { get; set; } = 30;
    public int ActiveCooldownSeconds { get; set; } = 180;
    public string ExpItem { get; set; } = "";
    public int ExpPerItem { get; set; } = 1;
    public string PotentialItem { get; set; } = "";
    public string RemodelItem { get; set; } = "";
    public int IdentifySilver { get; set; }
    public string AdvanceItem { get; set; } = "";
    public List<int> AdvanceItemAmounts { get; set; } = [];
    public List<int> AdvanceSilver { get; set; } = [];
    public string DismantleItem { get; set; } = "";
    public int DismantlePerQuality { get; set; }
    public string RedeemItem { get; set; } = "";
    public int RedeemAmount { get; set; }
    public int AttributeBasePercent { get; set; } = 10;
    public int AttributeGrowthDivisor { get; set; } = 1000000;
    public List<TotemPropDefinition> Props { get; set; } = [];
    public List<TotemSkillDefinition> Skills { get; set; } = [];

    public int TypeOf(string item) => Items.IndexOf(item) + 1;

    public int Cap(int quality) => LevelCaps.Count == 0 ? 0 : LevelCaps[Math.Clamp(quality, 1, LevelCaps.Count) - 1];

    public int MaxPointsPerProp(int quality) => MaxPoint.Count == 0 ? 0 : MaxPoint[Math.Clamp(quality, 1, MaxPoint.Count) - 1];

    public int ExpFor(int level) => LevelExp.Count == 0 ? 0 : LevelExp[Math.Clamp(level, 0, LevelExp.Count - 1)];

    public int Stage(int charge) => ChargeStageStarts.Count(start => charge >= start);

    public TotemSkillDefinition? Book(string item) => Skills.FirstOrDefault(skill => skill.Book.Equals(item, StringComparison.OrdinalIgnoreCase));

    public TotemSkillDefinition? Skill(string key, int level) => Skills.FirstOrDefault(skill => skill.Key == key && skill.Level == level);

    public static TotemRules For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\totems.json"));
    }

    private static TotemRules Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("Totems", $"game data file is missing: {path}");
            return new TotemRules();
        }
        try
        {
            TotemRules rules = JsonSerializer.Deserialize<TotemRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new TotemRules();
            Log.Info("Totems", $"{rules.Items.Count} imprint(s), {rules.Props.Count} potential attribute(s), {rules.Skills.Count} skill book(s) loaded");
            return rules;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Totems", $"Could not read {path}: {exception.Message}");
            return new TotemRules();
        }
    }
}
