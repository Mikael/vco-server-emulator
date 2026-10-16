namespace BountyBay.Core.Persistence;

public sealed class ArmSavedSoul
{
    public int Row { get; set; }
    public string Star { get; set; } = "";
    public string First { get; set; } = "";
    public string Second { get; set; } = "";

    public ArmSavedSoul Clone() => new() { Row = Row, Star = Star, First = First, Second = Second };
}

public sealed class ArmUpgradeState
{
    public int Evol { get; set; }
    public int Stars { get; set; }
    public int StarLuck { get; set; }
    public int RankLevel { get; set; }
    public int RankQuality { get; set; }
    public int PropPoints { get; set; }
    public int CharmSkill { get; set; }
    public Dictionary<string, int> Bonus { get; set; } = [];
    public Dictionary<string, int> Refine { get; set; } = [];
    public Dictionary<string, int> Memory { get; set; } = [];
    public Dictionary<int, int> Points { get; set; } = [];
    public Dictionary<string, int> PointValues { get; set; } = [];
    public Dictionary<int, ArmSavedSoul> CharmStars { get; set; } = [];

    public ArmUpgradeState Clone() => new()
    {
        Evol = Evol,
        Stars = Stars,
        StarLuck = StarLuck,
        RankLevel = RankLevel,
        RankQuality = RankQuality,
        PropPoints = PropPoints,
        CharmSkill = CharmSkill,
        Bonus = new Dictionary<string, int>(Bonus),
        Refine = new Dictionary<string, int>(Refine),
        Memory = new Dictionary<string, int>(Memory),
        Points = new Dictionary<int, int>(Points),
        PointValues = new Dictionary<string, int>(PointValues),
        CharmStars = CharmStars.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
    };
}

public sealed partial class CharacterRecord
{
    public Dictionary<int, string> ArmSavedSkills { get; set; } = [];
    public Dictionary<int, ArmSavedSoul> ArmSavedSouls { get; set; } = [];
}
