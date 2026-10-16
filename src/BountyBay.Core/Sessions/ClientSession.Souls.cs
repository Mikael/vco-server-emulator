using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class SoulOption
{
    public const int Menu = 320;
    public const int FirstFuse = 321;
    public const int LastFuse = 328;
    public const int Leave = 339;
}

public sealed partial class ClientSession
{
    public const int SoulView = 70;
    public const int SoulSlots = 8;

    private bool _soulOpen;
    private bool _soulShown;
    private string? _soulItem;
    private int _soulSlot = -1;

    private SoulRules Souls => SoulRules.For(Context.Settings);

    public void OpenSoulView()
    {
        if (!_soulOpen)
        {
            _soulOpen = true;
            Send(ViewPackets.Create(SoulView, 1), "(star soul view)");
        }
        if (_soulItem != null && !OwnsSoulItem(_soulItem)) _soulItem = null;
        ShowSoulBox();
    }

    public void CloseSoulView()
    {
        if (!_soulOpen) return;
        if (SoulChart() is { } chart) DropPendingSouls(chart);
        _soulOpen = false;
        _soulShown = false;
        _soulItem = null;
        _soulSlot = -1;
        Send(ViewPackets.Delete(SoulView), "(close star soul view)");
    }

    public bool SoulBoxPut(int view, int index)
    {
        if (!_soulOpen) return false;
        (int View, int Index)? from = SoulSource(view, index);
        DropHand();
        if (from is not { } source || SoulSlotsOf(source.View) is not { } slots || !slots.TryGetValue(source.Index, out ItemStack? item))
        {
            SystemMessage("Pick up the equipment or the Star Soul first.", SystemMessageKind.Prompt);
            return true;
        }
        if (SoulRules.IsSoul(item.Name)) EmbedSoul(source.View, source.Index, item.Name);
        else PlaceSoulEquipment(item.Name);
        return true;
    }

    public bool SoulBoxSelect(int index)
    {
        if (!_soulOpen) return false;
        DropHand();
        if (_soulItem != null && SoulChart() is { } chart) DropPendingSouls(chart);
        _soulItem = null;
        _soulSlot = -1;
        ShowSoulBoxItem();
        ShowSoulBox();
        return true;
    }

    public void SelectSoulSlot(int slot)
    {
        if (slot < 0 || slot >= SoulSlots) return;
        _soulSlot = slot;
        ShowSoulDetails(slot);
    }

    public void PreviewSoulDown(int slot)
    {
        if (SoulAt(slot) is not { } soul)
        {
            SystemMessage("There is no Star Soul in that slot.", SystemMessageKind.Prompt);
            return;
        }
        _soulSlot = slot;
        ShowSoulDetails(slot);
        Send(SoulPackets.DownInfo(DownYield(soul)), "(star soul disassembly yield)");
    }

