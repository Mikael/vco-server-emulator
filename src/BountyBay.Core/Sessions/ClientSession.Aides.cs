using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class AideOption
{
    public const int First = 300;
    public const int Recruit = 300;
    public const int Last = 319;
}

public sealed partial class ClientSession
{
    public const int AideListView = 52;
    public const int AideDeployView = 53;

    private const int AideDeployCapacity = 21;
    private const int AideBlock = 7;
    private const int AideEquipSlots = 5;
    private const int AideSuits = 2;
    private const int AideStatScale = 100;
    private const int AideSpecialPercent = 50;

    private static readonly int[] AideHeads = [1, 8, 15];
    private static readonly string[] AidePostNames = ["land", "sea", "special"];

    private readonly HashSet<(int View, int Index)> _aideShown = [];
    private readonly Dictionary<int, int[]> _aideTraining = [];
    private readonly Dictionary<string, DateTime> _aideSkillUsed = [];
    private bool _aideViewsOpen;
    private int _aideListCapacity;
    private int _aidePicked;

    private AideBook AideRules => AideBook.For(Context.Settings);

    private int AideSlotCount => Math.Clamp(Character.AideSlots, AideRules.StartSlots, AideRules.MaxSlots);

    public void EnterWorldAides()
    {
        _aideShown.Clear();
        _aideTraining.Clear();
        _aideViewsOpen = false;
        _aidePicked = 0;
        if (Character.AideSlots < AideRules.StartSlots) Character.AideSlots = AideRules.StartSlots;
        foreach (AideRecord aide in Character.Aides.Where(aide => aide.Stats.Length != AideBook.Stats))
            aide.Stats = [.. aide.Stats.Concat(new int[AideBook.Stats]).Take(AideBook.Stats)];
        SendAideViews();
    }

    public (int Attack, int Defence, float SailorDamage, float SailorDefence) AideBonus()
    {
        int attack = 0, defence = 0;
        float sailorDamage = 0f, sailorDefence = 0f;
        foreach (AideRecord aide in Character.Aides.Where(aide => aide.Post >= 0))
        {
            int land = aide.Post switch { 0 => 100, 2 => AideSpecialPercent, _ => 0 };
            int sea = aide.Post switch { 1 => 100, 2 => AideSpecialPercent, _ => 0 };
            attack += aide.Stats[0] * land / 100 / AideStatScale;
            defence += aide.Stats[1] * land / 100 / AideStatScale;
            sailorDamage += aide.Stats[2] * sea / 100f / AideStatScale;
            sailorDefence += aide.Stats[3] * sea / 100f / AideStatScale;
        }
        return (attack, defence, sailorDamage, sailorDefence);
    }

    public bool HandleAideOption(int target, int option)
    {
        if (option is < AideOption.First or > AideOption.Last) return false;
        if (option == AideOption.Recruit)
        {
            Send(ChatPackets.NpcDialogue(target, [(DialogueEntry.Text, 0, "These Generals are looking for a captain. Choose one you like.")]), "(recruit dialogue)");
            OpenAideRecruit();
            return true;
        }
        Send(ChatPackets.NpcDialogue(target, [(DialogueEntry.Text, 0, $"Fair winds, {Name}.")]), "(npc dialogue)");
        return true;
    }

    public void OpenAideRecruit()
    {
        if (Character.AideCandidates.Count == 0 || Character.AideCandidates.All(candidate => candidate.Taken)) RollAideCandidates();
        SendAideCandidates();
        Send(AidePackets.ShowRecruitList(), "(general recruit window)");
    }

