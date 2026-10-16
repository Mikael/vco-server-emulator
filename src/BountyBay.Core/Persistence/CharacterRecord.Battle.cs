namespace BountyBay.Core.Persistence;

public sealed class BattleEntryCount
{
    public string Day { get; set; } = "";
    public string Week { get; set; } = "";
    public int Today { get; set; }
    public int ThisWeek { get; set; }
}

public sealed class MatchRecordEntry
{
    public int Type { get; set; }
    public string Date { get; set; } = "";
    public bool Won { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Rating { get; set; }
}

public sealed partial class CharacterRecord
{
    public Dictionary<string, int> BattleHonor { get; set; } = [];
    public Dictionary<int, BattleEntryCount> BattleEntries { get; set; } = [];
    public Dictionary<string, BattleEntryCount> CopyEntries { get; set; } = [];
    public int BattleWins { get; set; }
    public int BattleLosses { get; set; }
    public int ArenaRating { get; set; }
    public List<MatchRecordEntry> MatchRecords { get; set; } = [];
    public int TowerMaxFloor { get; set; }
    public int TowerSeaMaxFloor { get; set; }
    public int BattleReturnScene { get; set; }
    public float[]? BattleReturnPosition { get; set; }
}
