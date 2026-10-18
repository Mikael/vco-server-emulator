namespace BountyBay.Core.Persistence;

// One attribute line of a General (aide) equipment piece: index into FGRebuild.ini [FGRefineProp] and its value.
public sealed class AideArmLine
{
    public int Prop { get; set; }
    public int Value { get; set; }

    public AideArmLine Clone() => new() { Prop = Prop, Value = Value };
}

public sealed class AideArmState
{
    public List<AideArmLine> Lines { get; set; } = [];
    // Line positions (0-2) the player has activated; the others are sent in LockProp.
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
