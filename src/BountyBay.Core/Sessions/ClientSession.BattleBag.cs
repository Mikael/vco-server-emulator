using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int BattleBagView = 90;
    private const int BattleBagSlots = 24;
    private const float BattleBagLoadMax = 100f;

    private bool _battleBagOpen;

    public void OpenBattleBag()
    {
        if (!_battleBagOpen)
        {
            _battleBagOpen = true;
            int capacity = Math.Max(BattleBagSlots, Character.BattleBag.Count == 0 ? 0 : Character.BattleBag.Keys.Max());
            Send(ViewPackets.Create(BattleBagView, capacity), "(battle bag)");
            foreach ((int slot, ItemStack item) in Character.BattleBag.OrderBy(pair => pair.Key))
                Send(ViewPackets.AddItem(BattleBagView, slot, ItemProperties(item, slot)), $"(battle bag item {item.Name})");
        }
        SendBattleBagLoad();
        Log.Info(Tag, $"{Name} opens the battle bag ({Character.BattleBag.Count} stacks)");
    }

    public void CloseBattleBag()
    {
        if (!_battleBagOpen) return;
        _battleBagOpen = false;
        Send(ViewPackets.Delete(BattleBagView), "(close battle bag)");
        if (_warehouseOpen) SendWarehouseLoad();
    }

    public bool BattleBagSelect(int how, int index)
    {
        if (!_battleBagOpen) return false;
        if (Hand != null && HandSource() is { } from && from.View != BattleBagView)
        {
            int amount = Hand.Amount;
            DropHand();
            BattleBagStore(from.View, from.Index, amount);
            return true;
        }
        if (Hand != null) DropHand();
        if (how is 0 or 2) BattleBagTake(index, int.MaxValue);
        return true;
    }

    private float BattleBagLoad => Character.BattleBag.Values.Sum(Weight);

    private void BattleBagStore(int view, int index, int amount)
    {
        if (MailBagOf(view) is not { } bag || !bag.TryGetValue(index, out ItemStack? item)) return;
        amount = Math.Clamp(amount, 1, item.Amount);
        int unit = ItemCatalog.Number(item.Name, "Weight");
        float room = BattleBagLoadMax - BattleBagLoad;
        if (unit > 0 && unit * amount > room) amount = (int)(room / unit);
        if (amount <= 0)
        {
            SystemMessage($"Your battle bag cannot hold more than {BattleBagLoadMax:F0} loads.", SystemMessageKind.Prompt);
            return;
        }
        int slot = Character.BattleBag.FirstOrDefault(pair => pair.Value.Name == item.Name && Stackable(item.Name)).Key;
        if (slot == 0) slot = FreeSlot(Character.BattleBag, BattleBagSlots);
        if (slot == 0)
        {
            SystemMessage("Your battle bag is full.", SystemMessageKind.Prompt);
            return;
        }
        if (Character.BattleBag.TryGetValue(slot, out ItemStack? stored))
        {
            stored.Amount += amount;
            Send(ViewPackets.UpdateItem(BattleBagView, slot, new PropertyBag { ["Amount"] = stored.Amount }), "(battle bag item)");
        }
        else
        {
            ItemStack moved = item.Clone();
            moved.Amount = amount;
            Character.BattleBag[slot] = moved;
            Send(ViewPackets.AddItem(BattleBagView, slot, ItemProperties(moved, slot)), $"(battle bag item {moved.Name})");
        }
        MailTake(view, index, amount, "(put in battle bag)");
        SendBattleBagLoad();
        Save();
        Log.Info(Tag, $"{Name} puts {amount} x {item.Name} in the battle bag");
    }

    private void BattleBagTake(int index, int amount)
    {
        if (!Character.BattleBag.TryGetValue(index, out ItemStack? stored)) return;
        amount = Math.Clamp(amount, 1, stored.Amount);
        ItemStack taken = stored.Clone();
        taken.Amount = amount;
        if (!GiveItem(taken, "(taken from battle bag)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        stored.Amount -= amount;
        if (stored.Amount > 0)
        {
            Send(ViewPackets.UpdateItem(BattleBagView, index, new PropertyBag { ["Amount"] = stored.Amount }), "(battle bag item)");
        }
        else
        {
            Character.BattleBag.Remove(index);
            Send(ViewPackets.RemoveItem(BattleBagView, index), "(battle bag item)");
        }
        SendBattleBagLoad();
        Save();
        Log.Info(Tag, $"{Name} takes {amount} x {stored.Name} from the battle bag");
    }

    private void SendBattleBagLoad()
    {
        SetKnownProperty("BoxLoad", BattleBagLoad);
        SetKnownProperty("BoxMaxLoad", BattleBagLoadMax);
    }
}
