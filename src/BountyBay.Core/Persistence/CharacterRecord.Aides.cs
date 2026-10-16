namespace BountyBay.Core.Persistence;

public sealed class AideRecord
{
    public int Slot { get; set; }
    public int Code { get; set; }
    public string Name { get; set; } = "";
    public int Quality { get; set; } = 1;
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int Photo { get; set; }
    public string Skill { get; set; } = "";
    public int[] Stats { get; set; } = new int[4];
    public int Post { get; set; } = -1;
    public Dictionary<int, ItemStack> Equipment { get; set; } = [];
    public Dictionary<int, Dictionary<int, string>> Suits { get; set; } = [];
}

public sealed class AideCandidate
{
    public int Code { get; set; }
    public string Skill { get; set; } = "";
    public int Quality { get; set; } = 1;
    public int Price { get; set; }
    public bool Taken { get; set; }
}

public sealed partial class CharacterRecord
{
    public List<AideRecord> Aides { get; set; } = [];
    public int AideSlots { get; set; }
    public List<AideCandidate> AideCandidates { get; set; } = [];
}
