using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int TotemOperationView = TotemPackets.OperationView;
    public const int TotemBoxView = TotemPackets.BoxView;
    public const int FirstTotemOption = 340;
    public const int LastTotemOption = 345;

    private const int TotemRedeemOption = 345;
    private const int TotemActiveType = 1;
    private static readonly string[] TotemInheritNames = ["MhDamage", "XyDamage", "MmDamage", "LgDamage", "LhDamage"];
    private static readonly string[] TotemModeNames = ["Appraise", "Advance", "Dismantle", "Skills", "Remodel"];

    private TotemRecord? _totemSlot;
    private bool _totemSlotOpen;
    private int _totemMode;
    private TotemRemodel? _totemRemodel;
    private bool _totemBoxShown;
    private bool _totemBoxFilled;
    private int _totemCharge;
    private long _totemReadyAt;
    private bool _totemSkillsReady;
    private long _totemAttackBoostUntil;
    private long _totemDefendBoostUntil;
    private readonly Dictionary<string, long> _totemCooldowns = [];

    private sealed record TotemRemodel(int GrowRate, int[] Inherit);

    private TotemRules TotemRule => TotemRules.For(Context.Settings);

    public TotemRecord? WornTotem => Character.TotemWorn;

    public bool HandleTotemOption(int target, int option)
    {
        if (option < FirstTotemOption || option > LastTotemOption) return false;
        if (option == TotemRedeemOption)
        {
            RedeemTotem(target);
            return true;
        }
        Send(UiPackets.CloseMenu(), "(close npc menu)");
        OpenTotemOperation(option - FirstTotemOption);
        return true;
    }

    public void ApplyTotemProperties()
    {
        if (PropertyTable.Has("ChargePower")) Properties["ChargePower"] = 0;
    }

    public void EnterWorldTotems()
    {
        if (!_totemBoxShown)
        {
            _totemBoxShown = true;
            Send(ViewPackets.Create(TotemBoxView, 1), "(imprint box view)");
        }
        ShowTotemBox();
        StartTotemSkillTimer();
    }

    public void OpenTotemBox()
    {
        if (!_totemBoxShown) EnterWorldTotems();
        else ShowTotemBox();
        Send(TotemPackets.CombatAttributes(WornTotem is { } worn ? TotemAttributes(worn) : Array.Empty<int>()), "(imprint combat attributes)");
    }

    public bool TotemSelect(int view, int how, int index)
    {
        if (view == TotemOperationView)
        {
            SelectTotemOperationSlot();
            return true;
        }
        if (view == TotemBoxView)
        {
            SelectTotemBoxSlot();
            return true;
        }
        return false;
    }

    public bool TotemUseOnBox(int view, int index, int targetView, int targetIndex)
    {
        if (targetView != TotemOperationView && targetView != TotemBoxView) return false;
        TotemRecord? totem = targetView == TotemOperationView ? _totemSlot : WornTotem;
        if (MailBagOf(view) is not { } bag || !bag.TryGetValue(index, out ItemStack? item)) return true;
        if (totem == null)
        {
            SystemMessage("Put an Imprint into the interface first.", SystemMessageKind.Prompt);
            return true;
        }
        TotemRules rules = TotemRule;
        if (item.Name == rules.PotentialItem)
        {
            if (totem.Points.All(points => points == 0))
            {
                SystemMessage("No potential points have been allocated on this Imprint.", SystemMessageKind.Prompt);
                return true;
            }
            TakeFrom(view, index, 1, "(imprint potential pill used)");
            Array.Clear(totem.Points);
            SystemMessage("All potential points of the Imprint were reset.");
        }
        else if (item.Name == rules.ExpItem)
        {
            AddTotemExp(totem);
            return true;
        }
        else if (rules.Book(item.Name) is { } book && targetView == TotemOperationView)
        {
            TakeFrom(view, index, 1, "(imprint skill book used)");
            LearnTotemSkill(totem, book, false);
        }
        else
        {
            SystemMessage("This item cannot be used on an Imprint here.", SystemMessageKind.Prompt);
            return true;
        }
        RefreshTotem(totem);
        RefreshLoad();
        Save();
        return true;
    }

    public void TotemAppraiseCost() => Send(TotemPackets.AppraiseCost(TotemRule.IdentifySilver), "(imprint appraisal cost)");

    public void AppraiseTotem()
    {
        if (TotemInSlot() is not { } totem) return;
        if (totem.Identified)
        {
            SystemMessage("The growth rate of this Imprint has already been appraised.", SystemMessageKind.Prompt);
            return;
        }
        int cost = TotemRule.IdentifySilver;
        if (Money < cost)
        {
            SystemMessage($"The appraisal costs {cost} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        SetMoney(Money - cost);
        totem.Identified = true;
        RefreshTotem(totem);
        Send(TotemPackets.Appraised(), "(imprint appraised)");
        SystemMessage($"The growth rate of the Imprint is {totem.GrowRate}.");
        Save();
    }

    public void TotemAdvanceNeeds()
    {
        if (_totemSlot is not { } totem || totem.Quality >= TotemRule.LevelCaps.Count)
        {
            Send(TotemPackets.AdvanceNeeds([]), "(imprint advance materials)");
            return;
        }
        Send(TotemPackets.AdvanceNeeds(AdvanceCost(totem)), "(imprint advance materials)");
    }

    public void AdvanceTotem()
    {
        if (TotemInSlot() is not { } totem) return;
        TotemRules rules = TotemRule;
        if (totem.Quality >= rules.LevelCaps.Count)
        {
            SystemMessage("This Imprint already has the highest quality.", SystemMessageKind.Prompt);
            return;
        }
        int cap = rules.Cap(totem.Quality);
        if (totem.Level < cap)
        {
            SystemMessage($"The Imprint must reach level {cap} before it can advance.", SystemMessageKind.Prompt);
            return;
        }
        List<(string Item, int Amount)> cost = AdvanceCost(totem);
        foreach ((string item, int amount) in cost)
        {
            bool enough = item == TotemPackets.SilverKey ? Money >= amount : BagTotal(item) >= amount;
            if (enough) continue;
            SystemMessage($"You need {amount} x {TotemItemLabel(item)} to advance this Imprint.", SystemMessageKind.Prompt);
            return;
        }
        foreach ((string item, int amount) in cost)
        {
            if (item == TotemPackets.SilverKey) SetMoney(Money - amount);
            else TakeFromBags(item, amount);
        }
        totem.Quality++;
        RefreshTotem(totem);
        Send(TotemPackets.Advanced(), "(imprint advanced)");
        SystemMessage($"The Imprint advanced to {QualityName(totem.Quality)} quality. Its level cap is now {rules.Cap(totem.Quality)}.");
        TotemAdvanceNeeds();
        RefreshLoad();
        Save();
    }

    public void TotemDismantleReturn()
    {
        if (_totemSlot is not { } totem) return;
        TotemRules rules = TotemRule;
        Send(TotemPackets.DismantleReturn(rules.DismantleItem, rules.DismantlePerQuality * totem.Quality), "(imprint dismantle return)");
    }

    public void DismantleTotem()
    {
        if (TotemInSlot() is not { } totem) return;
        TotemRules rules = TotemRule;
        int amount = rules.DismantlePerQuality * totem.Quality;
        var returned = new ItemStack { Name = rules.DismantleItem, Photo = Context.Data.PhotoOf(rules.DismantleItem), Amount = amount };
        if (amount > 0 && !GiveItem(returned, "(imprint dismantled)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        _totemSlot = null;
        _totemRemodel = null;
        Send(ViewPackets.RemoveItem(TotemOperationView, 1), "(imprint dismantled)");
        SystemMessage(amount > 0 ? $"The Imprint was dismantled into {amount} x {TotemItemLabel(rules.DismantleItem)}." : "The Imprint was dismantled.");
        Log.Info(Tag, $"{Name} dismantled imprint {totem.Id} ({totem.Item})");
        RefreshLoad();
        Save();
    }

    public void ShowTotemRemodel()
    {
        if (_totemSlot is { } totem) Send(TotemPackets.RemodelAttributes(TotemAttributes(totem)), "(imprint remodel attributes)");
    }

    public void RemodelTotem()
    {
        if (TotemInSlot() is not { } totem) return;
        TotemRules rules = TotemRule;
        if (totem.FuseCount > 0)
        {
            SystemMessage("An Imprint that has been fused cannot be remodelled.", SystemMessageKind.Prompt);
            return;
        }
        if (BagTotal(rules.RemodelItem) < 1)
        {
            SystemMessage($"You need 1 x {TotemItemLabel(rules.RemodelItem)} to remodel the Imprint.", SystemMessageKind.Prompt);
            return;
        }
        TakeFromBags(rules.RemodelItem, 1);
        int[] inherit = RollInherit(totem.Type);
        int grow = RollGrowth();
        _totemRemodel = new TotemRemodel(grow, inherit);
        Send(TotemPackets.RemodelResult(inherit, grow, TotemAttributes(totem, grow, inherit, 0)), "(imprint remodel result)");
        RefreshLoad();
        Save();
    }

    public void SaveTotemRemodel()
    {
        if (_totemSlot is not { } totem || _totemRemodel is not { } remodel)
        {
            SystemMessage("There is no remodel result to save.", SystemMessageKind.Prompt);
            return;
        }
        totem.GrowRate = remodel.GrowRate;
        totem.Inherit = remodel.Inherit;
        totem.Identified = true;
        totem.Level = 0;
        totem.Exp = 0;
        Array.Clear(totem.Points);
        _totemRemodel = null;
        RefreshTotem(totem);
        Send(TotemPackets.RemodelSaved(), "(imprint remodel saved)");
        SystemMessage("The remodelled attributes were saved. The Imprint's level and experience are now 0.");
        Save();
    }

    public void CancelTotemRemodel()
    {
        bool pending = _totemRemodel != null;
        _totemRemodel = null;
        if (pending) Send(TotemPackets.RemodelCancelled(), "(imprint remodel cancelled)");
    }

    public void CloseTotemOperation()
    {
        ReturnTotemSlot("(imprint taken out)");
        _totemRemodel = null;
        if (!_totemSlotOpen) return;
        _totemSlotOpen = false;
        Send(ViewPackets.Delete(TotemOperationView), "(close imprint window)");
    }

    public void AddTotemExp()
    {
        if (WornTotem is not { } worn)
        {
            SystemMessage("Put an Imprint into the Imprint system first.", SystemMessageKind.Prompt);
            return;
        }
        AddTotemExp(worn);
    }

    public void AllocateTotemPoints(IReadOnlyList<int> values)
    {
        if (WornTotem is not { } worn) return;
        TotemRules rules = TotemRule;
        int[] points = (int[])worn.Points.Clone();
        int max = rules.MaxPointsPerProp(worn.Quality);
        for (int i = 0; i + 1 < values.Count; i += 2)
        {
            int prop = values[i];
            int added = values[i + 1];
            if (prop < 0 || prop >= points.Length || added <= 0) continue;
            points[prop] = Math.Min(max, points[prop] + added);
        }
        if (points.Sum() > worn.Level)
        {
            SystemMessage("You do not have that many potential points.", SystemMessageKind.Prompt);
            ShowTotemBox();
            return;
        }
        worn.Points = points;
        ShowTotemBox();
        RefreshStats();
        Save();
    }

    public void ReleaseTotemCharge()
    {
        if (WornTotem is not { } worn || !InWorld || AtSea) return;
        TotemRules rules = TotemRule;
        int stage = rules.Stage(_totemCharge);
        if (stage == 0)
        {
            SystemMessage("The Imprint is not charged yet.", SystemMessageKind.Prompt);
            return;
        }
        if (SkillTarget() is not { } monster)
        {
            SystemMessage("There is no target for the Imprint attack.", SystemMessageKind.Prompt);
            return;
        }
        int[] attributes = TotemAttributes(worn);
        int percent = rules.ChargePercents.Count >= stage ? rules.ChargePercents[stage - 1] : 100;
        MonsterState state = Context.World.Monster(monster);
        int raw = (attributes[0] + attributes[1] + attributes[4] * 10) * percent / 100;
        int dealt = Math.Max(1, raw - MonsterDefence(monster, state) / 2);
        SetTotemCharge(0);
        Context.World.Provoke(monster, this);
        Wound(monster, state, dealt, 0xff40ff);
        Log.Debug(Tag, $"{Name} releases the imprint attack (stage {stage}) for {dealt} on {monster.Name}");
    }

    public void UseTotemSkill(string key)
    {
        if (WornTotem is not { } worn) return;
        TotemSkillEntry? skill = worn.Skills.FirstOrDefault(entry => entry.Key == key && entry.Type == TotemActiveType);
        if (skill == null) return;
        long now = Environment.TickCount64;
        if (now < _totemReadyAt || _totemCooldowns.ContainsKey(key))
        {
            SystemMessage("The Imprint skill is not ready yet.", SystemMessageKind.Prompt);
            return;
        }
        TotemRules rules = TotemRule;
        int seconds = skill.Level > 0 ? 12 : 5;
        switch (key)
        {
            case "TS_hzg":
                _totemAttackBoostUntil = now + seconds * 1000L;
                break;
            case "TS_hzy":
                _totemDefendBoostUntil = now + seconds * 1000L;
                break;
        }
        _totemCooldowns[key] = now + rules.ActiveCooldownSeconds * 1000L;
        Send(TotemPackets.EnableSkill(key, false), "(imprint skill cooling down)");
        string name = rules.Skill(skill.Key, skill.Level)?.Name ?? skill.Key;
        SystemMessage($"You use the Imprint skill {name}.");
    }

    public void TotemCharge(bool attacking)
    {
        if (WornTotem == null) return;
        long now = Environment.TickCount64;
        int gain = 1 + (attacking ? now < _totemAttackBoostUntil ? 1 : 0 : now < _totemDefendBoostUntil ? 1 : 0);
        int charged = Math.Min(TotemRule.ChargeMax, _totemCharge + gain);
        if (charged != _totemCharge) SetTotemCharge(charged);
    }

    public void TickTotems(long now)
    {
        if (WornTotem is not { } worn) return;
        if (!_totemSkillsReady && now >= _totemReadyAt)
        {
            _totemSkillsReady = true;
            foreach (TotemSkillEntry skill in worn.Skills.Where(skill => skill.Type == TotemActiveType && !_totemCooldowns.ContainsKey(skill.Key)))
                Send(TotemPackets.EnableSkill(skill.Key, true), "(imprint skill ready)");
        }
        foreach (string key in _totemCooldowns.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
        {
            _totemCooldowns.Remove(key);
            if (_totemSkillsReady) Send(TotemPackets.EnableSkill(key, true), "(imprint skill ready)");
        }
    }

    public int TotemBonus(string property)
    {
        if (WornTotem is not { } worn) return 0;
        string name = property switch
        {
            "AddMaxHP" => "MaxHP",
            "AddMaxSP" => "MaxSP",
            _ => property,
        };
        TotemRules rules = TotemRule;
        int total = 0;
        for (int i = 0; i < rules.Props.Count && i < worn.Points.Length; i++)
        {
            if (rules.Props[i].Name == name) total += rules.Props[i].ValueAt(worn.Points[i]);
        }
        foreach (TotemSkillEntry skill in worn.Skills)
        {
            if (rules.Skill(skill.Key, skill.Level) is { } definition && definition.Stat == name) total += definition.Value;
        }
        return total;
    }

    private void OpenTotemOperation(int mode)
    {
        ReturnTotemSlot("(imprint taken out)");
        _totemRemodel = null;
        _totemMode = Math.Clamp(mode, 0, TotemPackets.Modes.Length - 1);
        Send(TotemPackets.Mode(_totemMode), "(imprint window mode)");
        if (_totemSlotOpen) Send(ViewPackets.Delete(TotemOperationView), "(imprint window)");
        Send(ViewPackets.Create(TotemOperationView, 1), "(imprint window)");
        _totemSlotOpen = true;
        Log.Debug(Tag, $"{Name} opens the imprint {TotemModeNames[_totemMode]} window");
    }

    private void SelectTotemOperationSlot()
    {
        if (!_totemSlotOpen)
        {
            DropHand();
            return;
        }
        if (Hand != null && HandSource() is { } from)
        {
            string name = Hand.Name;
            DropHand();
            if (TotemRule.TypeOf(name) <= 0)
            {
                SystemMessage("Only an Imprint can be placed here.", SystemMessageKind.Prompt);
                return;
            }
            TotemRecord placed = ClaimTotem(from.View, from.Index);
            if (!ReturnTotemSlot("(imprint swapped out)"))
            {
                StoreTotem(placed, "(imprint put back)");
                return;
            }
            _totemSlot = placed;
            _totemRemodel = null;
            Send(ViewPackets.AddItem(TotemOperationView, 1, TotemItemProperties(_totemSlot, 1)), "(imprint put in)");
            return;
        }
        DropHand();
        ReturnTotemSlot("(imprint taken out)");
    }

    private void SelectTotemBoxSlot()
    {
        if (Hand != null && HandSource() is { } from)
        {
            string name = Hand.Name;
            DropHand();
            if (TotemRule.TypeOf(name) <= 0)
            {
                SystemMessage("Only an Imprint can be placed into the Imprint system.", SystemMessageKind.Prompt);
                return;
            }
            TotemRecord equipped = ClaimTotem(from.View, from.Index);
            if (WornTotem is { } old && !StoreTotem(old, "(imprint swapped out)"))
            {
                StoreTotem(equipped, "(imprint put back)");
                return;
            }
            Character.TotemWorn = equipped;
            SetTotemCharge(0);
            StartTotemSkillTimer();
            ShowTotemBox();
            Send(TotemPackets.CombatAttributes(TotemAttributes(equipped)), "(imprint combat attributes)");
            RefreshStats();
            Save();
            return;
        }
        DropHand();
        if (WornTotem is not { } worn || !StoreTotem(worn, "(imprint taken off)")) return;
        Character.TotemWorn = null;
        SetTotemCharge(0);
        _totemCooldowns.Clear();
        ShowTotemBox();
        Send(TotemPackets.CombatAttributes([]), "(imprint combat attributes)");
        RefreshStats();
        Save();
    }

    private TotemRecord? TotemInSlot()
    {
        if (_totemSlot == null) SystemMessage("Put an Imprint into the interface first.", SystemMessageKind.Prompt);
        return _totemSlot;
    }

    private TotemRecord ClaimTotem(int view, int index)
    {
        string name = MailBagOf(view)![index].Name;
        int owned = BagTotal(name);
        List<TotemRecord> stored = Character.Totems.Where(kept => kept.Item == name).ToList();
        foreach (TotemRecord stale in stored.Skip(owned)) Character.Totems.Remove(stale);
        TotemRecord totem = stored.Take(owned).FirstOrDefault() ?? NewTotem(name);
        Character.Totems.Remove(totem);
        TakeFrom(view, index, 1, "(imprint put in)");
        RefreshLoad();
        Save();
        return totem;
    }

    private bool ReturnTotemSlot(string note)
    {
        if (_totemSlot is not { } totem) return true;
        if (!StoreTotem(totem, note)) return false;
        _totemSlot = null;
        _totemRemodel = null;
        Send(ViewPackets.RemoveItem(TotemOperationView, 1), note);
        return true;
    }

    private bool StoreTotem(TotemRecord totem, string note)
    {
        var item = new ItemStack { Name = totem.Item, Photo = Context.Data.PhotoOf(totem.Item), Amount = 1 };
        if (!GiveItem(item, note))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return false;
        }
        Character.Totems.Add(totem);
        RefreshLoad();
        Save();
        return true;
    }

    private TotemRecord NewTotem(string name)
    {
        int type = Math.Max(1, TotemRule.TypeOf(name));
        var totem = new TotemRecord
        {
            Id = ++Character.TotemSerial,
            Item = name,
            Type = type,
            Side = Random.Shared.Next(1, 3),
            Quality = 1,
            GrowRate = RollGrowth(),
            Inherit = RollInherit(type),
        };
        Log.Info(Tag, $"{Name} gets a new imprint {totem.Id} ({name}, growth {totem.GrowRate})");
        return totem;
    }

    private int RollGrowth()
    {
        List<int[]> ranges = TotemRule.GrowthRanges;
        if (ranges.Count == 0) return 1000;
        int low = ranges[0][0];
        int high = ranges[^1][^1];
        return Math.Min(Random.Shared.Next(low, high + 1), Random.Shared.Next(low, high + 1));
    }

    private int[] RollInherit(int type)
    {
        TotemRules rules = TotemRule;
        var inherit = new int[TotemInheritNames.Length];
        for (int i = 0; i < inherit.Length; i++)
        {
            int[] range = i == type - 1 ? rules.MainInherit : rules.OtherInherit;
            inherit[i] = Math.Min(Random.Shared.Next(range[0], range[1] + 1), Random.Shared.Next(range[0], range[1] + 1));
        }
        return inherit;
    }

    private void AddTotemExp(TotemRecord totem)
    {
        TotemRules rules = TotemRule;
        int cap = rules.Cap(totem.Quality);
        if (totem.Level >= cap)
        {
            SystemMessage(totem.Quality >= rules.LevelCaps.Count
                ? "The Imprint has reached the highest level."
                : $"The Imprint has reached level {cap}. Advance it at the Imprint Growth Commissioner to raise the level cap.", SystemMessageKind.Prompt);
            return;
        }
        int needed = Math.Max(1, rules.ExpFor(totem.Level) - totem.Exp);
        int perItem = Math.Max(1, rules.ExpPerItem);
        int owned = BagTotal(rules.ExpItem);
        int used = Math.Min(owned, (needed + perItem - 1) / perItem);
        if (used <= 0)
        {
            SystemMessage($"You need {TotemItemLabel(rules.ExpItem)} to upgrade the Imprint.", SystemMessageKind.Prompt);
            return;
        }
        TakeFromBags(rules.ExpItem, used);
        totem.Exp += used * perItem;
        if (totem.Exp >= rules.ExpFor(totem.Level))
        {
            totem.Exp = 0;
            totem.Level++;
            SystemMessage($"The Imprint reached level {totem.Level} and gained 1 potential point.", SystemMessageKind.Upgrade);
            RollAutoTotemSkill(totem);
        }
        RefreshTotem(totem);
        RefreshLoad();
        Save();
    }

    private void RollAutoTotemSkill(TotemRecord totem)
    {
        TotemRules rules = TotemRule;
        if (totem.Skills.Count(skill => skill.Auto) >= rules.AutoSkillMax || Random.Shared.Next(100) >= rules.AutoSkillPercent) return;
        List<TotemSkillDefinition> primary = rules.Skills.Where(skill => skill.Level == 0 && totem.Skills.All(known => known.Type != skill.Type)).ToList();
        if (primary.Count == 0) return;
        TotemSkillDefinition learned = primary[Random.Shared.Next(primary.Count)];
        LearnTotemSkill(totem, learned, true);
    }

    private void LearnTotemSkill(TotemRecord totem, TotemSkillDefinition book, bool automatic)
    {
        TotemRules rules = TotemRule;
        var entry = new TotemSkillEntry { Key = book.Key, Level = book.Level, Type = book.Type, Auto = automatic };
        int sameType = totem.Skills.FindIndex(skill => skill.Type == book.Type);
        string replaced = "";
        if (sameType >= 0)
        {
            replaced = totem.Skills[sameType].Key;
            totem.Skills[sameType] = entry;
        }
        else if (automatic)
        {
            totem.Skills.Add(entry);
        }
        else
        {
            int grids = Math.Clamp(totem.Grids, 0, rules.MaxSkills);
            int chance = grids < rules.SkillGridPercents.Count ? rules.SkillGridPercents[grids] : 0;
            List<int> learnedSlots = totem.Skills.Select((skill, i) => (skill, i)).Where(pair => !pair.skill.Auto).Select(pair => pair.i).ToList();
            if (learnedSlots.Count == 0 || (grids < rules.MaxSkills && Random.Shared.Next(100) < chance))
            {
                totem.Grids = grids + 1;
                totem.Skills.Add(entry);
            }
            else
            {
                int slot = learnedSlots[Random.Shared.Next(learnedSlots.Count)];
                replaced = totem.Skills[slot].Key;
                totem.Skills[slot] = entry;
            }
        }
        string name = book.Name.Length > 0 ? book.Name : book.Key;
        SystemMessage(replaced.Length > 0 ? $"The Imprint learned {name}, replacing an existing skill." : $"The Imprint learned {name}.",
            SystemMessageKind.Upgrade);
        if (totem == WornTotem && book.Type == TotemActiveType) Send(TotemPackets.EnableSkill(book.Key, _totemSkillsReady), "(imprint skill)");
    }

    private List<(string Item, int Amount)> AdvanceCost(TotemRecord totem)
    {
        TotemRules rules = TotemRule;
        int step = totem.Quality - 1;
        var cost = new List<(string Item, int Amount)>();
        if (step < rules.AdvanceItemAmounts.Count && rules.AdvanceItem.Length > 0) cost.Add((rules.AdvanceItem, rules.AdvanceItemAmounts[step]));
        if (step < rules.AdvanceSilver.Count) cost.Add((TotemPackets.SilverKey, rules.AdvanceSilver[step]));
        return cost;
    }

    private int[] TotemAttributes(TotemRecord totem) => TotemAttributes(totem, totem.GrowRate, totem.Inherit, totem.Level);

    private int[] TotemAttributes(TotemRecord totem, int growRate, int[] inherit, int level)
    {
        TotemRules rules = TotemRule;
        var values = new int[6];
        for (int i = 0; i < 5; i++)
        {
            long total = (i < inherit.Length ? inherit[i] : 0) + (i < totem.Fused.Length ? totem.Fused[i] : 0);
            long value = total * rules.AttributeBasePercent / 100 + total * growRate * level / Math.Max(1, rules.AttributeGrowthDivisor);
            if (i < 4) values[i] = (int)value;
            else values[4] = values[5] = (int)(value / 10);
        }
        foreach (TotemSkillEntry skill in totem.Skills)
        {
            if (rules.Skill(skill.Key, skill.Level) is { Attribute: >= 0 and < 6 } definition) values[definition.Attribute] += definition.Value;
        }
        return values;
    }

    private PropertyBag TotemItemProperties(TotemRecord totem, int slot)
    {
        TotemRules rules = TotemRule;
        PropertyBag bag = ItemProperties(new ItemStack { Name = totem.Item, Photo = Context.Data.PhotoOf(totem.Item), Amount = 1 }, slot);
        PutTotem(bag, "GUid", $"{totem.Id}-{Name}");
        PutTotem(bag, "XQGUid", totem.Relative);
        PutTotem(bag, "Level", totem.Level);
        PutTotem(bag, "Exp", totem.Exp);
        PutTotem(bag, "GrowRate", totem.Identified ? totem.GrowRate : 0);
        PutTotem(bag, "TotemType", totem.Type);
        PutTotem(bag, "QualityLevel", totem.Quality);
        PutTotem(bag, "Sex", totem.Side);
        PutTotem(bag, "FuseFlag", totem.FuseCount);
        PutTotem(bag, "LevelPoint", Math.Max(0, totem.Level - totem.Points.Sum()));
        PutTotem(bag, "TotemSkill", string.Join('-', totem.Skills.Select(skill => $"{skill.Key} 0 {skill.Level} {skill.Type}")));
        for (int i = 0; i < TotemInheritNames.Length; i++)
        {
            PutTotem(bag, TotemInheritNames[i], i < totem.Inherit.Length ? totem.Inherit[i] : 0);
            PutTotem(bag, TotemInheritNames[i] + "_", i < totem.Fused.Length ? totem.Fused[i] : 0);
        }
        for (int i = 0; i < rules.Props.Count; i++)
            PutTotem(bag, rules.Props[i].Name, rules.Props[i].ValueAt(i < totem.Points.Length ? totem.Points[i] : 0));
        return bag;
    }

    private static void PutTotem(PropertyBag bag, string name, object value)
    {
        if (PropertyTable.Has(name)) bag[name] = value;
    }

    private void RefreshTotem(TotemRecord totem)
    {
        if (totem == _totemSlot) Send(ViewPackets.UpdateItem(TotemOperationView, 1, TotemItemProperties(totem, 1)), "(imprint updated)");
        if (totem == WornTotem)
        {
            ShowTotemBox();
            Send(TotemPackets.CombatAttributes(TotemAttributes(totem)), "(imprint combat attributes)");
            RefreshStats();
        }
    }

    private void ShowTotemBox()
    {
        if (!_totemBoxShown) return;
        if (_totemBoxFilled)
        {
            Send(ViewPackets.RemoveItem(TotemBoxView, 1), "(imprint box)");
            _totemBoxFilled = false;
        }
        if (WornTotem is not { } worn) return;
        Send(ViewPackets.AddItem(TotemBoxView, 1, TotemItemProperties(worn, 1)), "(imprint box)");
        _totemBoxFilled = true;
    }

    private void StartTotemSkillTimer()
    {
        _totemReadyAt = Environment.TickCount64 + TotemRule.ActiveReadySeconds * 1000L;
        _totemSkillsReady = false;
        if (WornTotem is not { } worn) return;
        foreach (TotemSkillEntry skill in worn.Skills.Where(skill => skill.Type == TotemActiveType))
            Send(TotemPackets.EnableSkill(skill.Key, false), "(imprint skill waiting)");
    }

    private void SetTotemCharge(int value)
    {
        _totemCharge = Math.Clamp(value, 0, TotemRule.ChargeMax);
        if (!PropertyTable.Has("ChargePower")) return;
        if (InWorld && !AtSea) SetProperty("ChargePower", _totemCharge, visibleToOthers: true);
        else if (InWorld) SetProperty("ChargePower", _totemCharge);
        else Properties["ChargePower"] = _totemCharge;
    }

    private void RedeemTotem(int target)
    {
        TotemRules rules = TotemRule;
        string text;
        if (rules.Items.Count == 0 || rules.RedeemItem.Length == 0)
        {
            text = "I have no Imprints to give out right now.";
        }
        else if (BagTotal(rules.RedeemItem) < rules.RedeemAmount)
        {
            text = $"Bring me {rules.RedeemAmount} x {TotemItemLabel(rules.RedeemItem)} and I will give you an Imprint.";
        }
        else
        {
            string item = rules.Items[Random.Shared.Next(rules.Items.Count)];
            var imprint = new ItemStack { Name = item, Photo = Context.Data.PhotoOf(item), Amount = 1 };
            if (!GiveItem(imprint, "(imprint redeemed)"))
            {
                text = "Your bag is full.";
            }
            else
            {
                TakeFromBags(rules.RedeemItem, rules.RedeemAmount);
                text = $"Here is your {TotemItemLabel(item)}. Put it into the Imprint system to use it.";
                RefreshLoad();
                Save();
            }
        }
        Send(ChatPackets.NpcDialogue(target, [(DialogueEntry.Text, 0, text), (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye")]), "(imprint redeemer)");
    }

    private string TotemItemLabel(string name)
        => Context.Data.Items.Goods.TryGetValue(name, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : name;

    private static string QualityName(int quality) => quality switch
    {
        1 => "Normal",
        2 => "Rare",
        3 => "Superior",
        4 => "Perfect",
        5 => "Divinity",
        _ => quality.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
