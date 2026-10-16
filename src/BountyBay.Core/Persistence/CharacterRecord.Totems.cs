namespace BountyBay.Core.Persistence;

public sealed class TotemRecord
{
    public int Id { get; set; }
    public string Item { get; set; } = "";
    public int Type { get; set; } = 1;
    public int Side { get; set; } = 1;
    public int Quality { get; set; } = 1;
    public int Level { get; set; }
    public int Exp { get; set; }
    public int GrowRate { get; set; }
    public bool Identified { get; set; }
    public int[] Inherit { get; set; } = new int[5];
    public int[] Fused { get; set; } = new int[5];
    public int FuseCount { get; set; }
    public string Relative { get; set; } = "";
    public int[] Points { get; set; } = new int[16];
    public int Grids { get; set; }
    public List<TotemSkillEntry> Skills { get; set; } = [];
}

public sealed class TotemSkillEntry
{
    public string Key { get; set; } = "";
    public int Level { get; set; }
    public int Type { get; set; }
    public bool Auto { get; set; }
}

public sealed partial class CharacterRecord
{
    public List<TotemRecord> Totems { get; set; } = [];
    public TotemRecord? TotemWorn { get; set; }
    public int TotemSerial { get; set; }
}
