using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed record ShopEntry(string Name, int Photo, int Cost);

public sealed partial class ClientSession
{
    private readonly Dictionary<int, Dictionary<int, ShopEntry>> _shop = [];
    private bool _inventorySent;
    private bool _handViewOpen;

    public ItemStack? Hand { get; private set; }

    public bool ShopOpen => _shop.Count > 0;

    public Dictionary<int, ItemStack> Items => Character.Items!;

    public Dictionary<int, Dictionary<int, ItemStack>> Bags => Character.Bags!;

    public static PropertyBag ItemProperties(ItemStack item, int slot, int? cost = null)
    {
        var bag = new PropertyBag
        {
            ["Name"] = item.Name,
            ["Index"] = slot,
            ["Amount"] = item.Amount,
            ["Photo"] = item.Photo,
            ["Kind"] = 1,
            ["ItemType"] = 1,
        };
        if (cost != null) bag["Cost"] = cost.Value;
        return bag;
    }

    public void SetMoney(int amount)
    {
        Character.Money = amount;
        SetProperty("Money", amount);
    }

    public void SetHand(ItemStack? item)
    {
        if (Hand != null) Send(ViewPackets.RemoveItem(ViewId.Hand, 1), "(hand empty)");
        Hand = item;
        if (item == null) return;
        if (!_handViewOpen)
        {
            _handViewOpen = true;
            Send(ViewPackets.Create(ViewId.Hand, 1), "(hand view)");
        }
        Send(ViewPackets.AddItem(ViewId.Hand, 1, ItemProperties(item, 1)), "(item in hand)");
    }

