using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed record BuyBackEntry(ItemStack Item, int Unit);

public static class BboItemOption
{
    public const int First = 360;
    public const int Compound = 360;
    public const int FirstRecipe = 361;
    public const int LastRecipe = 379;
    public const int ConsignProceeds = 380;
    public const int Leave = 389;
    public const int Last = 389;
}

public sealed partial class ClientSession
{
    public const int ShopBuyBackView = 48;
    public const int CompoundView = 68;
    public const int GlobalCollectView = 98;
    public const string ChuckConfirm = "Chuck sure";

    private const int BuyBackSlots = 18;
    private const int SortMilliseconds = 2000;

    private static readonly int[] BboBagViews = [ViewId.BagEquip, ViewId.BagArticle, ViewId.BagItems];

    private readonly List<BuyBackEntry> _buyBack = [];
    private readonly HashSet<int> _compoundShown = [];
    private (int View, int Index)? _handFrom;
    private bool _buyBackOpen;
    private int _buyBackShown;
    private bool _compoundOpen;
    private long _sortedAt;

    public void PickToHand(int view, int index, int amount)
    {
        if (MailBagOf(view) is not { } bag || !bag.TryGetValue(index, out ItemStack? item)) return;
        ItemStack taken = item.Clone();
        taken.Amount = Math.Clamp(amount, 1, item.Amount);
        SetHand(taken);
        _handFrom = (view, index);
    }

    public void DropHand()
    {
        SetHand(null);
        _handFrom = null;
    }

    public bool BagSelect(int view, int how, int index)
    {
        if (how != 0 || MailBagOf(view) is not { } bag) return false;
        if (Hand == null)
        {
            if (!bag.ContainsKey(index)) return false;
            PickToHand(view, index, int.MaxValue);
            return true;
        }
        if (HandSource() is { } gear && gear.View != view
            && (gear.View, view) is (ViewId.BagEquip, ViewId.Equipped) or (ViewId.Equipped, ViewId.BagEquip))
        {
            DropHand();
            MoveGear(gear.View, gear.Index, sound: false);
            return true;
        }
        if (HandSource() is not { } from || from.View != view || from.Index == index)
        {
            DropHand();
            return true;
        }
        int amount = Hand.Amount;
        DropHand();
        MoveStack(view, from.Index, index, amount);
        return true;
    }

    public void HandTo(string where)
    {
        if (Hand == null) return;
        (int View, int Index)? from = HandSource();
        switch (where)
        {
            case "a" when from is { View: ViewId.BagEquip } gear:
                DropHand();
                MoveGear(ViewId.BagEquip, gear.Index);
                break;
            case "I" when from is { } source:
                int amount = Hand.Amount;
                DropHand();
                MoveToHold(source.View, source.Index, amount);
                break;
            case "A":
                DropHand();
                SystemMessage("Ship equipment is fitted at a shipyard.", SystemMessageKind.Prompt);
                break;
            default:
                DropHand();
                break;
        }
    }

    public void Chuck(bool sure)
    {
        if (Hand == null) return;
        if (!sure)
        {
            Send(UiPackets.ShowQuestionBox($"Throw away {Hand.Amount} article(s)? They will be lost.", ChuckConfirm), "(throw away?)");
            return;
        }
        if (HandSource() is not { } from)
        {
            DropHand();
            return;
        }
        string name = Hand.Name;
        int amount = Hand.Amount;
        DropHand();
        TakeFrom(from.View, from.Index, amount, "(item thrown away)");
        RefreshLoad();
        Save();
        Log.Info(Tag, $"{Name} throws away {amount} x {name}");
    }

