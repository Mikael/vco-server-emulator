namespace BountyBay.Core.Persistence;

public sealed class RankArmAttribute
{
    public string Prop { get; set; } = "";
    public int Level { get; set; }

    public RankArmAttribute Clone() => new() { Prop = Prop, Level = Level };
}

public sealed class RankArmState
{
    public int PropBits { get; set; } = 1;
    public int SaveBits { get; set; } = 1;
    public RankArmAttribute?[] Slots { get; set; } = new RankArmAttribute?[4];
    public RankArmAttribute?[] Saves { get; set; } = new RankArmAttribute?[5];
    public RankArmAttribute? Fresh { get; set; }

    public RankArmState Clone() => new()
    {
        PropBits = PropBits,
        SaveBits = SaveBits,
        Slots = Slots.Select(slot => slot?.Clone()).ToArray(),
        Saves = Saves.Select(save => save?.Clone()).ToArray(),
        Fresh = Fresh?.Clone(),
    };
}

public sealed partial class CharacterRecord
{
    public Dictionary<int, ItemStack> RankWorn { get; set; } = [];
    public int TemperEssence { get; set; }
}
