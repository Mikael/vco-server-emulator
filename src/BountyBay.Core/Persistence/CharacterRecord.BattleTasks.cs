namespace BountyBay.Core.Persistence;

public sealed class BattleTaskRecord
{
    public string Day { get; set; } = "";
    public int Tier { get; set; }
    public bool Done { get; set; }
}

public sealed partial class CharacterRecord
{
    public Dictionary<int, BattleTaskRecord> BattleTasks { get; set; } = [];
}
