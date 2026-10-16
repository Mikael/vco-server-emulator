using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private int ContrabandPercent => Math.Clamp(40 + 10 * Character.ContrabandLevel, 50, 100);

    public void SellContraband(int view, int index)
    {
        if (view != LootView || !_lootOpen || !_loot.TryGetValue(index, out ItemStack? item))
        {
            SystemMessage("There is nothing there to dispose of.", SystemMessageKind.Prompt);
            return;
        }
        int price = Math.Max(1, Context.Data.PriceOf(item.Name) * item.Amount * ContrabandPercent / 100);
        _loot.Remove(index);
        Send(ViewPackets.RemoveItem(LootView, index), "(spoil disposed of)");
        if (_loot.Count == 0) CloseLoot();
        SetMoney(Money + price);
        Character.ContrabandSold += item.Amount;
        Save();
        SystemMessage($"You disposed of {item.Amount} stolen article(s) for {price} silver coins.");
        Log.Info(Tag, $"{Name} disposes of {item.Amount} x {item.Name} for {price}");
    }
}
