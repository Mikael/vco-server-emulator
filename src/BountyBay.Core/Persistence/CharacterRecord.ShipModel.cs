namespace BountyBay.Core.Persistence;

public sealed class ShipModelMergeRecord
{
    public string Item { get; set; } = "";
    public int Times { get; set; }
    public int MixType { get; set; }
    public string MixName { get; set; } = "";
    public Dictionary<string, int> Bonus { get; set; } = [];
}

public sealed partial class CharacterRecord
{
    public Dictionary<string, ShipModelMergeRecord> ShipModelMerges { get; set; } = [];
    public string ShipModelCover { get; set; } = "";
    public int ShowShipModel { get; set; }
    public Dictionary<int, ItemStack> ShipModelInnerParts { get; set; } = [];
}