    public void UpgradeSoul(int slot, int count)
    {
        if (SoulAt(slot) is not { } soul)
        {
            SystemMessage("There is no Star Soul in that slot.", SystemMessageKind.Prompt);
            return;
        }
        SoulRules rules = Souls;
        if (soul.Level >= rules.MaxLevel)
        {
            SystemMessage("This Star Soul has reached the highest level.", SystemMessageKind.Prompt);
            return;
        }
        count = Math.Clamp(count, 1, Math.Max(1, BagTotal(rules.EssenceItem)));
        if (BagTotal(rules.EssenceItem) < count)
        {
            SystemMessage($"You need {ItemLabel(rules.EssenceItem)} to upgrade a Star Soul.", SystemMessageKind.Prompt);
            return;
        }
        long fee = (long)rules.UpgradeFeePerLevelGuess * soul.Level * count;
        if (Money < fee)
        {
            SystemMessage($"Upgrading costs {fee} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        TakeFromBags(rules.EssenceItem, count);
        SetMoney(Money - fee);
        soul.Essence += count;
        soul.Exp += rules.ExpPerEssenceGuess * count;
        while (soul.Level < rules.MaxLevel && soul.Exp >= rules.ExpToNext(soul.Level))
        {
            soul.Exp -= rules.ExpToNext(soul.Level);
            soul.Level++;
        }
        if (soul.Level >= rules.MaxLevel) soul.Exp = 0;
        RefreshLoad();
        Save();
        Send(SoulPackets.Effect(2), "(star soul upgrade effect)");
        ShowSoulDetails(slot);
        ShowSoulBox();
        Log.Info(Tag, $"{Name} upgrades star soul {soul.Id} in {_soulItem} slot {slot} to level {soul.Level} ({soul.Exp} exp)");
    }

    public void DisassembleSoul(int slot)
    {
        if (SoulChart() is not { } chart || SoulAt(slot) is not { } soul)
        {
            SystemMessage("There is no Star Soul in that slot.", SystemMessageKind.Prompt);
            return;
        }
        List<(string Item, int Amount)> yields = DownYield(soul);
        if (yields.Any(entry => entry.Amount > 0 && RoomFor(entry.Item) < entry.Amount))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        chart.Slots[slot] = null;
        foreach ((string item, int amount) in yields)
        {
            if (amount > 0) GiveItem(new ItemStack { Name = item, Photo = Context.Data.PhotoOf(item), Amount = amount }, "(star soul disassembled)");
        }
        RefreshLoad();
        Save();
        ShowSoulBox();
        ShowSoulDetails(slot);
        SystemMessage($"{Souls.NameOf(soul.Id)} was disassembled.");
        Log.Info(Tag, $"{Name} disassembles star soul {soul.Id} from {_soulItem} slot {slot}");
    }

    public void PreviewSoulAdvance(int slot)
    {
        if (SoulAt(slot) is not { } soul)
        {
            SystemMessage("There is no Star Soul in that slot.", SystemMessageKind.Prompt);
            return;
        }
        _soulSlot = slot;
        SoulRules rules = Souls;
        int quality = SoulRules.Quality(soul.Id);
        int next = Math.Min(quality + 1, SoulRules.Qualities - 1);
        string nextId = SoulRules.SoulId(SoulRules.IsSea(soul.Id), next, SoulRules.Element(soul.Id));
        var nextSoul = new SoulRecord { Id = nextId, Level = soul.Level, Attributes = soul.Attributes };
        Send(SoulPackets.ViewUpQuality(soul.Id, nextId, quality, next, soul.Level, soul.Level, SoulRows(soul), SoulRows(nextSoul),
            quality + 1 < SoulRules.Qualities ? SoulPackets.Materials(AdvanceMaterials(quality), SoulRules.At(rules.AdvanceFee, quality)) : SoulPackets.Materials([], 0)),
            "(star soul advancement)");
    }

    public void AdvanceSoul(int slot)
    {
        if (SoulChart() is not { } chart || SoulAt(slot) is not { } soul)
        {
            SystemMessage("There is no Star Soul in that slot.", SystemMessageKind.Prompt);
            return;
        }
        SoulRules rules = Souls;
        int quality = SoulRules.Quality(soul.Id);
        if (quality + 1 >= SoulRules.Qualities)
        {
            SystemMessage("This Star Soul already has the highest grade.", SystemMessageKind.Prompt);
            return;
        }
        if (quality + 1 > chart.Grade)
        {
            SystemMessage("The grade of the Star Chart is too low for a Star Soul of a higher grade.", SystemMessageKind.Prompt);
            return;
        }
        if (!PaySoulCost(AdvanceMaterials(quality), SoulRules.At(rules.AdvanceFee, quality))) return;
        string old = soul.Id;
        soul.Id = SoulRules.SoulId(SoulRules.IsSea(soul.Id), quality + 1, SoulRules.Element(soul.Id));
        Save();
        Send(SoulPackets.Effect(3), "(star soul advancement effect)");
        PreviewSoulAdvance(slot);
        ShowSoulBox();
        SystemMessage($"{rules.NameOf(old)} advanced to {rules.NameOf(soul.Id)}.");
        Log.Info(Tag, $"{Name} advances star soul {old} to {soul.Id} in {_soulItem} slot {slot}");
    }

    public void PreviewSoulChange(int slot)
    {
        if (SoulAt(slot) is not { } soul)
        {
            SystemMessage("There is no Star Soul in that slot.", SystemMessageKind.Prompt);
            return;
        }
        _soulSlot = slot;
        int quality = SoulRules.Quality(soul.Id);
        Send(SoulPackets.ViewChange(soul.Id, quality, soul.Level, SoulRows(soul),
            SoulPackets.Materials(RebuildMaterials(quality), SoulRules.At(Souls.RebuildFeeGuess, quality))), "(star soul rebuild)");
    }

    public void StartSoulChange(int slot)
    {
        if (SoulAt(slot) is not { } soul)
        {
            SystemMessage("There is no Star Soul in that slot.", SystemMessageKind.Prompt);
            return;
        }
        int quality = SoulRules.Quality(soul.Id);
        if (!PaySoulCost(RebuildMaterials(quality), SoulRules.At(Souls.RebuildFeeGuess, quality))) return;
        soul.Pending = RollAttributes(soul.Id);
        Save();
        Send(SoulPackets.Effect(1), "(star soul rebuild effect)");
        Send(SoulPackets.ChangeResult(SoulRows(soul, soul.Pending)), "(star soul rebuild result)");
        Log.Info(Tag, $"{Name} rebuilds star soul {soul.Id} in {_soulItem} slot {slot}");
    }

    public void SaveSoulChange(int slot)
    {
        if (SoulAt(slot) is not { } soul || soul.Pending == null)
        {
            SystemMessage("There is no rebuilt attribute to keep.", SystemMessageKind.Prompt);
            return;
        }
        soul.Attributes = soul.Pending;
        soul.Pending = null;
        Save();
        Send(SoulPackets.CancelChange(), "(star soul rebuild kept)");
        PreviewSoulChange(slot);
        ShowSoulBox();
    }

    public void CancelSoulChange(int slot)
    {
        if (SoulAt(slot) is { } soul) soul.Pending = null;
        Send(SoulPackets.CancelChange(), "(star soul rebuild dropped)");
    }

    public void CloseSoulChange(bool list)
    {
        if (SoulChart() is { } chart)
        {
            foreach (SoulRecord? soul in chart.Slots)
            {
                if (soul != null) soul.Pending = null;
            }
        }
        Send(list ? SoulPackets.CloseChangeList() : SoulPackets.CloseChange(), "(close star soul rebuild)");
        ShowSoulBox();
    }

    public void FreshSoulChart()
    {
        if (SoulChart(create: true) is not { } chart)
        {
            SystemMessage("Put a weapon, a piece of clothing or a hat on the Star Soul board first.", SystemMessageKind.Prompt);
            return;
        }
        SoulRules rules = Souls;
        if (!PaySoulCost([(rules.ChartItem, 1)], rules.ChartFeeGuess))
        {
            Send(SoulPackets.GiveUpFreshChart(), "(star chart transmutation refused)");
            return;
        }
        int count = Math.Max(1, rules.Charts(chart.Sea));
        chart.PendingChart = Random.Shared.Next(count);
        chart.PendingGrade = RollGrade(rules);
        Save();
        Send(SoulPackets.StartFreshChart(chart.PendingGrade, chart.PendingChart, SkillLevel(chart, chart.PendingGrade)), "(star chart transmutation)");
        Log.Info(Tag, $"{Name} transmutes the star chart of {_soulItem}: chart {chart.PendingChart} grade {chart.PendingGrade}");
    }

    public void SaveSoulChart(bool keep)
    {
        if (SoulChart() is not { } chart || chart.PendingChart < 0)
        {
            Send(SoulPackets.GiveUpFreshChart(), "(no star chart pending)");
            return;
        }
        if (keep)
        {
            chart.Chart = chart.PendingChart;
            chart.Grade = chart.PendingGrade;
        }
        chart.PendingChart = -1;
        chart.PendingGrade = 0;
        Save();
        if (!keep)
        {
            Send(SoulPackets.GiveUpFreshChart(), "(star chart kept)");
            return;
        }
        ShowSoulBoxItem();
        Send(SoulPackets.SaveSkill(chart.Chart, chart.Grade, SkillLevel(chart, chart.Grade)), "(star chart saved)");
        ShowSoulBox();
        SystemMessage($"The Star Chart now carries {SkillOf(chart)}.");
    }

    public void UseSoulSkill(string skill)
    {
        SystemMessage("Star Chart skills have no effect on this server yet.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} uses star soul skill {skill}");
    }

    public void LuckSoulCommand(string[] words)
    {
        if (words[0] != "csCloseLuckSoul") SystemMessage("The Star Board is not available on this server.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} luck soul command: {string.Join(' ', words)}");
    }

    public void BatchSoulChange(string[] words)
    {
        if (words[0] == "csStartCFightSoulList")
            SystemMessage("Batch rebuilding is not available here; use the single rebuild.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} batch star soul rebuild: {string.Join(' ', words)}");
    }

    public IReadOnlyDictionary<string, int> SoulBonuses()
    {
        var bonuses = new Dictionary<string, int>(StringComparer.Ordinal);
        if (Character.Bags == null || !Character.Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn)) return bonuses;
        foreach (string item in worn.Values.Select(stack => stack.Name).Distinct())
        {
            if (!Character.Souls.TryGetValue(item, out SoulChartRecord? chart) || chart.Chart < 0) continue;
            foreach ((string name, int value) in ChartTotals(chart)) bonuses[name] = bonuses.GetValueOrDefault(name) + value;
        }
        return bonuses;
    }

    public bool HandleSoulOption(int target, int option)
    {
        if (option < SoulOption.Menu || option > SoulOption.Leave) return false;
        if (option >= SoulOption.FirstFuse && option <= SoulOption.LastFuse) FuseSoul(option - SoulOption.FirstFuse);
        if (option == SoulOption.Leave)
        {
            Send(ChatPackets.NpcDialogue(target, [(DialogueEntry.Text, 0, $"May the stars guide you, {Name}.")]), "(npc dialogue)");
            return true;
        }
        ShowSoulExchangeMenu(target);
        return true;
    }

    public void ShowSoulExchangeMenu(int target)
    {
        SoulRules rules = Souls;
        var entries = new List<(DialogueEntry, int, string)>
        {
            (DialogueEntry.Text, 0, $"A Basic Star Soul costs {rules.FuseShards} x {ItemLabel(rules.ShardItem)}, "
                + $"{rules.FuseSpoils} x {ItemLabel(rules.SpoilsItem)} and {rules.FuseFee} silver coins. Which one do you want?"),
        };
        for (int element = 0; element < SoulRules.Elements; element++)
        {
            string id = SoulRules.SoulId(false, 0, element);
            entries.Add((DialogueEntry.Option, SoulOption.FirstFuse + element, rules.NameOf(id)));
        }
        entries.Add((DialogueEntry.Option, SoulOption.Leave, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(star soul exchange)");
    }

    private void FuseSoul(int element)
    {
        SoulRules rules = Souls;
        string id = SoulRules.SoulId(false, 0, element);
        if (RoomFor(id) < 1)
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        if (!PaySoulCost([(rules.ShardItem, rules.FuseShards), (rules.SpoilsItem, rules.FuseSpoils)], rules.FuseFee)) return;
        GiveItem(new ItemStack { Name = id, Photo = Context.Data.PhotoOf(id), Amount = 1 }, "(star soul exchanged)");
        RefreshLoad();
        Save();
        SystemMessage($"You received {rules.NameOf(id)}.");
        Log.Info(Tag, $"{Name} exchanges a basic star soul {id}");
    }

    private void PlaceSoulEquipment(string item)
    {
        if (SoulPart(item) < 0)
        {
            SystemMessage("Only weapons, clothes and hats can carry a Star Chart.", SystemMessageKind.Prompt);
            return;
        }
        if (_soulItem != null && SoulChart() is { } previous) DropPendingSouls(previous);
        _soulItem = item;
        _soulSlot = -1;
        ShowSoulBoxItem();
        ShowSoulBox();
    }

    private void EmbedSoul(int view, int index, string id)
    {
        if (SoulChart() is not { } chart || chart.Chart < 0)
        {
            SystemMessage("Star Souls can only be embedded on equipment with a Star Chart.", SystemMessageKind.Prompt);
            return;
        }
        if (SoulRules.IsSea(id) != chart.Sea)
        {
            SystemMessage(chart.Sea ? "This equipment needs a sea-battle Star Soul." : "This equipment needs a land-battle Star Soul.",
                SystemMessageKind.Prompt);
            return;
        }
        if (SoulRules.Quality(id) > chart.Grade)
        {
            SystemMessage("The grade of this Star Soul is higher than the grade of the Star Chart.", SystemMessageKind.Prompt);
            return;
        }
        int slot = _soulSlot >= 0 && _soulSlot < SoulSlots && chart.Slots[_soulSlot] == null
            ? _soulSlot
            : Array.FindIndex(chart.Slots, soul => soul == null);
        if (slot < 0)
        {
            SystemMessage("All Star Soul slots of this equipment are filled.", SystemMessageKind.Prompt);
            return;
        }
        if (OwnSlots(view) != null) TakeFrom(view, index, 1, "(star soul embedded)");
        else TakeFromBags(id, 1);
        chart.Slots[slot] = new SoulRecord { Id = id, Level = 1, Attributes = RollAttributes(id) };
        _soulSlot = slot;
        RefreshLoad();
        Save();
        ShowSoulBox();
        ShowSoulDetails(slot);
        SystemMessage($"{Souls.NameOf(id)} was embedded.");
        Log.Info(Tag, $"{Name} embeds star soul {id} into {_soulItem} slot {slot}");
    }

    private void ShowSoulBoxItem()
    {
        if (!_soulOpen) return;
        if (_soulShown) Send(ViewPackets.RemoveItem(SoulView, 1), "(star soul equipment)");
        _soulShown = _soulItem != null;
        if (_soulItem == null) return;
        var item = new ItemStack { Name = _soulItem, Photo = Context.Data.PhotoOf(_soulItem), Amount = 1 };
        PropertyBag bag = ItemProperties(item, 1);
        int chart = Character.Souls.TryGetValue(_soulItem, out SoulChartRecord? record) ? record.Chart + 1 : 0;
        if (PropertyTable.Has("StartMap")) bag["StartMap"] = chart;
        Send(ViewPackets.AddItem(SoulView, 1, bag), "(star soul equipment)");
    }

    private void ShowSoulBox()
    {
        if (!_soulOpen) return;
        SoulChartRecord? chart = _soulItem != null && Character.Souls.TryGetValue(_soulItem, out SoulChartRecord? record) ? record : null;
        if (chart != null) NormalizeChart(chart);
        string[] slots = Enumerable.Range(0, SoulSlots).Select(slot => chart?.Slots[slot]?.Id ?? SoulPackets.Empty).ToArray();
        int map = chart?.Chart ?? -1;
        Send(SoulPackets.ViewArm(slots, map, chart?.Grade ?? 0, chart == null ? -1 : SkillLevel(chart, chart.Grade)), "(star soul board)");
        Send(SoulPackets.PropertyList(chart == null ? [] : ChartTotals(chart).Select(pair => $"{pair.Key}-{Shown(pair.Key, pair.Value)}").ToList()),
            "(star soul attributes)");
    }

    private void ShowSoulDetails(int slot)
    {
        SoulRules rules = Souls;
        if (SoulAt(slot) is not { } soul)
        {
            Send(SoulPackets.ViewSoul(SoulPackets.Empty, 0, 0, 0, rules.MoneyRow, 1, []), "(star soul details)");
            return;
        }
        int max = soul.Level >= rules.MaxLevel ? Math.Max(1, soul.Exp) : rules.ExpToNext(soul.Level);
        Send(SoulPackets.ViewSoul(soul.Id, soul.Level, SoulRules.Quality(soul.Id), soul.Exp, rules.MoneyRow, max, SoulRows(soul)),
            "(star soul details)");
    }

    private List<string> SoulRows(SoulRecord soul, List<SoulAttributeRecord>? attributes = null)
    {
        SoulRules rules = Souls;
        int quality = SoulRules.Quality(soul.Id);
        return (attributes ?? soul.Attributes).Select(attribute =>
        {
            int value = rules.Value(attribute.Name, soul.Level, quality, attribute.Roll);
            return $"{attribute.Name}-{Shown(attribute.Name, value)}-{rules.Tier(attribute.Name, value)}";
        }).ToList();
    }

    private Dictionary<string, int> ChartTotals(SoulChartRecord chart)
    {
        SoulRules rules = Souls;
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SoulRecord? soul in chart.Slots)
        {
            if (soul == null) continue;
            int quality = SoulRules.Quality(soul.Id);
            foreach (SoulAttributeRecord attribute in soul.Attributes)
                totals[attribute.Name] = totals.GetValueOrDefault(attribute.Name) + rules.Value(attribute.Name, soul.Level, quality, attribute.Roll);
        }
        return totals;
    }

    private string Shown(string attribute, int value)
        => Souls.IsFloat(attribute) ? (value / 100.0).ToString("0.##", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    private List<SoulAttributeRecord> RollAttributes(string id)
    {
        SoulRules rules = Souls;
        int low = SoulRules.At(rules.RollPercentGuess, 0);
        int high = Math.Max(low, SoulRules.At(rules.RollPercentGuess, 1));
        return rules.Pool(id).OrderBy(_ => Random.Shared.Next()).Take(Math.Max(1, rules.AttributesPerSoulGuess))
            .Select(name => new SoulAttributeRecord { Name = name, Roll = Random.Shared.Next(low, high + 1) }).ToList();
    }

    private static int RollGrade(SoulRules rules)
    {
        int total = rules.ChartGradeWeightsGuess.Take(SoulRules.Qualities).Sum();
        if (total <= 0) return 0;
        int roll = Random.Shared.Next(total);
        for (int grade = 0; grade < Math.Min(SoulRules.Qualities, rules.ChartGradeWeightsGuess.Count); grade++)
        {
            roll -= rules.ChartGradeWeightsGuess[grade];
            if (roll < 0) return grade;
        }
        return 0;
    }

    private static int SkillLevel(SoulChartRecord chart, int grade)
    {
        if (chart.Slots.Length < SoulSlots || chart.Slots.Any(soul => soul == null)) return -1;
        return Math.Min(grade, chart.Slots.Min(soul => SoulRules.Quality(soul!.Id)));
    }

    private string SkillOf(SoulChartRecord chart)
    {
        string skill = _soulItem == null ? "" : Souls.SkillName(chart.Sea, chart.Chart, SoulPart(_soulItem));
        return skill.Length > 0 ? $"the skill {skill}" : "a new skill";
    }

    private List<(string Item, int Amount)> DownYield(SoulRecord soul)
    {
        SoulRules rules = Souls;
        return
        [
            (rules.EssenceItem, soul.Essence * rules.DownEssencePercentGuess / 100),
            (rules.ShardItem, SoulRules.At(rules.DownShardsGuess, SoulRules.Quality(soul.Id))),
        ];
    }

    private List<(string Item, int Amount)> AdvanceMaterials(int quality)
    {
        SoulRules rules = Souls;
        return [(rules.ShardItem, SoulRules.At(rules.AdvanceShards, quality)), (rules.SpoilsItem, SoulRules.At(rules.AdvanceSpoils, quality))];
    }

    private List<(string Item, int Amount)> RebuildMaterials(int quality)
        => [(Souls.SpoilsItem, SoulRules.At(Souls.RebuildSpoilsGuess, quality))];

    private bool PaySoulCost(IReadOnlyList<(string Item, int Amount)> items, long fee)
    {
        foreach ((string item, int amount) in items)
        {
            if (amount <= 0 || BagTotal(item) >= amount) continue;
            SystemMessage($"You need {amount} x {ItemLabel(item)}.", SystemMessageKind.Prompt);
            return false;
        }
        if (Money < fee)
        {
            SystemMessage($"This costs {fee} silver coins.", SystemMessageKind.Prompt);
            return false;
        }
        foreach ((string item, int amount) in items)
        {
            if (amount > 0) TakeFromBags(item, amount);
        }
        if (fee > 0) SetMoney(Money - fee);
        RefreshLoad();
        return true;
    }


    private SoulChartRecord? SoulChart(bool create = false)
    {
        if (_soulItem == null || !OwnsSoulItem(_soulItem)) return null;
        if (Character.Souls.TryGetValue(_soulItem, out SoulChartRecord? chart)) return NormalizeChart(chart);
        if (!create) return null;
        chart = new SoulChartRecord { Sea = ItemCatalog.Number(_soulItem, "AddCannonHR") > 0 };
        Character.Souls[_soulItem] = chart;
        return chart;
    }

    private SoulRecord? SoulAt(int slot) => slot >= 0 && slot < SoulSlots && SoulChart() is { } chart ? chart.Slots[slot] : null;

    private static SoulChartRecord NormalizeChart(SoulChartRecord chart)
    {
        if (chart.Slots.Length != SoulSlots)
        {
            var slots = new SoulRecord?[SoulSlots];
            Array.Copy(chart.Slots, slots, Math.Min(SoulSlots, chart.Slots.Length));
            chart.Slots = slots;
        }
        return chart;
    }

    private static void DropPendingSouls(SoulChartRecord chart)
    {
        chart.PendingChart = -1;
        foreach (SoulRecord? soul in chart.Slots)
        {
            if (soul != null) soul.Pending = null;
        }
    }

    private static int SoulPart(string item) => ItemCatalog.Number(item, "ArmType") switch
    {
        1 or 2 or 15 => 0,
        4 => 1,
        5 => 2,
        _ => -1,
    };

    private bool OwnsSoulItem(string item)
        => (Character.Bags?.Values.Any(slots => slots.Values.Any(stack => stack.Name == item)) ?? false)
            || (Character.Items?.Values.Any(stack => stack.Name == item) ?? false);

    private Dictionary<int, ItemStack>? SoulSlotsOf(int view)
        => view == ViewId.BagItems ? Character.Items
            : Character.Bags != null && Character.Bags.TryGetValue(view, out Dictionary<int, ItemStack>? slots) ? slots : null;

    private (int View, int Index)? SoulSource(int view, int index)
    {
        if (SoulSlotsOf(view) is { } slots && slots.ContainsKey(index)) return (view, index);
        return HandSource();
    }
}