    public void RefreshAideRecruit()
    {
        int cost = Math.Max(0, AideRules.Data.RefreshCost);
        if (cost > 0 && Money < cost)
        {
            SystemMessage($"Refreshing the list costs {cost:N0} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        if (cost > 0) SetMoney(Money - cost);
        RollAideCandidates();
        SendAideCandidates();
        Log.Info(Tag, $"{Name} refreshes the general recruit list");
    }

    public void RecruitAide(int row, int code, string name)
    {
        AideCandidate? candidate = Character.AideCandidates.FirstOrDefault(entry => entry.Code == code && !entry.Taken);
        if (candidate == null)
        {
            SystemMessage("This General has already left. Refresh the list.", SystemMessageKind.Prompt);
            return;
        }
        int slot = Enumerable.Range(1, AideSlotCount).FirstOrDefault(number => Character.Aides.All(entry => entry.Slot != number));
        if (slot == 0)
        {
            SystemMessage("Your General Rest Room is full. Unlock another rest slot first.", SystemMessageKind.Prompt);
            return;
        }
        if (Money < candidate.Price)
        {
            SystemMessage($"Recruiting this General costs {candidate.Price:N0} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        SetMoney(Money - candidate.Price);
        var aide = new AideRecord
        {
            Slot = slot,
            Code = candidate.Code,
            Name = AideRules.NameOf(candidate.Code),
            Quality = Math.Clamp(candidate.Quality, 1, 3),
            Level = 1,
            Photo = AideRules.RandomPhoto(),
            Skill = candidate.Skill,
        };
        for (int stat = 0; stat < AideBook.Stats; stat++)
        {
            int top = AideRules.Maximum(1, stat) * AideRules.StartPercent(aide.Quality) / 100;
            aide.Stats[stat] = Random.Shared.Next(top / 2, top + 1);
        }
        Character.Aides.Add(aide);
        candidate.Taken = true;
        Save();
        Send(AidePackets.RecruitItem(candidate.Code, candidate.Skill, candidate.Quality, candidate.Price, true), "(general recruited)");
        SendAideViews();
        Send(AidePackets.NewAide(), "(new general)");
        SystemMessage($"{aide.Name} joins you as a General.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} recruits general {aide.Name} (row {row}, quality {aide.Quality}, {candidate.Price} silver, client name '{name}')");
    }

    public void OpenAideDialog() => SendAideViews();

    public void CloseAideDialog() => _aidePicked = 0;

    public void RefreshAide(int index)
    {
        if (DeployedAt(index) is not { } aide) return;
        ShowAideItem(AideDeployView, index, AideProperties(aide, index));
    }

    public bool AideSelect(int view, int how, int index)
    {
        if (view == AideListView)
        {
            AideRecord? listed = Character.Aides.FirstOrDefault(aide => aide.Slot == index);
            if (how == 0)
            {
                _aidePicked = listed?.Slot ?? 0;
                return true;
            }
            if (listed == null) return true;
            if (listed.Post >= 0) PostAide(listed, -1);
            else
            {
                int open = Enumerable.Range(0, AideHeads.Length).FirstOrDefault(free => DeployedIn(free) == null, -1);
                if (open < 0) SystemMessage("All General posts are taken. Return a General to the list first.", SystemMessageKind.Prompt);
                else PostAide(listed, open);
            }
            return true;
        }
        if (view != AideDeployView) return false;
        int head = Array.IndexOf(AideHeads, index);
        if (head >= 0)
        {
            if (how == 0 && _aidePicked > 0 && Character.Aides.FirstOrDefault(aide => aide.Slot == _aidePicked) is { } picked)
            {
                _aidePicked = 0;
                PostAide(picked, head);
            }
            else if (how == 2 && DeployedIn(head) is { } posted) PostAide(posted, -1);
            return true;
        }
        int post = (index - 1) / AideBlock;
        int equip = (index - 1) % AideBlock - 1;
        if (post < 0 || post >= AideHeads.Length || equip < 0 || equip >= AideEquipSlots) return true;
        if (how == 2) UnequipAide(post, equip);
        else if (Hand != null) EquipAideFromHand(post, equip);
        return true;
    }

    public bool AideExpandCapacity(int slot)
    {
        if (slot <= AideSlotCount)
        {
            SystemMessage("This rest slot is already open.", SystemMessageKind.Prompt);
            return true;
        }
        if (slot != AideSlotCount + 1)
        {
            SystemMessage("Please unlock the front lock first!", SystemMessageKind.Prompt);
            return true;
        }
        if (slot > AideRules.MaxSlots) return true;
        int cost = Math.Max(0, AideRules.UnlockCost(slot));
        if (Money < cost)
        {
            SystemMessage($"Opening this rest slot costs {cost:N0} silver coins.", SystemMessageKind.Prompt);
            return true;
        }
        SetMoney(Money - cost);
        Character.AideSlots = slot;
        Save();
        SendAideViews();
        SystemMessage($"General rest slot {slot} is open.", SystemMessageKind.Upgrade);
        return true;
    }

    public void TrainAide(int index, int type)
    {
        if (DeployedAt(index) is not { } aide)
        {
            AideTrainFailed("Choose a General on a post first.");
            return;
        }
        if (!AideRules.TypeAllowed(aide.Level, type))
        {
            AideTrainFailed("This development mode is not available at the General's level.");
            return;
        }
        AideTrainCost? cost = AideRules.Cost(type);
        if (cost == null)
        {
            AideTrainFailed("Unknown development mode.");
            return;
        }
        if (cost.Silver > 0 && Money < cost.Silver)
        {
            AideTrainFailed($"This development costs {cost.Silver:N0} silver coins.");
            return;
        }
        if (cost.Item.Length > 0 && cost.Amount > 0 && BagTotal(cost.Item) < cost.Amount)
        {
            AideTrainFailed($"This development needs {cost.Amount} {AideItemLabel(cost.Item)}.");
            return;
        }
        if (cost.Silver > 0) SetMoney(Money - cost.Silver);
        if (cost.Item.Length > 0 && cost.Amount > 0) TakeFromBags(cost.Item, cost.Amount);

        (int low, int high) = AideRules.Delta(type);
        var deltas = new int[AideBook.Stats];
        for (int stat = 0; stat < AideBook.Stats; stat++)
        {
            int top = AideRules.Maximum(aide.Level, stat);
            int change = top * Random.Shared.Next(low, high + 1) / 100;
            deltas[stat] = Math.Clamp(aide.Stats[stat] + change, 0, top) - aide.Stats[stat];
        }
        _aideTraining[aide.Slot] = deltas;
        GainAideExp(aide, AideRules.TrainExp(type));
        Save();
        RefreshAideEverywhere(aide);
        Send(AidePackets.TrainResult(deltas), "(general training result)");
        Log.Info(Tag, $"{Name} trains general {aide.Name} (mode {type}): {string.Join(' ', deltas)}");
    }

    public void SaveAideTraining(int index)
    {
        if (DeployedAt(index) is not { } aide || !_aideTraining.Remove(aide.Slot, out int[]? deltas)) return;
        for (int stat = 0; stat < AideBook.Stats; stat++)
            aide.Stats[stat] = Math.Clamp(aide.Stats[stat] + deltas[stat], 0, AideRules.Maximum(aide.Level, stat));
        Save();
        RefreshAideEverywhere(aide);
    }

    public void CancelAideTraining(int index)
    {
        if (DeployedAt(index) is { } aide) _aideTraining.Remove(aide.Slot);
    }

    public void PreviewAideTransfer(int from, int to)
    {
        if (!AideTransferPair(from, to, out _, out _)) return;
        Send(AidePackets.TransferRatio(AideRules.Data.TransferLossPercent), "(general transfer preview)");
    }

    public void TransferAide(int from, int to)
    {
        if (!AideTransferPair(from, to, out AideRecord? source, out AideRecord? target)) return;
        if (source.Equipment.Count > 0)
        {
            SystemMessage("Take off the equipment of the General on the left first.", SystemMessageKind.Prompt);
            return;
        }
        int keep = 100 - Math.Clamp(AideRules.Data.TransferLossPercent, 0, 100);
        target.Level = Math.Clamp(Math.Max(target.Level, source.Level * keep / 100), 1, AideRules.MaxLevel);
        for (int stat = 0; stat < AideBook.Stats; stat++)
            target.Stats[stat] = Math.Min(Math.Max(target.Stats[stat], source.Stats[stat] * keep / 100), AideRules.Maximum(target.Level, stat));
        if (source.Post >= 0) source.Post = -1;
        _aideTraining.Remove(source.Slot);
        _aideTraining.Remove(target.Slot);
        Character.Aides.Remove(source);
        Save();
        SendAideViews();
        SystemMessage($"{source.Name} passed on the experience to {target.Name} and left.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} transfers general {source.Name} into {target.Name}");
    }

    public void UseAideSuit(int index, int suit)
    {
        if (DeployedAt(index) is not { } aide) return;
        if (!aide.Suits.TryGetValue(suit, out Dictionary<int, string>? pieces) || pieces.Count == 0)
        {
            SystemMessage("This suit is empty.", SystemMessageKind.Prompt);
            return;
        }
        int post = aide.Post;
        int missing = 0;
        foreach ((int slot, string item) in pieces.OrderBy(pair => pair.Key))
        {
            if (aide.Equipment.TryGetValue(slot, out ItemStack? worn) && worn.Name == item) continue;
            (int View, int Index)? found = BboBagViews
                .SelectMany(view => MailBagOf(view)!.Where(pair => pair.Value.Name == item).Select(pair => (View: view, Index: pair.Key)))
                .Cast<(int View, int Index)?>().FirstOrDefault();
            if (found is not { } at)
            {
                missing++;
                continue;
            }
            PutOnAide(aide, post, slot, at.View, at.Index);
        }
        if (missing > 0) SystemMessage($"{missing} piece(s) of this suit are not in your bags.", SystemMessageKind.Prompt);
    }

    public void AskAideSuit(int index, int suit)
    {
        if (DeployedAt(index) is not { } aide || !aide.Suits.TryGetValue(suit, out Dictionary<int, string>? pieces)) return;
        foreach ((int slot, string item) in pieces.OrderBy(pair => pair.Key))
            Send(AidePackets.SuitAdded(index, suit, slot, item), "(general suit piece)");
    }

    public void AddAideSuit(int index, int suit, int slot, string item)
    {
        if (DeployedAt(index) is not { } aide || suit is < 0 or >= AideSuits || slot is < 0 or >= AideEquipSlots || item.Length == 0) return;
        if (!aide.Suits.TryGetValue(suit, out Dictionary<int, string>? pieces)) aide.Suits[suit] = pieces = [];
        pieces[slot] = item;
        Save();
        Send(AidePackets.SuitAdded(index, suit, slot, item), "(general suit piece added)");
    }

    public void DeleteAideSuit(int index, int suit, int slot)
    {
        if (DeployedAt(index) is not { } aide || !aide.Suits.TryGetValue(suit, out Dictionary<int, string>? pieces)) return;
        if (!pieces.Remove(slot)) return;
        Save();
        Send(AidePackets.SuitRemoved(index, suit, slot), "(general suit piece removed)");
    }

    public void UseAideSkill(string skill, float? x, float? z)
    {
        AideRecord? aide = Character.Aides.FirstOrDefault(entry => entry.Post >= 0 && entry.Skill == skill);
        if (aide == null)
        {
            SystemMessage("No General on a post knows this skill.", SystemMessageKind.Prompt);
            return;
        }
        DateTime now = DateTime.UtcNow;
        int cooldown = Math.Max(0, AideRules.Data.SkillCooldownSeconds);
        if (_aideSkillUsed.TryGetValue(skill, out DateTime used) && (now - used).TotalSeconds < cooldown)
        {
            SystemMessage($"{AideRules.SkillName(skill)} is not ready yet ({cooldown - (int)(now - used).TotalSeconds} s).", SystemMessageKind.Prompt);
            return;
        }
        _aideSkillUsed[skill] = now;
        SystemMessage($"{aide.Name} uses {AideRules.SkillName(skill)}.");
        Log.Info(Tag, $"{Name}: general {aide.Name} uses {skill}{(x is { } px && z is { } pz ? $" at {px:0.#},{pz:0.#}" : "")}");
    }

    public void AideRebuildNotice(string command)
    {
        SystemMessage("General equipment rebuilding is not available yet.", SystemMessageKind.Prompt);
        Log.Debug(Tag, $"{Name} general rebuild request not handled: {command}");
    }

    private void RollAideCandidates()
    {
        var used = new HashSet<int>(Character.Aides.Select(aide => aide.Code));
        Character.AideCandidates.Clear();
        int count = Math.Clamp(AideRules.Data.Candidates, 1, 60);
        for (int attempt = 0; Character.AideCandidates.Count < count && attempt < count * 20; attempt++)
        {
            int code = AideRules.RandomCode();
            if (!used.Add(code)) continue;
            int quality = AideRules.RandomQuality();
            Character.AideCandidates.Add(new AideCandidate
            {
                Code = code,
                Quality = quality,
                Skill = AideRules.RandomSkill(quality),
                Price = AideRules.Price(quality),
            });
        }
        Save();
    }

    private void SendAideCandidates()
    {
        Send(TablePackets.Clear(AideTables.Recruit), "(clear general recruit list)");
        var rows = Character.AideCandidates
            .Select(candidate => new object[] { candidate.Code, candidate.Skill, candidate.Quality, candidate.Price, candidate.Taken ? 1 : 0 })
            .ToList();
        if (rows.Count > 0) Send(TablePackets.Rows(AideTables.Recruit, rows), "(general recruit list)");
    }

    private void SendAideViews()
    {
        int slots = AideSlotCount;
        if (!_aideViewsOpen)
        {
            _aideViewsOpen = true;
            _aideListCapacity = slots;
            Send(ViewPackets.Create(AideListView, slots), "(general list view)");
            Send(ViewPackets.Create(AideDeployView, AideDeployCapacity), "(general post view)");
        }
        else if (_aideListCapacity != slots)
        {
            _aideListCapacity = slots;
            _aideShown.RemoveWhere(entry => entry.View == AideListView);
            Send(ViewPackets.Delete(AideListView), "(general list view resized)");
            Send(ViewPackets.Create(AideListView, slots), "(general list view)");
        }
        for (int slot = 1; slot <= slots; slot++)
        {
            AideRecord? aide = Character.Aides.FirstOrDefault(entry => entry.Slot == slot);
            ShowAideItem(AideListView, slot, aide == null ? null : AideProperties(aide, slot));
        }
        if (PropertyTable.Has("BoxTag"))
        {
            int deployed = Character.Aides.Where(aide => aide.Post >= 0 && aide.Slot is >= 1 and <= 31)
                .Aggregate(0, (mask, aide) => mask | (1 << (aide.Slot - 1)));
            Send(ViewPackets.Properties(AideListView, new PropertyBag { ["BoxTag"] = deployed }), "(generals on posts)");
        }
        for (int post = 0; post < AideHeads.Length; post++)
        {
            AideRecord? aide = DeployedIn(post);
            int head = AideHeads[post];
            ShowAideItem(AideDeployView, head, aide == null ? null : AideProperties(aide, head));
            for (int equip = 0; equip < AideEquipSlots; equip++)
            {
                ItemStack? item = aide != null && aide.Equipment.TryGetValue(equip, out ItemStack? worn) ? worn : null;
                ShowAideItem(AideDeployView, head + 1 + equip, item == null ? null : AideEquipProperties(item, head + 1 + equip, equip));
            }
        }
    }

    private void ShowAideItem(int view, int index, PropertyBag? bag)
    {
        if (bag == null)
        {
            if (_aideShown.Remove((view, index))) Send(ViewPackets.RemoveItem(view, index), "(general slot empty)");
            return;
        }
        PropertyBag known = SeaPackets.Known(bag);
        if (_aideShown.Add((view, index))) Send(ViewPackets.AddItem(view, index, known), "(general)");
        else Send(ViewPackets.UpdateItem(view, index, known), "(general changed)");
    }

    private PropertyBag AideProperties(AideRecord aide, int index)
    {
        int level = Math.Clamp(aide.Level, 1, AideRules.MaxLevel);
        return new PropertyBag
        {
            ["Name"] = aide.Name,
            ["Index"] = index,
            ["Level"] = level,
            ["Quality"] = aide.Quality,
            ["NowExp"] = aide.Exp,
            ["UpgradeExp"] = AideRules.ExpToLevel(level),
            ["RandomHole"] = 0,
            ["Photo"] = aide.Photo,
            ["AideSkill"] = $"{aide.Skill} 1 0",
            ["AAddAT"] = aide.Stats[0] / AideStatScale,
            ["AAddATMax"] = AideRules.Maximum(level, 0) / AideStatScale,
            ["AAddDF"] = aide.Stats[1] / AideStatScale,
            ["AAddDFMax"] = AideRules.Maximum(level, 1) / AideStatScale,
            ["AAddHurtSailor"] = aide.Stats[2] / (float)AideStatScale,
            ["AAddHSailorMax"] = AideRules.Maximum(level, 2) / (float)AideStatScale,
            ["AAddDDSailor"] = aide.Stats[3] / (float)AideStatScale,
            ["AAddDSailorMax"] = AideRules.Maximum(level, 3) / (float)AideStatScale,
        };
    }

    private static PropertyBag AideEquipProperties(ItemStack item, int index, int equip)
    {
        PropertyBag bag = ItemProperties(item, index);
        bag["SnArmType"] = equip;
        return bag;
    }

    private AideRecord? DeployedIn(int post) => Character.Aides.FirstOrDefault(aide => aide.Post == post);

    private AideRecord? DeployedAt(int index)
    {
        int post = Array.IndexOf(AideHeads, index);
        return post >= 0 ? DeployedIn(post) : null;
    }

    private void PostAide(AideRecord aide, int post)
    {
        if (post >= 0 && DeployedIn(post) is { } occupant && occupant != aide) occupant.Post = -1;
        aide.Post = post;
        Save();
        SendAideViews();
        if (post >= 0) SystemMessage($"{aide.Name} takes the {AidePostNames[post]} post.");
        else SystemMessage($"{aide.Name} returns to the list.");
    }

    private void RefreshAideEverywhere(AideRecord aide)
    {
        ShowAideItem(AideListView, aide.Slot, AideProperties(aide, aide.Slot));
        if (aide.Post >= 0) ShowAideItem(AideDeployView, AideHeads[aide.Post], AideProperties(aide, AideHeads[aide.Post]));
    }

    private void GainAideExp(AideRecord aide, int exp)
    {
        aide.Exp += Math.Max(0, exp);
        while (aide.Level < AideRules.MaxLevel && aide.Exp >= AideRules.ExpToLevel(aide.Level))
        {
            aide.Exp -= AideRules.ExpToLevel(aide.Level);
            aide.Level++;
            SystemMessage($"{aide.Name} reached level {aide.Level}.", SystemMessageKind.Upgrade);
        }
        if (aide.Level >= AideRules.MaxLevel) aide.Exp = Math.Min(aide.Exp, AideRules.ExpToLevel(aide.Level));
    }

    private void AideTrainFailed(string text)
    {
        SystemMessage(text, SystemMessageKind.Prompt);
        Send(AidePackets.TrainFailed(), "(general training failed)");
    }

    private bool AideTransferPair(int from, int to, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AideRecord? source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AideRecord? target)
    {
        source = Character.Aides.FirstOrDefault(aide => aide.Slot == from);
        target = Character.Aides.FirstOrDefault(aide => aide.Slot == to);
        if (from == to)
        {
            SystemMessage("Cannot choose the same General.", SystemMessageKind.Prompt);
            return false;
        }
        if (source == null || target == null)
        {
            SystemMessage("General Information Error.", SystemMessageKind.Prompt);
            return false;
        }
        return true;
    }

    private int AideEquipType(string item)
    {
        int slot = AideRules.EquipSlot(item);
        return slot >= 0 ? slot : ItemCatalog.Number(item, "SnArmType", -1);
    }

    private void EquipAideFromHand(int post, int equip)
    {
        if (DeployedIn(post) is not { } aide || HandSource() is not { } from)
        {
            DropHand();
            return;
        }
        string item = Hand!.Name;
        DropHand();
        if (AideEquipType(item) != equip)
        {
            SystemMessage("This item does not fit this General equipment slot.", SystemMessageKind.Prompt);
            return;
        }
        if (aide.Level < AideRules.EquipLevel(equip))
        {
            SystemMessage($"The General must reach level {AideRules.EquipLevel(equip)} to use this slot.", SystemMessageKind.Prompt);
            return;
        }
        PutOnAide(aide, post, equip, from.View, from.Index);
    }

    private void PutOnAide(AideRecord aide, int post, int equip, int view, int index)
    {
        if (MailBagOf(view) is not { } bag || !bag.TryGetValue(index, out ItemStack? stack)) return;
        ItemStack one = stack.Clone();
        one.Amount = 1;
        if (aide.Equipment.TryGetValue(equip, out ItemStack? old) && !GiveItem(old, "(general equipment returned)"))
        {
            SystemMessage("Your bags are full.", SystemMessageKind.Prompt);
            return;
        }
        TakeFrom(view, index, 1, "(general equipment taken from bag)");
        aide.Equipment[equip] = one;
        Save();
        int at = AideHeads[post] + 1 + equip;
        ShowAideItem(AideDeployView, at, AideEquipProperties(one, at, equip));
    }

    private void UnequipAide(int post, int equip)
    {
        if (DeployedIn(post) is not { } aide || !aide.Equipment.TryGetValue(equip, out ItemStack? worn)) return;
        if (!GiveItem(worn, "(general equipment returned)"))
        {
            SystemMessage("Your bags are full.", SystemMessageKind.Prompt);
            return;
        }
        aide.Equipment.Remove(equip);
        Save();
        ShowAideItem(AideDeployView, AideHeads[post] + 1 + equip, null);
    }

    private static string AideItemLabel(string item)
    {
        string label = ItemCatalog.Text(item, "Name");
        return label.Length > 0 ? label : item;
    }
}
