namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public int ShowNouse { get; set; }
    public Dictionary<int, Dictionary<int, string>> Outfits { get; set; } = [];
    public int WorkType { get; set; }
    public int WorkLevel { get; set; }
    public Dictionary<string, int> WorkPointsSpent { get; set; } = [];
}
