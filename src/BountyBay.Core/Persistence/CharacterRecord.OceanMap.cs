namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    // The explored cells of the world maps: 64 rows x 2 words (64 columns) per ocean, ocean 0 first (VisitedOcean table).
    public List<uint> VisitedOcean { get; set; } = [];
}
