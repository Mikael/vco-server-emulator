namespace BountyBay.Core.Persistence;

public sealed class SoulAttributeRecord
{
    public string Name { get; set; } = "";
    public int Roll { get; set; } = 100;
}

public sealed class SoulRecord
{
    public string Id { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int Essence { get; set; }
    public List<SoulAttributeRecord> Attributes { get; set; } = [];
    public List<SoulAttributeRecord>? Pending { get; set; }
}

public sealed class SoulChartRecord
{
    public int Chart { get; set; } = -1;
    public int Grade { get; set; }
    public bool Sea { get; set; }
    public int PendingChart { get; set; } = -1;
    public int PendingGrade { get; set; }
    public SoulRecord?[] Slots { get; set; } = new SoulRecord?[8];
}

public sealed partial class CharacterRecord
{
    public Dictionary<string, SoulChartRecord> Souls { get; set; } = [];
}