    public void OpenShop(Dictionary<int, List<string>> stock)
    {
        CloseShop();
        foreach ((int view, List<string> goods) in stock)
        {
            var shelf = new Dictionary<int, ShopEntry>();
            _shop[view] = shelf;
            Send(ViewPackets.Create(view, ViewId.ShopSlots), "(shop shelf)");
            for (int i = 0; i < goods.Count; i++)
            {
                var entry = new ShopEntry(goods[i], Context.Data.PhotoOf(goods[i]), Context.Data.PriceOf(goods[i]));
                shelf[i + 1] = entry;
                var item = new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = 1 };
                Send(ViewPackets.AddItem(view, i + 1, ItemProperties(item, i + 1, entry.Cost)), "(shop item)");
            }
        }
    }

    public void CloseShop()
    {
        foreach (int view in _shop.Keys) Send(ViewPackets.Delete(view), "(close shop shelf)");
        _shop.Clear();
    }

    public bool MoveGear(int view, int index)
    {
        if (view is not (ViewId.BagEquip or ViewId.Equipped)) return false;
        if (!Bags[view].TryGetValue(index, out ItemStack? item)) return false;
        int target = view == ViewId.BagEquip ? ViewId.Equipped : ViewId.BagEquip;
        int free = FreeSlot(Bags[target], target == ViewId.Equipped ? ViewId.EquippedSlots : ViewId.BagSlots);
        if (free == 0) return true;
        Bags[view].Remove(index);
        Bags[target][free] = item;
        Send(ViewPackets.RemoveItem(view, index), "(move gear)");
        Send(ViewPackets.AddItem(target, free, ItemProperties(item, free)), "(move gear)");
        Save();
        Log.Info(Tag, $"{Name} {(target == ViewId.Equipped ? "wears" : "takes off")} {item.Name}");
        return true;
    }

    public bool UseItem(int index)
    {
        if (!Items.TryGetValue(index, out ItemStack? item)) return false;
        int heal = Context.Data.HealOf(item.Name);
        if (heal > 0 && Hp >= MaxHp)
        {
            SystemMessage("You are already at full health.");
            return true;
        }
        if (heal > 0) SetHp(Hp + heal);
        TakeFromBag(index, 1, "(item used)");
        Save();
        Log.Info(Tag, $"{Name} uses {item.Name} (slot {index})");
        return true;
    }

    public bool BuyOne(int view, int index)
    {
        if (!_shop.TryGetValue(view, out Dictionary<int, ShopEntry>? shelf) || !shelf.TryGetValue(index, out ShopEntry? entry))
            return false;
        if (Money < entry.Cost)
        {
            SystemMessage("Not enough money.");
            return false;
        }
        int slot = Items.FirstOrDefault(pair => pair.Value.Name == entry.Name, new KeyValuePair<int, ItemStack>(0, null!)).Key;
        if (slot == 0)
        {
            slot = FreeSlot(Items, ViewId.BagSlots);
            if (slot == 0)
            {
                SystemMessage("Your bag is full.");
                return false;
            }
            Items[slot] = new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = 1 };
            Send(ViewPackets.AddItem(ViewId.BagItems, slot, ItemProperties(Items[slot], slot)), "(bought item)");
        }
        else
        {
            Items[slot].Amount++;
            Send(ViewPackets.UpdateItem(ViewId.BagItems, slot, new PropertyBag { ["Amount"] = Items[slot].Amount }), "(bought item)");
        }
        SetMoney(Money - entry.Cost);
        Save();
        Log.Info(Tag, $"{Name} buys {entry.Name} for {entry.Cost}");
        return true;
    }

    public bool SellFromBag(int index, int quantity)
    {
        if (quantity <= 0 || !Items.TryGetValue(index, out ItemStack? item)) return false;
        quantity = Math.Min(quantity, item.Amount);
        int price = Math.Max(1, Context.Data.PriceOf(item.Name) / 2) * quantity;
        TakeFromBag(index, quantity, "(sold item)");
        SetMoney(Money + price);
        Save();
        Log.Info(Tag, $"{Name} sells {quantity} x {item.Name} for {price}");
        return true;
    }

    public bool ThrowAway(int index)
    {
        if (!Items.Remove(index, out ItemStack? item)) return false;
        Send(ViewPackets.RemoveItem(ViewId.BagItems, index), "(item thrown away)");
        Save();
        Log.Info(Tag, $"{Name} throws away {item.Name}");
        return true;
    }

    public void SendShortcuts(bool clear = true)
    {
        if (clear) Send(TablePackets.Clear(GameTables.Shortcut), "(clear shortcuts)");
        List<object[]> rows = Character.Shortcuts
            .OrderBy(pair => pair.Key)
            .Select(pair => new object[] { pair.Key, pair.Value.Kind, pair.Value.Name })
            .ToList();
        if (rows.Count > 0) Send(TablePackets.Rows(GameTables.Shortcut, rows), "(shortcuts)");
        if (clear) Save();
    }

    private void EnsureInventory()
    {
        if (Character.Items == null)
        {
            Character.Items = [];
            foreach (ItemStack item in Context.Data.Items.StarterItems)
                Character.Items[Character.Items.Count + 1] = item.Clone();
        }
        if (Character.Bags == null)
        {
            Character.Bags = new Dictionary<int, Dictionary<int, ItemStack>> { [ViewId.BagEquip] = [] };
            foreach (ItemStack item in Context.Data.Items.StarterEquipment)
                Character.Bags[ViewId.BagEquip][Character.Bags[ViewId.BagEquip].Count + 1] = item.Clone();
        }
        foreach (int view in new[] { ViewId.BagEquip, ViewId.BagArticle, ViewId.Equipped })
            Character.Bags.TryAdd(view, []);
    }

    private void SendInventory()
    {
        foreach ((int view, Dictionary<int, ItemStack> slots) in Bags.OrderBy(pair => pair.Key))
        {
            Send(ViewPackets.Create(view, ViewId.BagSlots), $"(view {view})");
            foreach ((int slot, ItemStack item) in slots.OrderBy(pair => pair.Key))
                Send(ViewPackets.AddItem(view, slot, ItemProperties(item, slot)), $"(view {view} item {item.Name})");
        }
        Send(ViewPackets.Create(ViewId.BagItems, ViewId.BagSlots), "(bag view)");
        foreach ((int slot, ItemStack item) in Items.OrderBy(pair => pair.Key))
            Send(ViewPackets.AddItem(ViewId.BagItems, slot, ItemProperties(item, slot)), $"(bag item {item.Name})");
    }

    private void TakeFromBag(int index, int quantity, string note)
    {
        ItemStack item = Items[index];
        item.Amount -= quantity;
        if (item.Amount > 0)
        {
            Send(ViewPackets.UpdateItem(ViewId.BagItems, index, new PropertyBag { ["Amount"] = item.Amount }), note);
        }
        else
        {
            Items.Remove(index);
            Send(ViewPackets.RemoveItem(ViewId.BagItems, index), note);
        }
    }

    private static int FreeSlot(Dictionary<int, ItemStack> slots, int capacity)
    {
        for (int slot = 1; slot <= capacity; slot++)
        {
            if (!slots.ContainsKey(slot)) return slot;
        }
        return 0;
    }
}
