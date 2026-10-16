using System.Text.Json;
using System.Text.RegularExpressions;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class FireFlagProp
{
    public int Index { get; set; }
    public string Prop { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Float { get; set; }
    public List<int> Base { get; set; } = [];
    public List<int> Step { get; set; } = [];

    public int Value(int level, int steps)
    {
        int at = Math.Clamp(level, 1, Math.Max(1, Base.Count)) - 1;
        int step = at < Step.Count ? Step[at] : 0;
        return (at < Base.Count ? Base[at] : 0) + step * Math.Max(0, steps);
    }
}

public sealed class FlagMaterial
{
    public string Name { get; set; } = "";
    public int Amount { get; set; } = 1;
}

public sealed class FireFlagLevel
{
    public int Level { get; set; }
    public int Props { get; set; } = 1;
    public int MaxIndex { get; set; }
    public int ForgeHonor { get; set; }
    public int ForgeSilver { get; set; }
    public List<FlagMaterial> ForgeMaterials { get; set; } = [];
    public int RemakeHonor { get; set; }
    public int RemakeSilver { get; set; }
    public List<FlagMaterial> RemakeMaterials { get; set; } = [];
}

public sealed class FireFlagRules
{
    public const int LandType = 1;
    public const int SeaType = 2;
    public const int PropsPerType = 7;
    public const int TopLevel = 4;

    private static readonly Regex FlagKey = new(@"^FightFlag([12])0([1-9])$", RegexOptions.Compiled);
    private static readonly object Gate = new();
    private static FireFlagRules? _current;

    public int StartingHonor { get; set; }
    public List<FireFlagProp> Props { get; set; } = [];
    public List<FireFlagLevel> Levels { get; set; } = [];

    public static FireFlagRules For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\flags.json"));
    }

    public static bool TryParse(string key, out int type, out int grade)
    {
        Match match = FlagKey.Match(key);
        type = match.Success ? match.Groups[1].Value[0] - '0' : 0;
        grade = match.Success ? match.Groups[2].Value[0] - '0' : 0;
        return match.Success;
    }

    public static int LevelOf(int grade) => Math.Clamp(grade, 1, TopLevel);

    public static string? Promoted(string key)
        => TryParse(key, out int type, out int grade) && grade < TopLevel ? $"FightFlag{type}0{grade + 1}" : null;

    public static bool Owns(int type, int prop) => prop >= (type - 1) * PropsPerType && prop < type * PropsPerType;

    public FireFlagLevel Level(int level)
        => Levels.FirstOrDefault(entry => entry.Level == level) ?? new FireFlagLevel { Level = level };

    public FireFlagProp? Prop(int index) => Props.FirstOrDefault(prop => prop.Index == index);

    public static int Steps(int flagIndex, int slot, int props)
        => flagIndex > slot ? (flagIndex - slot + Math.Max(1, props) - 1) / Math.Max(1, props) : 0;

    private static FireFlagRules Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("FireFlag", $"game data file is missing: {path}");
            return new FireFlagRules();
        }
        try
        {
            FireFlagRules rules = JsonSerializer.Deserialize<FireFlagRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new FireFlagRules();
            Log.Info("FireFlag", $"{rules.Props.Count} flag attribute(s), {rules.Levels.Count} flag level(s) loaded");
            return rules;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("FireFlag", $"Could not read {path}: {exception.Message}");
            return new FireFlagRules();
        }
    }
}
