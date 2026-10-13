using VoyageCentury.Core.Data;
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
        if (cost == null) ApplyLetterPaper(bag, item.Name);
        if (cost != null)
        {
            bag["Cost"] = cost.Value;
            bag["ShowPrice"] = cost.Value;
        }
        ItemCatalog.Extend(bag, item.Name);
        return bag;
    }

    public void SetMoney(long amount)
    {
        Character.Money = (int)Math.Clamp(amount, 0, Rule.Economy.SilverCap);
        SetProperty("Money", Character.Money);
    }

    public bool TradeGoods(string name) => ItemCatalog.Number(name, "ItemType") == SeaPackets.ItemTypeGoods;

    public bool EloquentFor(string name)
        => SkillLevel("SKILL_SPEECHCRAFT") >= 10 * Math.Max(1, ItemCatalog.Number(name, "Level", 1)) - 4;

    public int ShopPrice(string name)
    {
        int price = Context.Data.PriceOf(name);
        if (TradeGoods(name) && EloquentFor(name)) price = Math.Max(1, price * (100 - Rule.Economy.TradeDiscountPercent) / 100);
        return price;
    }

    public int SalePrice(string name)
    {
        EconomyRules rules = Rule.Economy;
        int basePrice = Context.Data.PriceOf(name);
        if (!TradeGoods(name) || _shop.Values.Any(shelf => shelf.Values.Any(entry => entry.Name == name)))
            return Math.Max(1, basePrice * rules.SellBackPercent / 100);
        long price = basePrice;
        if (EloquentFor(name)) price = price * (100 + rules.TradeBonusPercent) / 100;
        price = Math.Min(price, (long)basePrice * rules.TradePriceCapFactor);
        return (int)Math.Max(1, price * (100 - rules.TradeTaxPercent) / 100);
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
        PropertyBag inHand = ItemProperties(item, 1);
        inHand["ItemSrcVID"] = ViewId.BagItems;
        Send(ViewPackets.AddItem(ViewId.Hand, 1, inHand), "(item in hand)");
    }

    public void OpenShop(Dictionary<int, List<string>> stock)
    {
        CloseShop();
        CloseWarehouse();
        foreach ((int view, List<string> goods) in stock)
        {
            var shelf = new Dictionary<int, ShopEntry>();
            _shop[view] = shelf;
            Send(ViewPackets.Create(view, ViewId.ShopSlots), "(shop shelf)");
            for (int i = 0; i < goods.Count; i++)
            {
                var entry = new ShopEntry(goods[i], Context.Data.PhotoOf(goods[i]), ShopPrice(goods[i]));
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
        int free;
        if (target == ViewId.Equipped)
        {
            if (!MayWear(item.Name)) return true;
            int gear = GearClass(item.Name);
            free = gear != 0 ? Bags[target].FirstOrDefault(pair => GearClass(pair.Value.Name) == gear).Key : 0;
            if (free == 0) free = gear != 0 && !Bags[target].ContainsKey(gear) ? gear : FreeGearSlot(Bags[target]);
        }
        else
        {
            free = FreeSlot(Bags[target], ViewId.BagSlots);
        }
        if (free == 0) return true;
        Bags[view].Remove(index);
        Send(ViewPackets.RemoveItem(view, index), "(move gear)");
        if (Bags[target].Remove(free, out ItemStack? replaced))
        {
            Send(ViewPackets.RemoveItem(target, free), "(swap gear)");
            Bags[view][index] = replaced;
            Send(ViewPackets.AddItem(view, index, ItemProperties(replaced, index)), "(swap gear)");
            RefreshDurability(view, index);
        }
        Bags[target][free] = item;
        Send(ViewPackets.AddItem(target, free, ItemProperties(item, free)), "(move gear)");
        RefreshDurability(target, free);
        RefreshStats();
        Save();
        Log.Info(Tag, $"{Name} {(target == ViewId.Equipped ? "wears" : "takes off")} {item.Name}");
        return true;
    }

    public bool MayWear(string name)
    {
        string skill = ItemCatalog.Find(name) is { } properties && properties.TryGetValue("NeedSkill", out object? needed) ? needed as string ?? "" : "";
        int level = ItemCatalog.Number(name, "NeedSkillLevel");
        if (skill.Length > 0 && SkillLevel(skill) < Math.Max(1, level))
        {
            SystemMessage($"Skill requirement not met: you need Level {Math.Max(1, level)} in the skill this article asks for.", SystemMessageKind.Prompt);
            return false;
        }
        string any = ItemCatalog.Find(name) is { } other && other.TryGetValue("NeedAnySkill", out object? list) ? list as string ?? "" : "";
        int anyLevel = ItemCatalog.Number(name, "NeedAnySkillLevel");
        if (any.Length > 0 && !any.Split(',').Any(key => SkillLevel(key) >= Math.Max(1, anyLevel)))
        {
            SystemMessage($"Skill requirement not met: one of the skills this article asks for must reach Level {anyLevel}.", SystemMessageKind.Prompt);
            return false;
        }
        int rank = ItemCatalog.Number(name, "NeedRank");
        if (rank > Character.Rank)
        {
            SystemMessage("Your noble title is not high enough to use this article.", SystemMessageKind.Prompt);
            return false;
        }
        return true;
    }

    private static int GearClass(string name) => ItemCatalog.Number(name, "Kind") switch
    {
        >= 5 and <= 8 => 1,
        3 => 2,
        4 => 3,
        _ => 0,
    };

    private static int FreeGearSlot(Dictionary<int, ItemStack> worn)
    {
        for (int slot = 4; slot <= ViewId.EquippedSlots; slot++)
        {
            if (!worn.ContainsKey(slot)) return slot;
        }
        return 0;
    }

    public bool UseItem(int index)
    {
        if (!Items.TryGetValue(index, out ItemStack? item)) return false;
        int heal = Context.Data.HealOf(item.Name);
        int refresh = Context.Data.SpOf(item.Name);
        if (heal > 0 && Hp >= MaxHp && (refresh <= 0 || Sp >= MaxSp))
        {
            SystemMessage("You are already at full health.");
            return true;
        }
        if (heal <= 0 && refresh > 0 && Sp >= MaxSp)
        {
            SystemMessage("Your SP is already full.");
            return true;
        }
        if (ItemCooling(item.Name)) return true;
        if (Context.Data.InstantOf(item.Name))
        {
            int before = Hp;
            if (heal > 0) SetHp(Hp + heal);
            if (refresh > 0) SetSp(Sp + refresh);
            if (Hp > before)
            {
                Send(ChatPackets.FloatingText(ProtocolConstants.SelfObjectId, $"+{Hp - before}", 0x00ff00), "(heal number)");
                ToOthers(ChatPackets.FloatingText(ObjectId, $"+{Hp - before}", 0x00ff00), "(heal number)");
            }
        }
        else
        {
            if (heal > 0)
            {
                _healPool += LastingPotion(heal);
                SetKnownProperty("HealCondition", 1);
            }
            if (refresh > 0)
            {
                _spPool += LastingPotion(refresh);
                SetKnownProperty("FreshCondition", 1);
            }
        }
        StartItemCooling(item.Name);
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
        if (TradeGoods(entry.Name) && ItemCatalog.Number(entry.Name, "Level", 1) > Math.Max(1, UniqueSkillLevel("US_TRADE")))
        {
            SystemMessage("Purchasing Skill Level insufficient, cannot Purchase the Goods!", SystemMessageKind.Prompt);
            return false;
        }
        if (ItemCatalog.Number(entry.Name, "ItemType") == 10)
        {
            int gearSlot = FreeSlot(Bags[ViewId.BagEquip], ViewId.BagSlots);
            if (gearSlot == 0)
            {
                SystemMessage("Your equipment bag is full.");
                return false;
            }
            Bags[ViewId.BagEquip][gearSlot] = new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = 1 };
            Send(ViewPackets.AddItem(ViewId.BagEquip, gearSlot, ItemProperties(Bags[ViewId.BagEquip][gearSlot], gearSlot)), "(bought gear)");
            SetMoney(Money - entry.Cost);
            Save();
            Log.Info(Tag, $"{Name} buys {entry.Name} for {entry.Cost}");
            return true;
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
            Items[slot] = new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = 1, Cost = TradeGoods(entry.Name) ? entry.Cost : 0 };
            Send(ViewPackets.AddItem(ViewId.BagItems, slot, ItemProperties(Items[slot], slot)), "(bought item)");
        }
        else
        {
            if (TradeGoods(entry.Name))
                Items[slot].Cost = (int)(((long)Items[slot].Cost * Items[slot].Amount + entry.Cost) / (Items[slot].Amount + 1));
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
        int unit = SalePrice(item.Name);
        long price = (long)unit * quantity;
        long profit = item.Cost > 0 && TradeGoods(item.Name) ? (long)(unit - item.Cost) * quantity : 0;
        TakeFromBag(index, quantity, "(sold item)");
        SetMoney(Money + price);
        if (profit > 0) GainSkillExperience("SKILL_SPEECHCRAFT", profit * Rule.Skills.EloquenceExperiencePerProfit);
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

    public void SendSocialTables(bool clear = true)
    {
        foreach ((TableDefinition table, IEnumerable<object[]> rows) in new (TableDefinition, IEnumerable<object[]>)[]
        {
            (GameTables.Friends, FriendRows()),
            (GameTables.Blacklist, Character.Blacklist.Select(name => new object[] { name })),
            (GameTables.TitleList, Character.Titles.Select(title => new object[] { title })),
        })
        {
            if (clear) Send(TablePackets.Clear(table), $"(clear {table.Name})");
            List<object[]> list = rows.ToList();
            if (list.Count > 0) Send(TablePackets.Rows(table, list), $"({table.Name})");
        }
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
        CompactBags();
    }

    private void SendInventory()
    {
        foreach ((int view, Dictionary<int, ItemStack> slots) in Bags.OrderBy(pair => pair.Key))
        {
            Send(ViewPackets.Create(view, view == ViewId.Equipped ? ViewId.EquippedSlots : ViewId.BagSlots), $"(view {view})");
            foreach ((int slot, ItemStack item) in slots.OrderBy(pair => pair.Key))
                Send(ViewPackets.AddItem(view, slot, ItemProperties(item, slot)), $"(view {view} item {item.Name})");
        }
        Send(ViewPackets.Create(ViewId.Ship, 1), "(ship view)");
        Send(ViewPackets.AddItem(ViewId.Ship, 1, ShipDetails()), "(ship details)");
        Send(ViewPackets.Create(ViewId.BagItems, ViewId.BagSlots), "(bag view)");
        foreach ((int slot, ItemStack item) in Items.OrderBy(pair => pair.Key))
            Send(ViewPackets.AddItem(ViewId.BagItems, slot, ItemProperties(item, slot)), $"(bag item {item.Name})");
    }

    public void HireSailors(int count) => HireShipSailors(count);

    public void FireSailors(int count) => DismissShipSailors(count);

    public void RepairShip() => RepairShipHull();

    private PropertyBag ShipDetails() => FleetShipDetails();

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
