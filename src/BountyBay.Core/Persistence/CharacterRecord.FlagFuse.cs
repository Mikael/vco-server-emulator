namespace BountyBay.Core.Persistence;

public sealed class FireFlagState
{
    public int Index { get; set; }
    public List<int> Props { get; set; } = [];
    public int Pending { get; set; } = -1;
}

public sealed partial class CharacterRecord
{
    public int FlagHonor { get; set; } = -1;
    public Dictionary<string, FireFlagState> FireFlags { get; set; } = [];
    public Dictionary<string, string> BeliefFusions { get; set; } = [];
}
