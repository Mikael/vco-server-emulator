using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class OnlineRewardItem
{
    public string Name { get; set; } = "";
    public int Amount { get; set; } = 1;
    public string Label { get; set; } = "";
}

public sealed class OnlineRewardStage
{
    public int Stage { get; set; }
    public int Minutes { get; set; }
    public int Money { get; set; }
    public List<OnlineRewardItem> Items { get; set; } = [];
}

public sealed class OnlineRewardRules
{
    public List<OnlineRewardStage> Stages { get; set; } = [];
    public int DailyMoney { get; set; } = 1000;
    public List<OnlineRewardItem> DailyItems { get; set; } = [];
    public int ClaimToleranceSeconds { get; set; } = 3;
}

public static class OnlineRewardTable
{
    private static readonly object Gate = new();
    private static OnlineRewardRules? _rules;

    public static OnlineRewardRules Get(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_rules != null) return _rules;
            var rules = new OnlineRewardRules();
            string path = settings.Resolve("data\\game\\onlinerewards.json");
            try
            {
                if (File.Exists(path))
                    rules = JsonSerializer.Deserialize<OnlineRewardRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? rules;
            }
            catch (JsonException error)
            {
                Log.Warn("rewards", $"{path}: {error.Message}");
            }
            ApplyClientTimes(rules, Path.Combine(settings.ResourcePath, "ini", "OnLineAward.ini"));
            rules.Stages = rules.Stages.Where(stage => stage.Stage > 0 && stage.Minutes > 0).OrderBy(stage => stage.Stage).ToList();
            return _rules = rules;
        }
    }

    private static void ApplyClientTimes(OnlineRewardRules rules, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            foreach (IniSection section in IniFile.Load(path).Named("Award"))
            {
                if (!int.TryParse(section["Stage"], out int stage) || !int.TryParse(section["Times"], out int minutes)) continue;
                OnlineRewardStage? known = rules.Stages.FirstOrDefault(entry => entry.Stage == stage);
                if (known == null) rules.Stages.Add(new OnlineRewardStage { Stage = stage, Minutes = minutes });
                else known.Minutes = minutes;
            }
        }
        catch (Exception error)
        {
            Log.Warn("rewards", $"{path}: {error.Message}");
        }
    }
}
