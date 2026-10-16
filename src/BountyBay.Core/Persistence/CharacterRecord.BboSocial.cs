namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public string Master { get; set; } = "";
    public SortedSet<string> Apprentices { get; set; } = [];
    public int GraduatedApprentices { get; set; }
    public bool RecruitingApprentices { get; set; }
    public List<int> TrackedQuests { get; set; } = [];
    public bool AutoTrackQuests { get; set; } = true;
    public int NoviceStep { get; set; } = -1;
}
