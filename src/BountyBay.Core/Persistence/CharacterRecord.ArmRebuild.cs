namespace BountyBay.Core.Persistence;

public sealed class ArmRebuildState
{
    public Dictionary<string, int> Deltas { get; set; } = [];
    public int Sign { get; set; }
    public string Skill { get; set; } = "";
    public int Level { get; set; }
    public string Day { get; set; } = "";
    public int Today { get; set; }

    public bool Empty => Deltas.Count == 0 && Skill.Length == 0 && Sign == 0;

    public ArmRebuildState Clone() => new()
    {
        Deltas = new Dictionary<string, int>(Deltas),
        Sign = Sign,
        Skill = Skill,
        Level = Level,
        Day = Day,
        Today = Today,
    };
}

public sealed class ArmRebuildRecord
{
    public int Index { get; set; }
    public string Item { get; set; } = "";
    public int ArmType { get; set; }
    public Dictionary<string, int> Deltas { get; set; } = [];
    public int Sign { get; set; }
    public string Skill { get; set; } = "";
}

public sealed partial class CharacterRecord
{
    public List<ArmRebuildRecord> ArmRebuildRecords { get; set; } = [];
}
