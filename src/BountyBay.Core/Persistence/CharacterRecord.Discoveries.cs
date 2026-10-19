namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    // Discovered entries per finding table (finding_animal, finding_conch, finding_nature, finding_plant, finding_relic).
    public Dictionary<string, List<int>> Discoveries { get; set; } = [];
}
