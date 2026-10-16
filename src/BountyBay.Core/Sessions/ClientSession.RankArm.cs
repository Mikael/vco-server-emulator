using System.Text.RegularExpressions;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int NobleAttributeSlots = 4;
    private const int NobleSaveSlots = 5;

    private static readonly Regex NobleKey = new(@"^JWTZ0(\d)(\d)$", RegexOptions.Compiled);

    private bool _nobleWornOpen;
    private bool _temperOpen;
    private ItemStack? _temperItem;

    private RankArmBook NobleBook => RankArmBook.For(Context.Settings);

    public static void RankArmItemProperties(PropertyBag bag, ItemStack item)
    {
        Match match = NobleKey.Match(item.Name);
        if (match.Success)
        {
            if (PropertyTable.Has("RankArmType")) bag["RankArmType"] = int.Parse(match.Groups[2].Value);
            if (PropertyTable.Has("QualityLevel") && !bag.ContainsKey("QualityLevel")) bag["QualityLevel"] = int.Parse(match.Groups[1].Value);
        }
        if (item.Rank is not { } state) return;
        if (PropertyTable.Has("RankPropBit")) bag["RankPropBit"] = state.PropBits;
        if (PropertyTable.Has("RankSaveBit")) bag["RankSaveBit"] = state.SaveBits;
    }

    public int RankArmWornSum(string property)
    {
        RankArmBook book = NobleBook;
        return Character.RankWorn.Values.Where(item => item.Rank != null)
            .SelectMany(item => item.Rank!.Slots)
            .Where(slot => slot != null && NobleProp(slot.Prop) == property)
            .Sum(slot => book.ValueOf(slot!.Prop, slot.Level));
    }

    public void OpenRankArm()
    {
        if (!_nobleWornOpen)
        {
            _nobleWornOpen = true;
            Send(ViewPackets.Create(RankArmPackets.WornView, RankArmPackets.WornSlots), "(nobility equipment view)");
            foreach ((int slot, ItemStack item) in Character.RankWorn.OrderBy(pair => pair.Key))
                Send(ViewPackets.AddItem(RankArmPackets.WornView, slot, ItemProperties(item, slot)), "(nobility equipment)");
        }
        SendNobleTotals();
    }

    public void OpenRankArmUpgrade() => OpenArmRankView();

    public void CloseRankArmUpgrade() => CloseArmRankView();

    public void OpenTemper()
    {
        if (!_temperOpen) Send(ViewPackets.Create(RankArmPackets.TemperView, 1), "(tempering view)");
        _temperOpen = true;
        _temperItem = null;
        SendTemperStuff();
    }

    public void CloseTemper()
    {
        if (!_temperOpen) return;
        _temperOpen = false;
        _temperItem = null;
        Send(ViewPackets.Delete(RankArmPackets.TemperView), "(close tempering view)");
    }

    public void Temper(bool lucky)
    {
        if (TemperTarget() is not { } item) return;
        RankArmBook book = NobleBook;
        RankArmState state = NobleState(item);
        if (BagTotal(book.StoneItem) < book.StonesPerTemper)
        {
            SystemMessage($"Tempering needs {book.StonesPerTemper} Tempered Stones.", SystemMessageKind.Prompt);
            return;
        }
        if (state.Fresh is { } old)
        {
            Character.TemperEssence += book.SplitOf(old.Prop, old.Level);
            state.Fresh = null;
        }
        int rounds = lucky ? Math.Max(1, book.LuckTempers) : 1;
        for (int round = 0; round < rounds && BagTotal(book.StoneItem) >= book.StonesPerTemper; round++)
        {
            TakeFromBags(book.StoneItem, book.StonesPerTemper);
            if (NobleRoll() is not { } rolled) break;
            if (!lucky || rolled.Tier >= book.LuckStopTier)
            {
                state.Fresh = new RankArmAttribute { Prop = rolled.Key };
                break;
            }
            Character.TemperEssence += book.SplitOf(rolled.Key, 0);
        }
        if (state.Fresh is { } fresh) Send(RankArmPackets.Fresh(fresh.Prop, book.ValueOf(fresh.Prop, 0)), "(tempering result)");
        else Send(RankArmPackets.FreshSplit(), "(tempering returned as essences)");
        SendTemperStuff();
        Save();
        Log.Info(Tag, $"{Name} tempers {item.Name}{(lucky ? " ten times" : "")}: {state.Fresh?.Prop ?? "split"}");
    }

    public void SaveTempered(int slot)
    {
        if (TemperTarget() is not { } item) return;
        RankArmState state = NobleState(item);
        if (state.Fresh is not { } fresh || slot is < 1 or > NobleSaveSlots)
        {
            SystemMessage(ArmRebuildText(1), SystemMessageKind.Prompt);
            return;
        }
        if ((state.SaveBits & (1 << (slot - 1))) == 0)
        {
            SystemMessage("This save slot is still locked.", SystemMessageKind.Prompt);
            return;
        }
        if (state.Saves[slot - 1] != null)
        {
            SystemMessage("This save slot is in use. Split the saved attribute first.", SystemMessageKind.Prompt);
            return;
        }
        state.Saves[slot - 1] = fresh;
        state.Fresh = null;
        Send(RankArmPackets.Saves([(slot, fresh.Prop, NobleBook.ValueOf(fresh.Prop, fresh.Level), fresh.Level)]), "(tempered attribute saved)");
        Send(RankArmPackets.HideFresh(), "(tempering result)");
        Save();
    }

    public void SplitTempered(int slot)
    {
        if (TemperTarget() is not { } item) return;
        RankArmState state = NobleState(item);
        RankArmAttribute? split = slot == 0 ? state.Fresh : slot is >= 1 and <= NobleSaveSlots ? state.Saves[slot - 1] : null;
        if (split == null)
        {
            SystemMessage(ArmRebuildText(1), SystemMessageKind.Prompt);
            return;
        }
        int gained = NobleBook.SplitOf(split.Prop, split.Level);
        Character.TemperEssence += gained;
        if (slot == 0)
        {
            state.Fresh = null;
            Send(RankArmPackets.HideFresh(), "(tempering result split)");
        }
        else
        {
            state.Saves[slot - 1] = null;
            Send(RankArmPackets.ClearSave(slot), "(saved attribute split)");
        }
        Send(RankArmPackets.Essence(Character.TemperEssence), "(tempered essences)");
        SystemMessage($"The attribute was split into {gained} Tempered Essences.");
        Save();
    }

    public void UseTempered(int slot, int source)
    {
        if (TemperTarget() is not { } item) return;
        RankArmBook book = NobleBook;
        RankArmState state = NobleState(item);
        RankArmAttribute? chosen = source == 0 ? state.Fresh : source is >= 1 and <= NobleSaveSlots ? state.Saves[source - 1] : null;
        if (chosen == null || slot is < 1 or > NobleAttributeSlots)
        {
            SystemMessage(ArmRebuildText(1), SystemMessageKind.Prompt);
            return;
        }
        if ((state.PropBits & (1 << (slot - 1))) == 0)
        {
            SystemMessage("This attribute slot is not activated.", SystemMessageKind.Prompt);
            return;
        }
        for (int other = 0; other < NobleAttributeSlots; other++)
        {
            if (other == slot - 1 || state.Slots[other] is not { } taken || NobleProp(taken.Prop) != NobleProp(chosen.Prop)) continue;
            SystemMessage("The same attribute type is already in another attribute slot.", SystemMessageKind.Prompt);
            return;
        }
        if (state.Slots[slot - 1] is { } replaced) Character.TemperEssence += book.SplitOf(replaced.Prop, replaced.Level);
        state.Slots[slot - 1] = chosen;
        if (source == 0)
        {
            state.Fresh = null;
            Send(RankArmPackets.HideFresh(), "(tempering result used)");
        }
        else
        {
            state.Saves[source - 1] = null;
            Send(RankArmPackets.ClearSave(source), "(saved attribute used)");
        }
        Send(RankArmPackets.Slots([NobleSlotLine(slot, chosen)]), "(attribute slot)");
        Send(RankArmPackets.Essence(Character.TemperEssence), "(tempered essences)");
        NobleAfterChange(item);
    }

    public void UpgradeTempered(int slot)
    {
        if (TemperTarget() is not { } item) return;
        RankArmBook book = NobleBook;
        RankArmState state = NobleState(item);
        if (slot is < 1 or > NobleAttributeSlots || state.Slots[slot - 1] is not { } attribute)
        {
            SystemMessage(ArmRebuildText(1), SystemMessageKind.Prompt);
            return;
        }
        int maxLevel = book.Limit(attribute.Prop)?.MaxLevel ?? 0;
        if (attribute.Level >= maxLevel)
        {
            SystemMessage("This attribute has reached the max level.", SystemMessageKind.Prompt);
            return;
        }
        int cost = book.StuffAt(attribute.Level + 1, attribute.Prop)?.Cost ?? 0;
        if (Character.TemperEssence < cost)
        {
            SystemMessage($"The upgrade needs {cost} Tempered Essences.", SystemMessageKind.Prompt);
            return;
        }
        Character.TemperEssence -= cost;
        attribute.Level++;
        Send(RankArmPackets.SlotLevel(slot, attribute.Level, maxLevel, book.ValueOf(attribute.Prop, attribute.Level)), "(attribute upgraded)");
        Send(RankArmPackets.Essence(Character.TemperEssence), "(tempered essences)");
        NobleAfterChange(item);
    }

    public void OpenTemperSave(int slot)
    {
        if (TemperTarget() is not { } item || slot is < 1 or > NobleSaveSlots) return;
        RankArmState state = NobleState(item);
        if ((state.SaveBits & (1 << (slot - 1))) != 0) return;
        int cost = NobleBook.SaveBitSilver;
        if (Money < cost)
        {
            SystemMessage($"Opening a save slot costs {cost} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        SetMoney(Money - cost);
        state.SaveBits |= 1 << (slot - 1);
        NobleAfterChange(item);
        SystemMessage("The save slot has been opened.");
    }

    private void NobleWornSelect(int how, int slot)
    {
        if (slot is < 1 or > RankArmPackets.WornSlots)
        {
            DropHand();
            return;
        }
        Dictionary<int, ItemStack> worn = Character.RankWorn;
        if (Hand != null && how == 0)
        {
            (int View, int Index)? from = HandSource();
            ItemStack? item = RecastFromHand();
            if (item == null || from is not { } source) return;
            Match match = NobleKey.Match(item.Name);
            if (!match.Success || int.Parse(match.Groups[2].Value) != slot)
            {
                SystemMessage("This piece of equipment does not fit this nobility slot.", SystemMessageKind.Prompt);
                return;
            }
            NobleState(item);
            Dictionary<int, ItemStack> bag = RecastSlots(source.View)!;
            bag.Remove(source.Index);
            Send(ViewPackets.RemoveItem(source.View, source.Index), "(nobility equipment worn)");
            if (worn.Remove(slot, out ItemStack? old))
            {
                bag[source.Index] = old;
                Send(ViewPackets.RemoveItem(RankArmPackets.WornView, slot), "(nobility equipment swapped)");
                Send(ViewPackets.AddItem(source.View, source.Index, ItemProperties(old, source.Index)), "(nobility equipment swapped)");
            }
            worn[slot] = item;
            Send(ViewPackets.AddItem(RankArmPackets.WornView, slot, ItemProperties(item, slot)), "(nobility equipment worn)");
        }
        else
        {
            DropHand();
            if (!worn.TryGetValue(slot, out ItemStack? item)) return;
            Dictionary<int, ItemStack> gear = Bags[ViewId.BagEquip];
            int free = FreeSlot(gear, ViewId.BagSlots);
            if (free == 0)
            {
                SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
                return;
            }
            worn.Remove(slot);
            gear[free] = item;
            Send(ViewPackets.RemoveItem(RankArmPackets.WornView, slot), "(nobility equipment taken off)");
            Send(ViewPackets.AddItem(ViewId.BagEquip, free, ItemProperties(item, free)), "(nobility equipment taken off)");
        }
        RefreshStats();
        SendNobleTotals();
        Save();
    }

    private void TemperSlot(int how)
    {
        if (Hand != null && how == 0)
        {
            if (RecastFromHand() is { } item) TemperPlace(item);
            return;
        }
        DropHand();
        if (_temperItem == null) return;
        _temperItem = null;
        Send(ViewPackets.RemoveItem(RankArmPackets.TemperView, 1), "(tempering slot emptied)");
    }

    private void TemperPlace(ItemStack item)
    {
        if (!NobleKey.IsMatch(item.Name))
        {
            SystemMessage("Only Nobility equipment can be tempered.", SystemMessageKind.Prompt);
            return;
        }
        if (_temperItem != null) Send(ViewPackets.RemoveItem(RankArmPackets.TemperView, 1), "(tempering slot)");
        _temperItem = item;
        RankArmState state = NobleState(item);
        Send(ViewPackets.AddItem(RankArmPackets.TemperView, 1, ItemProperties(item, 1)), "(tempering slot)");
        List<(int, string, int, int, int)> slots = Enumerable.Range(1, NobleAttributeSlots)
            .Where(slot => state.Slots[slot - 1] != null).Select(slot => NobleSlotLine(slot, state.Slots[slot - 1]!)).ToList();
        if (slots.Count > 0) Send(RankArmPackets.Slots(slots), "(attribute slots)");
        for (int slot = 1; slot <= NobleSaveSlots; slot++)
        {
            if (state.Saves[slot - 1] is { } save)
                Send(RankArmPackets.Saves([(slot, save.Prop, NobleBook.ValueOf(save.Prop, save.Level), save.Level)]), "(saved attribute)");
            else Send(RankArmPackets.ClearSave(slot), "(empty save slot)");
        }
        if (state.Fresh is { } fresh) Send(RankArmPackets.Fresh(fresh.Prop, NobleBook.ValueOf(fresh.Prop, fresh.Level)), "(tempering result)");
        else Send(RankArmPackets.HideFresh(), "(tempering result)");
        SendTemperStuff();
    }

    private ItemStack? TemperTarget()
    {
        if (_temperOpen && _temperItem is { } item && RecastLocate(item) != null) return item;
        SystemMessage("Put the Nobility equipment in the tempering slot first.", SystemMessageKind.Prompt);
        return null;
    }

    private RankArmState NobleState(ItemStack item)
    {
        if (item.Rank is { } existing)
        {
            if (existing.Slots.Length != NobleAttributeSlots) existing.Slots = [.. existing.Slots.Concat(new RankArmAttribute?[NobleAttributeSlots]).Take(NobleAttributeSlots)];
            if (existing.Saves.Length != NobleSaveSlots) existing.Saves = [.. existing.Saves.Concat(new RankArmAttribute?[NobleSaveSlots]).Take(NobleSaveSlots)];
            return existing;
        }
        Match match = NobleKey.Match(item.Name);
        int quality = match.Success ? Math.Clamp(int.Parse(match.Groups[1].Value), 1, NobleAttributeSlots) : 1;
        var state = new RankArmState { PropBits = (1 << quality) - 1, SaveBits = Math.Max(1, NobleBook.InitialSaveBit) };
        item.Rank = state;
        return state;
    }

    private RankArmLimit? NobleRoll()
    {
        List<RankArmLimit> limits = NobleBook.Limits.Where(limit => limit.Weight > 0).ToList();
        int total = limits.Sum(limit => limit.Weight);
        if (total <= 0) return null;
        int roll = Random.Shared.Next(total);
        foreach (RankArmLimit limit in limits)
        {
            if (roll < limit.Weight) return limit;
            roll -= limit.Weight;
        }
        return limits[^1];
    }

    private (int Slot, string Prop, int Value, int Level, int MaxLevel) NobleSlotLine(int slot, RankArmAttribute attribute)
        => (slot, attribute.Prop, NobleBook.ValueOf(attribute.Prop, attribute.Level), attribute.Level, NobleBook.Limit(attribute.Prop)?.MaxLevel ?? 0);

    private void NobleAfterChange(ItemStack item)
    {
        if (_temperOpen && ReferenceEquals(_temperItem, item))
            Send(ViewPackets.UpdateItem(RankArmPackets.TemperView, 1, ItemProperties(item, 1)), "(tempering slot)");
        RecastRefresh(item, "(nobility equipment changed)");
        if (RecastLocate(item) is { View: RankArmPackets.WornView })
        {
            RefreshStats();
            SendNobleTotals();
        }
        Save();
    }

    private void SendTemperStuff()
    {
        RankArmBook book = NobleBook;
        Send(RankArmPackets.Stuff([(book.StoneItem, BagTotal(book.StoneItem), book.StonesPerTemper)]), "(tempering materials)");
        Send(RankArmPackets.Essence(Character.TemperEssence), "(tempered essences)");
    }

    private void SendNobleTotals()
    {
        if (!_nobleWornOpen) return;
        RankArmBook book = NobleBook;
        List<(string Prop, int Value)> totals = Character.RankWorn.Values.Where(item => item.Rank != null)
            .SelectMany(item => item.Rank!.Slots).Where(slot => slot != null)
            .GroupBy(slot => NobleProp(slot!.Prop))
            .Select(group => (group.Key, group.Sum(slot => book.ValueOf(slot!.Prop, slot.Level)))).ToList();
        Send(RankArmPackets.PropList(totals), "(nobility attribute totals)");
    }

    private void ShowNobleExchange(int target)
    {
        RankArmBook book = NobleBook;
        string price = book.ExchangeItem.Length > 0 && book.ExchangeAmount > 0
            ? $"Each piece costs {book.ExchangeAmount} x {Context.Data.Items.Goods.GetValueOrDefault(book.ExchangeItem)?.Label ?? book.ExchangeItem}."
            : "Choose a piece.";
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, $"Baron equipment is exchanged here. {price}") };
        foreach (RankArmItem piece in book.Items.Where(piece => piece.Quality == 1 && piece.Slot is >= 1 and <= RankArmPackets.WornSlots).OrderBy(piece => piece.Slot))
            entries.Add((DialogueEntry.Option, ArmRebuildOption.FirstNobility + piece.Slot - 1, piece.Name.Length > 0 ? piece.Name : piece.Key));
        entries.Add((DialogueEntry.Option, ArmRebuildOption.Leave, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(nobility equipment exchange)");
    }

    private void NobleExchange(int slot)
    {
        RankArmBook book = NobleBook;
        if (book.Items.FirstOrDefault(piece => piece.Quality == 1 && piece.Slot == slot) is not { } piece) return;
        if (book.ExchangeItem.Length > 0 && BagTotal(book.ExchangeItem) < book.ExchangeAmount)
        {
            SystemMessage("You do not have enough materials for the exchange.", SystemMessageKind.Prompt);
            return;
        }
        Dictionary<int, ItemStack> gear = Bags[ViewId.BagEquip];
        int free = FreeSlot(gear, ViewId.BagSlots);
        if (free == 0)
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        if (book.ExchangeItem.Length > 0 && book.ExchangeAmount > 0) TakeFromBags(book.ExchangeItem, book.ExchangeAmount);
        var item = new ItemStack { Name = piece.Key, Photo = Context.Data.PhotoOf(piece.Key) };
        NobleState(item);
        gear[free] = item;
        Send(ViewPackets.AddItem(ViewId.BagEquip, free, ItemProperties(item, free)), "(nobility equipment exchanged)");
        Save();
        Log.Info(Tag, $"{Name} exchanges for {piece.Key}");
    }

    private static string NobleProp(string key)
    {
        int dash = key.IndexOf('-');
        return dash < 0 ? key : key[(dash + 1)..];
    }
}
