namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public Dictionary<int, int[]> ExplorePoints { get; set; } = [];
}
