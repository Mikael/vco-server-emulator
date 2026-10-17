using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed record ShopEntry(string Name, int Photo, int Cost);

public sealed partial class ClientSession
{
    private readonly Dictionary<int, Dictionary<int, ShopEntry>> _shop = [];
    private bool _inventorySent;
    private bool _handViewOpen;

    private const int MostAtOnce = 99;
    private const int EquipmentType = 10;
    private const string CannotAfford = "Sorry, your load or amount of silver coins is not enough to purchase so much goods.";

    public ItemStack? Hand { get; private set; }

    public bool ShopOpen => _shop.Count > 0;

    public Dictionary<int, ItemStack> Items => Character.Items!;

    public Dictionary<int, Dictionary<int, ItemStack>> Bags => Character.Bags!;

    public Dictionary<int, ItemStack> Cargo => Bags[ViewId.Cargo];

    private Dictionary<int, ItemStack>? OwnSlots(int view) => view switch
    {
        ViewId.BagItems => Items,
        ViewId.BagEquip or ViewId.BagArticle or ViewId.Cargo => Bags[view],
        _ => null,
    };

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
        if (cost == null && item.Cost > 0) bag["Cost"] = item.Cost;
        if (cost != null)
        {
            bag["Cost"] = cost.Value;
            bag["ShowPrice"] = cost.Value;
        }
        AddBboItemId(bag, item);
        AddFireFlagItemProperties(bag, item);
        ItemCatalog.Extend(bag, item.Name);
        ArmUpgradeProperties(bag, item);
        ArmRebuildItemProperties(bag, item);
        AddGuildArmProperties(GuildBuildData.Shared.Medals, bag, item.Name);
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
        SetHand(null);
        if (stock.ContainsKey(ViewId.TradeGoods)) StowTradeGoods();
        foreach ((int view, List<string> goods) in stock)
        {
            bool bourse = view == ViewId.TradeGoods;
            int capacity = bourse ? ViewId.CargoSlots : ViewId.ShopSlots;
            var shelf = new Dictionary<int, ShopEntry>();
            _shop[view] = shelf;
            Send(ViewPackets.Create(view, capacity), "(shop shelf)");
            for (int i = 0; i < Math.Min(goods.Count, capacity); i++)
            {
                var entry = new ShopEntry(goods[i], Context.Data.PhotoOf(goods[i]), ShopPrice(goods[i]));
                shelf[i + 1] = entry;
                var item = new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = 1 };
                Send(ViewPackets.AddItem(view, i + 1, ItemProperties(item, i + 1, entry.Cost)), "(shop item)");
            }
            if (bourse) SendGoodsInfo(shelf);
        }
        PriceOwnItems();
        OpenBuyBack();
    }

    public void CloseShop()
    {
        foreach (int view in _shop.Keys) Send(ViewPackets.Delete(view), "(close shop shelf)");
        _shop.Clear();
        CloseBuyBack();
    }

    private void SendGoodsInfo(Dictionary<int, ShopEntry> shelf)
    {
        IEnumerable<string> marks = shelf.OrderBy(pair => pair.Key)
            .Select(pair => MayTrade(pair.Value.Name) ? Context.Data.PriceOf(pair.Value.Name).ToString() : "0");
        Send(UiPackets.Custom($"GoodsInfo {Rule.Economy.TradeTaxPercent} {string.Join(' ', marks)}"), "(goods info)");
    }

    private bool MayTrade(string name)
        => ItemCatalog.Number(name, "Level", 1) <= Math.Max(1, UniqueSkillLevel("US_TRADE"));

    private bool Sellable(string name) => TradeGoods(name) == _shop.ContainsKey(ViewId.TradeGoods);

    private void PriceOwnItems()
    {
        foreach (int view in new[] { ViewId.BagEquip, ViewId.BagArticle, ViewId.BagItems, ViewId.Cargo })
        {
            foreach (int slot in OwnSlots(view)!.Keys.Order().ToList()) PriceOwnItem(view, slot);
        }
    }

    private void PriceOwnItem(int view, int slot)
    {
        if (!ShopOpen || OwnSlots(view) is not { } slots || !slots.TryGetValue(slot, out ItemStack? item)) return;
        var prices = new PropertyBag { ["ShowPrice"] = Sellable(item.Name) ? SalePrice(item.Name) : 0 };
        if (view == ViewId.Cargo) prices["Cost"] = item.Cost;
        Send(ViewPackets.UpdateItem(view, slot, prices), "(sale price)");
    }

    private void StowTradeGoods()
    {
        foreach ((int slot, ItemStack item) in Items.Where(pair => TradeGoods(pair.Value.Name)).OrderBy(pair => pair.Key).ToList())
        {
            int target = Cargo.FirstOrDefault(pair => pair.Value.Name == item.Name).Key;
            if (target == 0) target = FreeSlot(Cargo, ViewId.CargoSlots);
            if (target == 0) continue;
            Items.Remove(slot);
            if (Cargo.TryGetValue(target, out ItemStack? stack))
            {
                stack.Cost = (int)(((long)stack.Cost * stack.Amount + (long)item.Cost * item.Amount) / (stack.Amount + item.Amount));
                stack.Amount += item.Amount;
            }
            else
            {
                Cargo[target] = item;
            }
            if (!_inventorySent) continue;
            Send(ViewPackets.RemoveItem(ViewId.BagItems, slot), "(goods to the cabin)");
            if (stack == null) Send(ViewPackets.AddItem(ViewId.Cargo, target, ItemProperties(item, target)), "(goods to the cabin)");
            else Send(ViewPackets.UpdateItem(ViewId.Cargo, target, new PropertyBag { ["Amount"] = stack.Amount, ["Cost"] = stack.Cost }), "(goods to the cabin)");
        }
    }

    public int Owned(string name)
        => Items.Values.Concat(Cargo.Values).Where(stack => stack.Name == name).Sum(stack => stack.Amount);

    public void TakeOwned(string name, int count, string note)
    {
        foreach (int view in new[] { ViewId.BagItems, ViewId.Cargo })
        {
            foreach (int slot in OwnSlots(view)!.Where(pair => pair.Value.Name == name).Select(pair => pair.Key).Order().ToList())
            {
                if (count <= 0) return;
                int taken = Math.Min(count, OwnSlots(view)![slot].Amount);
                TakeFrom(view, slot, taken, note);
                count -= taken;
            }
        }
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
        RefreshAppearance();
        RefreshStats();
        Save();
        Log.Info(Tag, $"{Name} {(target == ViewId.Equipped ? "wears" : "takes off")} {item.Name}");
        return true;
    }

    public bool MayWear(string name)
    {
        string skill = ItemCatalog.Find(name) is { } properties && properties.TryGetValue("NeedSkill", out object? needed) ? needed as string ?? "" : "";
        int level = ItemCatalog.Number(name, "NeedSkillLevel");
        if (skill.Length > 0 && WeaponSkillLevel(skill) < Math.Max(1, level))
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
        int military = ItemCatalog.Number(name, "NeedMilitaryRank");
        if (military > MilitaryRankLevel)
        {
            SystemMessage("Your military rank is not high enough to use this article.", SystemMessageKind.Prompt);
            return false;
        }
        return true;
    }

    private int WeaponSkillLevel(string skill)
        => skill == "SKILL_BOW" ? Math.Max(SkillLevel(skill), SkillLevel("SKILL_SHOT")) : SkillLevel(skill);

    private static int GearClass(string name) => ItemCatalog.Number(name, "ArmType") switch
    {
        1 or 2 => 1,
        4 => 2,
        5 => 3,
        _ => ItemCatalog.Number(name, "Kind") switch
        {
            >= 5 and <= 8 => 1,
            3 => 2,
            4 => 3,
            _ => 0,
        },
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
        if (UseArmRebuildItem(item)) return true;
        if (UseGuildBuildItem(index, item)) return true;
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

    public bool AskToBuy(int view, int index)
    {
        if (!_shop.TryGetValue(view, out Dictionary<int, ShopEntry>? shelf) || !shelf.TryGetValue(index, out ShopEntry? entry))
            return false;
        SetHand(null);
        int most = MostBuyable(entry);
        if (most > 0) Send(UiPackets.Custom($"BuyInfo {view} {index} {entry.Cost} {most}"), "(buy offer)");
        return true;
    }

    private int MostBuyable(ShopEntry entry)
    {
        if (TradeGoods(entry.Name) && !MayTrade(entry.Name))
        {
            SystemMessage("Purchasing Skill Level insufficient, cannot Purchase the Goods!", SystemMessageKind.Prompt);
            return 0;
        }
        long most = Math.Min(RoomFor(entry.Name), entry.Cost > 0 ? Money / entry.Cost : MostAtOnce);
        if (most <= 0) SystemMessage(CannotAfford, SystemMessageKind.Prompt);
        return (int)Math.Max(0, most);
    }

    private int RoomFor(string name)
    {
        if (TradeGoods(name))
        {
            if (!Cargo.Values.Any(stack => stack.Name == name) && FreeSlot(Cargo, ViewId.CargoSlots) == 0) return 0;
            int weight = ItemCatalog.Number(name, "Weight");
            return weight <= 0 ? MostAtOnce : Math.Max(0, (ShipLoadMax - ShipLoad) / weight);
        }
        if (ItemCatalog.Number(name, "ItemType") == EquipmentType)
            return Enumerable.Range(1, ViewId.BagSlots).Count(slot => !Bags[ViewId.BagEquip].ContainsKey(slot));
        return Items.Values.Any(stack => stack.Name == name) || FreeSlot(Items, ViewId.BagSlots) != 0 ? MostAtOnce : 0;
    }

    public bool BuyFromShop(int view, int index, int count)
    {
        if (!_shop.TryGetValue(view, out Dictionary<int, ShopEntry>? shelf) || !shelf.TryGetValue(index, out ShopEntry? entry))
            return false;
        if (count < 1)
        {
            SystemMessage("At least choose one to purchase.", SystemMessageKind.Prompt);
            return true;
        }
        int most = MostBuyable(entry);
        if (most <= 0) return true;
        if (count > most)
        {
            SystemMessage(CannotAfford, SystemMessageKind.Prompt);
            return true;
        }
        if (TradeGoods(entry.Name))
        {
            AddBought(ViewId.Cargo, ViewId.CargoSlots, entry, count, entry.Cost);
        }
        else if (ItemCatalog.Number(entry.Name, "ItemType") == EquipmentType)
        {
            Dictionary<int, ItemStack> gear = Bags[ViewId.BagEquip];
            for (int i = 0; i < count; i++)
            {
                int slot = FreeSlot(gear, ViewId.BagSlots);
                gear[slot] = new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = 1 };
                Send(ViewPackets.AddItem(ViewId.BagEquip, slot, ItemProperties(gear[slot], slot)), "(bought gear)");
                PriceOwnItem(ViewId.BagEquip, slot);
            }
        }
        else
        {
            AddBought(ViewId.BagItems, ViewId.BagSlots, entry, count, 0);
        }
        long price = (long)entry.Cost * count;
        SetMoney(Money - price);
        RefreshLoad();
        Save();
        Log.Info(Tag, $"{Name} buys {count} x {entry.Name} for {price}");
        return true;
    }

    private void AddBought(int view, int capacity, ShopEntry entry, int count, int cost)
    {
        Dictionary<int, ItemStack> slots = OwnSlots(view)!;
        int slot = slots.FirstOrDefault(pair => pair.Value.Name == entry.Name).Key;
        if (slot == 0)
        {
            slot = FreeSlot(slots, capacity);
            slots[slot] = new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = count, Cost = cost };
            Send(ViewPackets.AddItem(view, slot, ItemProperties(slots[slot], slot)), "(bought item)");
        }
        else
        {
            ItemStack stack = slots[slot];
            if (cost > 0) stack.Cost = (int)(((long)stack.Cost * stack.Amount + (long)cost * count) / (stack.Amount + count));
            stack.Amount += count;
            Send(ViewPackets.UpdateItem(view, slot, new PropertyBag { ["Amount"] = stack.Amount }), "(bought item)");
        }
        PriceOwnItem(view, slot);
    }

    public bool SellToShop(int shopView, int view, int index, int quantity)
    {
        if (!_shop.ContainsKey(shopView)) return false;
        if (view == ViewId.Equipped)
        {
            SystemMessage("Take the article off before you sell it.", SystemMessageKind.Prompt);
            return true;
        }
        if (OwnSlots(view) is not { } slots || !slots.TryGetValue(index, out ItemStack? item)) return true;
        if (!Sellable(item.Name))
        {
            SystemMessage(TradeGoods(item.Name) ? "Trade goods are sold to a Trader." : "The Trader only deals in trade goods.", SystemMessageKind.Prompt);
            return true;
        }
        if (quantity < 1)
        {
            SystemMessage("You must have at least 1 item to sell.", SystemMessageKind.Prompt);
            return true;
        }
        if (quantity > item.Amount)
        {
            SystemMessage("Sorry, you haven't much goods to sell.", SystemMessageKind.Prompt);
            return true;
        }
        int unit = SalePrice(item.Name);
        long price = (long)unit * quantity;
        long profit = item.Cost > 0 && TradeGoods(item.Name) ? (long)(unit - item.Cost) * quantity : 0;
        if (Hand != null && Hand.Name == item.Name) SetHand(null);
        TakeFrom(view, index, quantity, "(sold item)");
        SetMoney(Money + price);
        if (profit > 0) GainSkillExperience("SKILL_SPEECHCRAFT", profit * Rule.Skills.EloquenceExperiencePerProfit);
        RefreshLoad();
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
        foreach (int view in new[] { ViewId.BagEquip, ViewId.BagArticle, ViewId.Equipped, ViewId.Cargo })
            Character.Bags.TryAdd(view, []);
        CompactBags();
        StowTradeGoods();
    }

    private void SendInventory()
    {
        foreach ((int view, Dictionary<int, ItemStack> slots) in Bags.OrderBy(pair => pair.Key))
        {
            int capacity = view == ViewId.Equipped ? ViewId.EquippedSlots : view == ViewId.Cargo ? ViewId.CargoSlots : ViewId.BagSlots;
            Send(ViewPackets.Create(view, capacity), $"(view {view})");
            foreach ((int slot, ItemStack item) in slots.OrderBy(pair => pair.Key))
                Send(ViewPackets.AddItem(view, slot, ItemProperties(item, slot)), $"(view {view} item {item.Name})");
        }
        Send(ViewPackets.Create(ViewId.Ship, 1), "(ship view)");
        Send(ViewPackets.AddItem(ViewId.Ship, 1, ShipDetails()), "(ship details)");
        SendShipStorageViews();
        Send(ViewPackets.Create(ViewId.BagItems, ViewId.BagSlots), "(bag view)");
        foreach ((int slot, ItemStack item) in Items.OrderBy(pair => pair.Key))
            Send(ViewPackets.AddItem(ViewId.BagItems, slot, ItemProperties(item, slot)), $"(bag item {item.Name})");
    }

    public void HireSailors(int count) => HireShipSailors(count);

    public void FireSailors(int count) => DismissShipSailors(count);

    public void RepairShip() => RepairShipHull();

    private PropertyBag ShipDetails() => BboShipDetails(FleetShipDetails());

    private void TakeFromBag(int index, int quantity, string note) => TakeFrom(ViewId.BagItems, index, quantity, note);

    private void TakeFrom(int view, int index, int quantity, string note)
    {
        Dictionary<int, ItemStack> slots = OwnSlots(view)!;
        ItemStack item = slots[index];
        item.Amount -= quantity;
        if (item.Amount > 0)
        {
            Send(ViewPackets.UpdateItem(view, index, new PropertyBag { ["Amount"] = item.Amount }), note);
        }
        else
        {
            slots.Remove(index);
            Send(ViewPackets.RemoveItem(view, index), note);
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
