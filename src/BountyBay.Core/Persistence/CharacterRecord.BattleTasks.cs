namespace BountyBay.Core.Persistence;

public sealed class BattleTaskRecord
{
    public string Day { get; set; } = "";
    public int Tier { get; set; }
    public bool Done { get; set; }
}

public sealed partial class CharacterRecord
{
    // Battle type -> today's daily battlefield quest.
    public Dictionary<int, BattleTaskRecord> BattleTasks { get; set; } = [];
}
