namespace BountyBay.Core.Persistence;

public sealed class AideArmLine
{
    public int Prop { get; set; }
    public int Value { get; set; }

    public AideArmLine Clone() => new() { Prop = Prop, Value = Value };
}

public sealed class AideArmState
{
    public List<AideArmLine> Lines { get; set; } = [];
    public List<int> Active { get; set; } = [];
    public List<AideArmLine>? Pending { get; set; }

    public AideArmState Clone() => new()
    {
        Lines = Lines.Select(line => line.Clone()).ToList(),
        Active = [.. Active],
        Pending = Pending?.Select(line => line.Clone()).ToList(),
    };
}

public sealed partial class CharacterRecord
{
    public string AideRebuildDay { get; set; } = "";
    public int AideFreeRebuildsUsed { get; set; }
}
