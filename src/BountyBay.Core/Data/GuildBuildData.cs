using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class GuildActivityRules
{
    public List<int> Levels { get; set; } = [16000, 43000, 82000, 132000, 201000, 281000];
    public int DailyLogin { get; set; } = 20;
    public int CopyEntry { get; set; } = 50;
    public int CopyStage { get; set; } = 100;
    public int MatchWin { get; set; } = 500;
    public int MatchPlayed { get; set; } = 100;

    public int LevelOf(long activity) => Levels.Count(threshold => activity >= threshold);
}

public sealed class GuildBuildMedal
{
    public string Item { get; set; } = "";
    public int Degree { get; set; }
    public int Amount { get; set; } = 1;
    public int Contribution { get; set; } = 10;
}

public sealed class GuildConstructionRules
{
    public int MaxLevel { get; set; } = 5;
    public int DegreePerLevel { get; set; } = 100;
    public int ExpPerDegree { get; set; } = 10;
    public List<GuildBuildMedal> Medals { get; set; } = [];
    public int LogPageSize { get; set; } = 20;
    public int LogLimit { get; set; } = 200;
    public GuildTaskRules Tasks { get; set; } = new();

    public int DegreeMax(int level) => DegreePerLevel * Math.Max(1, level);
}

// Guild construction quests (client NewTask 99990000 commerce / 99980000 industry): collect goods for the guild minister.
public sealed class GuildTaskRules
{
    public int Count { get; set; } = 20;
    public int Degree { get; set; } = 5;
    public int Contribution { get; set; } = 20;
    public int Activity { get; set; } = 50;
    public List<string> Commerce { get; set; } = [];
    public List<string> Industry { get; set; } = [];

    public List<string> Goods(int kind) => kind == 0 ? Commerce : Industry;
}

public sealed class GuildArmRules
{
    public string Fragment { get; set; } = "DJ010398";
    public int FragmentsPerMedal { get; set; } = 20;
    public int SplitFragments { get; set; } = 5;
    public List<string> Land { get; set; } = [];
    public List<string> Sea { get; set; } = [];
    public List<int> UpgradeFragments { get; set; } = [20, 40, 60, 80];

    public int MaxLevel => Math.Min(Land.Count, Sea.Count);

    public (int Kind, int Level) Medal(string item)
    {
        int land = Land.IndexOf(item);
        if (land >= 0) return (1, land + 1);
        int sea = Sea.IndexOf(item);
        return sea >= 0 ? (2, sea + 1) : (0, 0);
    }

    public string MedalItem(int kind, int level)
    {
        List<string> list = kind == 1 ? Land : Sea;
        return level >= 1 && level <= list.Count ? list[level - 1] : "";
    }

    public int UpgradeCost(int level) => level >= 1 && level <= UpgradeFragments.Count ? UpgradeFragments[level - 1] : 0;
}

public sealed class GuildCopyRules
{
    public int Scene { get; set; } = 1900098;
    public int Minutes { get; set; } = 120;
    public int MinMembers { get; set; } = 1;
    public int BatchLimit { get; set; } = 5;
    public int Stages { get; set; } = 11;
    public int KillsPerStage { get; set; } = 10;
    public int RunsPerSeason { get; set; } = 1;
}

public sealed class GuildMatchWager
{
    public string Name { get; set; } = "Silver";
    public int Unit { get; set; } = 10000;
}

public sealed class GuildMatchRules
{
    public int Guilds { get; set; } = 8;
    public int QuarterHour { get; set; } = 116;
    public int SemiHour { get; set; } = 140;
    public int FinalHour { get; set; } = 164;
    public List<GuildMatchWager> WagerTypes { get; set; } = [];
    public double MinimumOdds { get; set; } = 1.1;
}

public sealed class GuildBuildData
{
    private static readonly object Gate = new();
    private static GuildBuildData? _loaded;

    public GuildActivityRules Activity { get; set; } = new();
    public GuildConstructionRules Construction { get; set; } = new();
    public GuildArmRules Medals { get; set; } = new();
    public GuildCopyRules Copy { get; set; } = new();
    public GuildMatchRules Match { get; set; } = new();

    public static GuildBuildData Shared => _loaded ?? For(EmulatorSettings.Load());

    public static GuildBuildData For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;
            string path = settings.Resolve("data\\game\\guildbuild.json");
            try
            {
                _loaded = File.Exists(path)
                    ? JsonSerializer.Deserialize<GuildBuildData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new GuildBuildData()
                    : new GuildBuildData();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error("GuildBuild", $"Load failed: {exception.Message}");
                _loaded = new GuildBuildData();
            }
            if (_loaded.Match.WagerTypes.Count == 0) _loaded.Match.WagerTypes.Add(new GuildMatchWager());
            return _loaded;
        }
    }
}
