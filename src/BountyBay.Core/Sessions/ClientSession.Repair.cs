using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int ShipEquipmentViewId = 10;

    private static readonly int[] DurabilityViews = [ViewId.BagEquip, ViewId.Equipped];

    public int StallRepairPrice { get; private set; } = -1;

    public void SetStallRepair(int price) => StallRepairPrice = price > 0 ? price : -1;

    public int DurabilityMax(string itemName) => ItemCatalog.Number(itemName, "DFGMax");

    public int Durability(string itemName)
    {
        int max = DurabilityMax(itemName);
        if (max <= 0) return 0;
        int lost = Character.Wear?.GetValueOrDefault(itemName) ?? 0;
        return Math.Clamp(max - lost, 0, max);
    }

    public void SendDurability()
    {
        if (Character.Wear == null || Character.Wear.Count == 0) return;
        foreach (int view in DurabilityViews)
        {
            if (!Bags.TryGetValue(view, out Dictionary<int, ItemStack>? slots)) continue;
            foreach ((int slot, ItemStack item) in slots)
            {
                if (Character.Wear.ContainsKey(item.Name)) RefreshDurability(view, slot);
            }
        }
    }

    public void RefreshDurability(int view, int slot)
    {
        if (!Bags.TryGetValue(view, out Dictionary<int, ItemStack>? slots) || !slots.TryGetValue(slot, out ItemStack? item)) return;
        int max = DurabilityMax(item.Name);
        if (max <= 0) return;
        int now = Durability(item.Name);
        PropertyBag update = SeaPackets.Known(new PropertyBag
        {
            ["DFG"] = now,
            ["DFGMax"] = max,
            ["DFGAlert"] = 100 * now <= max * Yard.Rules.WearAlertPercent ? 1 : 0,
        });
        if (update.Count > 0) Send(ViewPackets.UpdateItem(view, slot, update), "(durability)");
    }

    public void WearEquipment()
    {
        FleetRules rules = Yard.Rules;
        if (rules.WearChance <= 0 || Random.Shared.NextDouble() >= rules.WearChance) return;
        if (!Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn)) return;
        foreach ((int slot, ItemStack item) in worn.ToList())
        {
            int max = DurabilityMax(item.Name);
            int before = Durability(item.Name);
            if (max <= 0 || before <= 0) continue;
            int kind = ItemCatalog.Number(item.Name, "Kind");
            int care = kind == 3 ? UniqueSkillLevel("US_SEWING2") : kind is >= 5 and <= 8 ? UniqueSkillLevel("US_FOUNDRY2") : 0;
            if (Random.Shared.Next(100) < 5 * care) continue;
            Character.Wear ??= new Dictionary<string, int>();
            Character.Wear[item.Name] = max - before + 1;
            RefreshDurability(ViewId.Equipped, slot);
            int alert = max * rules.WearAlertPercent / 100;
            if (before - 1 == 0)
                SystemMessage("A piece of your equipment is worn out. Have it repaired.", SystemMessageKind.Prompt);
            else if (before > alert && before - 1 <= alert)
                SystemMessage("A piece of your equipment is badly worn. Have it repaired soon.", SystemMessageKind.Prompt);
        }
    }

    public void RepairItem(int toolView, int view, int index)
    {
        if (view is ViewId.Ship or ShipEquipmentViewId)
        {
            AskShipRepair();
            return;
        }
        if (toolView == ViewId.Booth || BrowsingStall)
        {
            RepairAtStall(view, index);
            return;
        }
        RepairAtNpc(view, index);
    }

    public void RepairAtNpc(int view, int index)
    {
        if (!ShopOpen && !ShipyardOpen)
        {
            SystemMessage("Find a merchant or a stall that repairs equipment.");
            return;
        }
        if (WornItem(view, index) is not { } item) return;
        int lost = DurabilityMax(item.Name) - Durability(item.Name);
        int cost = Math.Max(1, lost * Yard.Rules.RepairPricePerPoint);
        if (Money < cost)
        {
            SystemMessage($"Not enough money: the repair costs {cost} gold.");
            return;
        }
        SetMoney(Money - cost);
        Character.Wear!.Remove(item.Name);
        RefreshDurability(view, index);
        SystemMessage($"Your equipment was repaired for {cost} gold.");
        Save();
        Log.Info(Tag, $"{Name} has {item.Name} repaired for {cost}");
    }

    public void RepairAtStall(int view, int index)
    {
        ClientSession? owner = BoothOwner();
        if (owner == null) return;
        int price = owner.StallRepairPrice;
        if (price < 0)
        {
            SystemMessage("This stall does not offer repairs.");
            return;
        }
        if (WornItem(view, index) is not { } item) return;
        if (Money < price)
        {
            SystemMessage($"Not enough money: the repair costs {price} gold.");
            return;
        }
        if ((long)owner.Money + price > int.MaxValue)
        {
            SystemMessage($"{owner.Name} cannot carry more money.");
            return;
        }
        SetMoney(Money - price);
        owner.SetMoney(owner.Money + price);
        Character.Wear!.Remove(item.Name);
        RefreshDurability(view, index);
        SystemMessage($"{owner.Name} repaired your equipment for {price} gold.");
        owner.SystemMessage($"You repaired equipment for {Name} and earned {price} gold.");
        Save();
        owner.Save();
        Log.Info(Tag, $"{Name} has {item.Name} repaired at the stall of {owner.Name} for {price}");
    }

    private ItemStack? WornItem(int view, int index)
    {
        if (!DurabilityViews.Contains(view) || !Bags.TryGetValue(view, out Dictionary<int, ItemStack>? slots)
            || !slots.TryGetValue(index, out ItemStack? item) || DurabilityMax(item.Name) <= 0)
        {
            SystemMessage("That item cannot be repaired.");
            return null;
        }
        if (ItemCatalog.Number(item.Name, "Unrepairable") != 0)
        {
            SystemMessage("This article is unrepairable.");
            return null;
        }
        if (Character.Wear == null || Durability(item.Name) >= DurabilityMax(item.Name))
        {
            SystemMessage("That item does not need repairing.");
            return null;
        }
        return item;
    }
}