    public void SortBag(int view)
    {
        if (MailBagOf(view) is not { } bag) return;
        long now = Environment.TickCount64;
        if (now - _sortedAt < SortMilliseconds) return;
        _sortedAt = now;
        if (Hand != null) DropHand();
        var stacks = new List<ItemStack>();
        foreach (ItemStack item in bag.OrderBy(pair => pair.Key).Select(pair => pair.Value))
        {
            ItemStack? same = Stackable(item.Name) ? stacks.FirstOrDefault(stack => stack.Name == item.Name) : null;
            if (same == null)
            {
                stacks.Add(item);
                continue;
            }
            same.Cost = (int)(((long)same.Cost * same.Amount + (long)item.Cost * item.Amount) / Math.Max(1, same.Amount + item.Amount));
            same.Amount += item.Amount;
        }
        stacks = stacks
            .OrderBy(stack => ItemCatalog.Number(stack.Name, "ItemType"))
            .ThenBy(stack => stack.Name, StringComparer.Ordinal)
            .ToList();
        foreach (int slot in bag.Keys.Order().ToList()) Send(ViewPackets.RemoveItem(view, slot), "(bag sorted)");
        bag.Clear();
        for (int i = 0; i < stacks.Count; i++)
        {
            bag[i + 1] = stacks[i];
            Send(ViewPackets.AddItem(view, i + 1, ItemProperties(stacks[i], i + 1)), "(bag sorted)");
            if (view == ViewId.BagEquip) RefreshDurability(view, i + 1);
            PriceOwnItem(view, i + 1);
        }
        Save();
        Log.Info(Tag, $"{Name} sorts bag tab {view} ({stacks.Count} stacks)");
    }

    public void ExpandBag(int view, int rows)
    {
        SystemMessage("This bag cannot be enlarged here.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} asks to unlock {rows} cell(s) of view {view}");
    }

    public void ExpandContainer(int view, int row)
    {
        if (view == ViewId.Warehouse && WarehouseOpen)
        {
            ExpandWarehouse();
            return;
        }
        SystemMessage("This container cannot be enlarged here.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} asks to enlarge view {view} (row {row})");
    }

    public void UseOnTarget(int view, int index, int target)
    {
        if (view == ViewId.BagItems && (target == 0 || target == ObjectId || target == ProtocolConstants.SelfObjectId))
        {
            UseItem(index);
            return;
        }
        SystemMessage("That article cannot be used on this target.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} uses view {view} slot {index} on object {target}");
    }

    public void UseOnBoxItem(int view, int index, int targetView, int targetIndex)
    {
        if (ArmRebuildBoxItem(view, index, targetView, targetIndex)) return;
        if (TotemUseOnBox(view, index, targetView, targetIndex)) return;
        if (targetView == ArmRankView && ArmRankPut(view, index)) return;
        if (targetView == SoulView && SoulBoxPut(view, index)) return;
        if (targetView == CompoundView && _compoundOpen)
        {
            CompoundPut(view, index);
            return;
        }
        SystemMessage("Nothing happens.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} uses view {view} slot {index} on view {targetView} slot {targetIndex}");
    }

    public void OpenBuyBack()
    {
        if (_yard.ContainsKey(ShopBuyBackView)) return;
        if (!_buyBackOpen)
        {
            _buyBackOpen = true;
            _buyBackShown = 0;
            Send(ViewPackets.Create(ShopBuyBackView, BuyBackSlots), "(buy-back view)");
        }
        ShowBuyBack();
    }

    public void CloseBuyBack()
    {
        if (!_buyBackOpen || ShopOpen) return;
        _buyBackOpen = false;
        _buyBackShown = 0;
        Send(ViewPackets.Delete(ShopBuyBackView), "(close buy-back)");
    }

    public bool ShopBuyBackActive => _buyBackOpen && ShopOpen && !_yard.ContainsKey(ShopBuyBackView);

    public void SellAndRemember(int shopView, int view, int index, int quantity)
    {
        ItemStack? before = OwnSlots(view) is { } slots && slots.TryGetValue(index, out ItemStack? item) ? item.Clone() : null;
        int money = Money;
        SellToShop(shopView, view, index, quantity);
        if (before == null) return;
        int left = OwnSlots(view)!.TryGetValue(index, out ItemStack? now) && now.Name == before.Name ? now.Amount : 0;
        int sold = before.Amount - left;
        if (sold <= 0 || Money <= money) return;
        RecordBuyBack(before.Name, before.Photo, sold, (Money - money) / sold);
    }

    public void RecordBuyBack(string name, int photo, int amount, int unit)
    {
        if (amount <= 0) return;
        if (_buyBack.Count >= BuyBackSlots) _buyBack.RemoveAt(0);
        _buyBack.Add(new BuyBackEntry(new ItemStack { Name = name, Photo = photo, Amount = amount }, Math.Max(1, unit)));
        if (_buyBackOpen) ShowBuyBack();
    }

    public bool AskBuyBack(int index)
    {
        if (!ShopBuyBackActive) return false;
        DropHand();
        if (index < 1 || index > _buyBack.Count) return true;
        BuyBackEntry entry = _buyBack[index - 1];
        long most = Math.Min(entry.Item.Amount, Money / entry.Unit);
        most = Math.Min(most, RoomFor(entry.Item.Name));
        if (most <= 0)
        {
            SystemMessage(CannotAfford, SystemMessageKind.Prompt);
            return true;
        }
        Send(UiPackets.Custom($"BuyInfo {ShopBuyBackView} {index} {entry.Unit} {most}"), "(buy-back offer)");
        return true;
    }

