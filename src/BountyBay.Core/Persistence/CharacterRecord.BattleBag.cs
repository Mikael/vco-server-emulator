namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public Dictionary<int, ItemStack> BattleBag { get; set; } = [];
}
