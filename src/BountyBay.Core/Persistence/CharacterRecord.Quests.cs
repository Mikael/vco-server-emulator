namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public Dictionary<int, List<int>> QuestCounts { get; set; } = [];
    public SortedSet<int> AutoQuestsGiven { get; set; } = [];
    public long WorkExperience { get; set; }
}
