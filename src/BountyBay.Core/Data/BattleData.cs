using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class BattleReward
{
    public string Item { get; set; } = "";
    public string Honor { get; set; } = "";
    public int Amount { get; set; }
}

public sealed class BattleLevelDefinition
{
    public string Name { get; set; } = "";
    public int MinLand { get; set; }
    public int MaxLand { get; set; }
    public int MinSea { get; set; }
    public int MaxSea { get; set; }
    public List<BattleReward> Win { get; set; } = [];
    public List<BattleReward> Lose { get; set; } = [];
}

public sealed class BattleSortPrize
{
    public string Score { get; set; } = "";
    public string Item { get; set; } = "";
    public List<int> Amounts { get; set; } = [];
}

public sealed class BattleDefinition
{
    public int Type { get; set; }
    public int Act { get; set; }
    public int ActFlag { get; set; } = -1;
    public string Name { get; set; } = "";
    public int Scene { get; set; }
    public int SceneKind { get; set; }
    public bool Sea { get; set; }
    public string Report { get; set; } = "HonorLand";
    public string Mode { get; set; } = "Score";
    public int LevelBase { get; set; } = 1;
    public int Minutes { get; set; } = 30;
    public int MinPlayers { get; set; } = 2;
    public int MaxPerCamp { get; set; } = 30;
    public bool TeamEntry { get; set; }
    public bool NoTeam { get; set; }
    public int MaxTeam { get; set; }
    public bool ChooseCamp { get; set; }
    public int DayMax { get; set; }
    public int WeekMax { get; set; }
    public int MinSkillTotal { get; set; }
    public int KillPoints { get; set; } = 1;
    public int FlagPoints { get; set; }
    public int ResourcePoints { get; set; }
    public int ResourceTarget { get; set; }
    public int ReliveSeconds { get; set; } = 10;
    public int StartRating { get; set; }
    public int MinDamage { get; set; }
    public List<string> Generals { get; set; } = [];
    public List<float[]> Spawns { get; set; } = [];
    public List<string> FlagKeys { get; set; } = [];
    public List<string> ResourceKeys { get; set; } = [];
    public List<BattleLevelDefinition> Levels { get; set; } = [];
    public List<BattleSortPrize> SortPrizes { get; set; } = [];

    public BattleLevelDefinition? Level(int level)
    {
        int index = level - LevelBase;
        return index >= 0 && index < Levels.Count ? Levels[index] : null;
    }
}

public sealed class CopyDefinition
{
    public int Group { get; set; }
    public int Copy { get; set; }
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Sea { get; set; }
    public int Scene { get; set; }
    public int Level { get; set; }
    public int Points { get; set; }
    public float[]? Spawn { get; set; }
}

public sealed class TowerFloor
{
    public int Floor { get; set; }
    public string Boss { get; set; } = "";
    public List<BattleReward> Prize { get; set; } = [];
}

public sealed class TowerDefinition
{
    public string Name { get; set; } = "";
    public bool Sea { get; set; }
    public int Scene { get; set; }
    public float[]? Spawn { get; set; }
    public int DailyEntries { get; set; }
    public int Minutes { get; set; }
    public List<string> NpcKeys { get; set; } = [];
    public List<TowerFloor> Floors { get; set; } = [];
}

public sealed class BattleDailyTask
{
    public int Battle { get; set; }
    public string CostItem { get; set; } = "";
    public bool CostPoints { get; set; }
    public List<int> Costs { get; set; } = [];
    public List<int> BonusPercent { get; set; } = [];
}

public sealed class BattleBook
{
    private static readonly object Gate = new();
    private static BattleBook? _current;

    public bool EnforceRequirements { get; set; } = true;
    public int ConfirmSeconds { get; set; } = 15;
    public int ReturnSeconds { get; set; } = 60;
    public int QueueWaitUnit { get; set; } = 1;
    public List<BattleDefinition> Battles { get; set; } = [];

    public List<BattleDailyTask> DailyTasks { get; set; } = [];
    public int CopyDailyEntries { get; set; }
    public int CopyMinPlayers { get; set; } = 1;
    public int CopyMaxPlayers { get; set; } = 5;
    public int CopyQueueSlots { get; set; } = 3;
    public List<CopyDefinition> Copies { get; set; } = [];
    public List<TowerDefinition> Towers { get; set; } = [];

    public static BattleBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_current != null) return _current;
            BattleBook book = Read<BattleBook>(settings.Resolve("data\\game\\battles.json")) ?? new BattleBook();
            BattleBook? copies = Read<BattleBook>(settings.Resolve("data\\game\\copies.json"));
            if (copies != null)
            {
                book.Copies = copies.Copies;
                book.Towers = copies.Towers;
                book.CopyDailyEntries = copies.CopyDailyEntries;
                book.CopyMinPlayers = copies.CopyMinPlayers;
                book.CopyMaxPlayers = copies.CopyMaxPlayers;
                book.CopyQueueSlots = copies.CopyQueueSlots;
            }
            Log.Info("Battle", $"{book.Battles.Count} battlefield(s), {book.Copies.Count} instance(s), {book.Towers.Count} tower(s) loaded");
            return _current = book;
        }
    }

    public BattleDefinition? Battle(int type) => Battles.FirstOrDefault(battle => battle.Type == type);

    public BattleDefinition? BattleInScene(int scene) => Battles.FirstOrDefault(battle => battle.Scene == scene);

    public CopyDefinition? Copy(int group, int copy) => Copies.FirstOrDefault(entry => entry.Group == group && entry.Copy == copy);

    public CopyDefinition? CopyInScene(int scene) => Copies.FirstOrDefault(entry => entry.Scene == scene);

    public CopyDefinition? CopyByKey(string key)
        => key.Length == 0 ? null : Copies.FirstOrDefault(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public TowerDefinition? Tower(bool sea) => Towers.FirstOrDefault(tower => tower.Sea == sea);

    public TowerDefinition? TowerInScene(int scene) => Towers.FirstOrDefault(tower => tower.Scene == scene);

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            Log.Warn("Battle", $"game data file is missing: {path}");
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), EmulatorSettings.Json);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Battle", $"Could not read {path}: {exception.Message}");
            return null;
        }
    }
}