    public bool BuyBack(int index, int count)
    {
        if (!ShopBuyBackActive) return false;
        if (index < 1 || index > _buyBack.Count) return true;
        BuyBackEntry entry = _buyBack[index - 1];
        count = Math.Min(count, entry.Item.Amount);
        if (count < 1)
        {
            SystemMessage("At least choose one to purchase.", SystemMessageKind.Prompt);
            return true;
        }
        long price = (long)entry.Unit * count;
        if (price > Money)
        {
            SystemMessage(CannotAfford, SystemMessageKind.Prompt);
            return true;
        }
        ItemStack bought = entry.Item.Clone();
        bought.Amount = count;
        if (!GiveItem(bought, "(bought back)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return true;
        }
        SetMoney(Money - price);
        entry.Item.Amount -= count;
        if (entry.Item.Amount <= 0) _buyBack.RemoveAt(index - 1);
        ShowBuyBack();
        RefreshLoad();
        Save();
        Log.Info(Tag, $"{Name} buys back {count} x {bought.Name} for {price}");
        return true;
    }

    public bool HandleBboItemOption(int target, int option)
    {
        if (option < BboItemOption.First || option > BboItemOption.Last) return false;
        CompoundBook book = CompoundBook.For(Context.Settings);
        if (option == BboItemOption.Compound)
        {
            OpenCompound();
            ShowCompoundMenu(target, book);
            return true;
        }
        if (option >= BboItemOption.FirstRecipe && option <= BboItemOption.LastRecipe)
        {
            int number = option - BboItemOption.FirstRecipe;
            if (number < book.All.Count) Compound(book.All[number]);
            ShowCompoundMenu(target, book);
            return true;
        }
        if (option == BboItemOption.ConsignProceeds)
        {
            int paid = CollectConsignment();
            Send(ChatPackets.NpcDialogue(target,
            [
                (DialogueEntry.Text, 0, paid > 0
                    ? "Everything that was owed to you from the consignment market has been handed over."
                    : "There is nothing waiting for you at the consignment market."),
                (DialogueEntry.Option, BboItemOption.Leave, "Goodbye"),
            ]), "(consignment desk)");
            return true;
        }
        CloseCompound();
        CloseNpcDialogue();
        return true;
    }

    public void OpenCompound()
    {
        if (_compoundOpen) return;
        _compoundOpen = true;
        _compoundShown.Clear();
        Send(ViewPackets.Create(CompoundView, 2), "(combination view)");
    }

    public void CloseCompound()
    {
        if (!_compoundOpen) return;
        _compoundOpen = false;
        _compoundShown.Clear();
        Send(ViewPackets.Delete(CompoundView), "(close combination view)");
    }

    public void CompoundSlot(int index)
    {
        if (!_compoundOpen) return;
        if (Hand != null && index == 1 && HandSource() is { } from)
        {
            DropHand();
            CompoundPut(from.View, from.Index);
            return;
        }
        DropHand();
        ShowCompoundItem(index, null);
    }

    public void CompoundPut(int view, int index)
    {
        if (!_compoundOpen || MailBagOf(view) is not { } bag || !bag.TryGetValue(index, out ItemStack? item)) return;
        ShowCompoundItem(1, item);
    }

    public void StartCompound(string key)
    {
        CompoundRecipe? recipe = CompoundBook.For(Context.Settings).Find(key);
        if (recipe == null)
        {
            SystemMessage("Nobody here knows how to make that combination.", SystemMessageKind.Prompt);
            Log.Info(Tag, $"{Name} asks for unknown combination {key}");
            return;
        }
        Compound(recipe);
    }

    public bool Compound(CompoundRecipe recipe)
    {
        foreach (CompoundIngredient ingredient in recipe.Ingredients)
        {
            if (BagTotal(ingredient.Name) >= ingredient.Amount) continue;
            SystemMessage($"You need {ingredient.Amount} x {ingredient.Name} for this combination.", SystemMessageKind.Prompt);
            return false;
        }
        if (Money < recipe.Fee)
        {
            SystemMessage($"This combination costs {recipe.Fee} silver coins.", SystemMessageKind.Prompt);
            return false;
        }
        var taken = new List<ItemStack>();
        foreach (CompoundIngredient ingredient in recipe.Ingredients) taken.AddRange(TakeFromBags(ingredient.Name, ingredient.Amount));
        var result = new ItemStack { Name = recipe.Result, Photo = Context.Data.PhotoOf(recipe.Result), Amount = recipe.Amount };
        if (!GiveItem(result.Clone(), "(combined article)"))
        {
            foreach (ItemStack back in taken) GiveItem(back, "(combination refunded)");
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return false;
        }
        SetMoney(Money - recipe.Fee);
        if (_compoundOpen)
        {
            ShowCompoundItem(1, null);
            ShowCompoundItem(2, result);
        }
        RefreshLoad();
        Save();
        SystemMessage("The combination succeeded.");
        Log.Info(Tag, $"{Name} combines {recipe.Key} into {recipe.Amount} x {recipe.Result} for {recipe.Fee}");
        return true;
    }

    private void ShowCompoundMenu(int target, CompoundBook book)
    {
        var entries = new List<(DialogueEntry, int, string)>
        {
            (DialogueEntry.Text, 0, book.All.Count == 0
                ? "I have no combinations to offer."
                : "Bring the articles and the fee, then choose what to make."),
        };
        int count = Math.Min(book.All.Count, BboItemOption.LastRecipe - BboItemOption.FirstRecipe + 1);
        for (int i = 0; i < count; i++)
        {
            CompoundRecipe recipe = book.All[i];
            string text = recipe.Title.Length > 0 ? recipe.Title : $"{recipe.Amount} x {recipe.Result} ({recipe.Fee} silver)";
            entries.Add((DialogueEntry.Option, BboItemOption.FirstRecipe + i, text));
        }
        entries.Add((DialogueEntry.Option, BboItemOption.Leave, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(combination menu)");
    }

    private void ShowCompoundItem(int index, ItemStack? item)
    {
        if (_compoundShown.Remove(index)) Send(ViewPackets.RemoveItem(CompoundView, index), "(combination slot)");
        if (item == null) return;
        _compoundShown.Add(index);
        Send(ViewPackets.AddItem(CompoundView, index, ItemProperties(item, index)), "(combination slot)");
    }

    private void ShowBuyBack()
    {
        if (!_buyBackOpen) return;
        for (int slot = 1; slot <= _buyBackShown; slot++) Send(ViewPackets.RemoveItem(ShopBuyBackView, slot), "(buy-back slot)");
        _buyBackShown = _buyBack.Count;
        for (int i = 0; i < _buyBack.Count; i++)
            Send(ViewPackets.AddItem(ShopBuyBackView, i + 1, ItemProperties(_buyBack[i].Item, i + 1, _buyBack[i].Unit)), "(buy-back item)");
    }

    private (int View, int Index)? HandSource()
    {
        if (Hand == null) return null;
        if (_handFrom is { } from && MailBagOf(from.View) is { } bag && bag.TryGetValue(from.Index, out ItemStack? held)
            && held.Name == Hand.Name && held.Amount >= Hand.Amount)
            return from;
        foreach (int view in BboBagViews)
        {
            foreach ((int slot, ItemStack stack) in MailBagOf(view)!.OrderBy(pair => pair.Key))
            {
                if (stack.Name == Hand.Name && stack.Amount >= Hand.Amount) return (view, slot);
            }
        }
        return null;
    }

    private void MoveStack(int view, int from, int to, int amount)
    {
        Dictionary<int, ItemStack> bag = MailBagOf(view)!;
        if (to < 1 || to > ViewId.BagSlots || !bag.TryGetValue(from, out ItemStack? source)) return;
        amount = Math.Clamp(amount, 1, source.Amount);
        if (bag.TryGetValue(to, out ItemStack? target))
        {
            if (target.Name == source.Name && Stackable(source.Name))
            {
                target.Amount += amount;
                Send(ViewPackets.UpdateItem(view, to, new PropertyBag { ["Amount"] = target.Amount }), "(stacks merged)");
                TakeFrom(view, from, amount, "(stacks merged)");
            }
            else if (amount == source.Amount)
            {
                bag[from] = target;
                bag[to] = source;
                Send(ViewPackets.RemoveItem(view, from), "(articles swapped)");
                Send(ViewPackets.RemoveItem(view, to), "(articles swapped)");
                ShowBagSlot(view, from);
                ShowBagSlot(view, to);
            }
            else
            {
                return;
            }
        }
        else if (amount == source.Amount)
        {
            bag.Remove(from);
            bag[to] = source;
            Send(ViewPackets.RemoveItem(view, from), "(article moved)");
            ShowBagSlot(view, to);
        }
        else
        {
            source.Amount -= amount;
            Send(ViewPackets.UpdateItem(view, from, new PropertyBag { ["Amount"] = source.Amount }), "(stack split)");
            bag[to] = new ItemStack { Name = source.Name, Photo = source.Photo, Amount = amount, Cost = source.Cost };
            ShowBagSlot(view, to);
        }
        Save();
    }

    private void ShowBagSlot(int view, int slot)
    {
        Send(ViewPackets.AddItem(view, slot, ItemProperties(MailBagOf(view)![slot], slot)), "(bag slot)");
        if (view == ViewId.BagEquip) RefreshDurability(view, slot);
        PriceOwnItem(view, slot);
    }

    private void MoveToHold(int view, int index, int amount)
    {
        if (MailBagOf(view) is not { } bag || !bag.TryGetValue(index, out ItemStack? item)) return;
        if (!InHold(item))
        {
            SystemMessage("Only trade goods and provisions can be stowed in the ship's hold.", SystemMessageKind.Prompt);
            return;
        }
        amount = Math.Clamp(amount, 1, item.Amount);
        var moved = new ItemStack { Name = item.Name, Photo = item.Photo, Amount = amount, Cost = item.Cost };
        if (!StowInHold(moved, "(stowed in the hold)"))
        {
            SystemMessage("The hold is full.", SystemMessageKind.Prompt);
            return;
        }
        TakeFrom(view, index, amount, "(stowed in the hold)");
        RefreshLoad();
        Save();
    }

    private bool StowInHold(ItemStack item, string note)
    {
        int slot = Cargo.FirstOrDefault(pair => pair.Value.Name == item.Name).Key;
        if (slot != 0)
        {
            ItemStack stack = Cargo[slot];
            stack.Cost = (int)(((long)stack.Cost * stack.Amount + (long)item.Cost * item.Amount) / Math.Max(1, stack.Amount + item.Amount));
            stack.Amount += item.Amount;
            Send(ViewPackets.UpdateItem(ViewId.Cargo, slot, new PropertyBag { ["Amount"] = stack.Amount, ["Cost"] = stack.Cost }), note);
            return true;
        }
        slot = FreeSlot(Cargo, ViewId.CargoSlots);
        if (slot == 0) return false;
        Cargo[slot] = item;
        Send(ViewPackets.AddItem(ViewId.Cargo, slot, ItemProperties(item, slot)), note);
        return true;
    }

    private bool GiveItem(ItemStack item, string note)
    {
        if (item.Amount <= 0) return true;
        if (TradeGoods(item.Name) || item.Name == ProvisionItem) return StowInHold(item, note);
        if (Stackable(item.Name)) return MailStore(ViewId.BagItems, item, note);
        Dictionary<int, ItemStack> gear = Bags[ViewId.BagEquip];
        if (Enumerable.Range(1, ViewId.BagSlots).Count(slot => !gear.ContainsKey(slot)) < item.Amount) return false;
        for (int i = 0; i < item.Amount; i++)
        {
            ItemStack one = item.Clone();
            one.Amount = 1;
            MailStore(ViewId.BagEquip, one, note);
        }
        return true;
    }

    private int BagTotal(string name)
        => BboBagViews.Sum(view => MailBagOf(view)!.Values.Where(stack => stack.Name == name).Sum(stack => stack.Amount));

    private List<ItemStack> TakeFromBags(string name, int count)
    {
        var taken = new List<ItemStack>();
        foreach (int view in BboBagViews)
        {
            foreach (int slot in MailBagOf(view)!.Where(pair => pair.Value.Name == name).Select(pair => pair.Key).Order().ToList())
            {
                if (count <= 0) return taken;
                ItemStack stack = MailBagOf(view)![slot];
                int amount = Math.Min(count, stack.Amount);
                taken.Add(new ItemStack { Name = stack.Name, Photo = stack.Photo, Amount = amount, Cost = stack.Cost });
                TakeFrom(view, slot, amount, "(used in a combination)");
                count -= amount;
            }
        }
        return taken;
    }

    private static bool Stackable(string name) => ItemCatalog.Number(name, "ItemType") != SeaPackets.ItemTypeEquipment;
}
