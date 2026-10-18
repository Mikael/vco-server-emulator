using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class ArmRebuildOption
{
    public const int First = 660;
    public const int Recast = 660;
    public const int Record = 661;
    public const int Nobility = 662;
    public const int FirstNobility = 663;
    public const int LastNobility = 669;
    public const int GeneralRebuild = 670;
    public const int Leave = 679;
    public const int Last = 679;
}

public sealed partial class ClientSession
{
    private const int SkillLockType = 4;
    private const int SkillLevels = 5;

    private static readonly string[] RecastGrades =
        ["", "Common Forging", "Skilled Forging", "Excellent Forging", "Royal Forging", "Ultimate Forging"];

    private static readonly Dictionary<string, string> RecastLabels = new()
    {
        ["AddAT"] = "Attack", ["AddHitRatio"] = "Accuracy", ["AddMaxHP"] = "HP", ["AddDF"] = "Defense", ["AddDodgeRatio"] = "Dodge",
    };

    private readonly HashSet<int> _recastLocks = [];
    private bool _recastOpen;
    private ItemStack? _recastItem;
    private int _recastLevel = 1;
    private bool _recordOpen;
    private ItemStack? _recordItem;
    private bool _transferOpen;
    private ItemStack? _transferTool;
    private ItemStack? _transferSource;
    private ItemStack? _transferTarget;
    private bool _transferPreview;

    private ArmRebuildBook RecastBook => ArmRebuildBook.For(Context.Settings);

    public static void ArmRebuildItemProperties(PropertyBag bag, ItemStack item)
    {
        if (item.Rebuild is { Empty: false } state)
        {
            foreach ((string name, int delta) in state.Deltas)
            {
                if (delta == 0 || !PropertyTable.Has(name)) continue;
                int current = bag.TryGetValue(name, out object? value) && value is not string ? Convert.ToInt32(value) : ItemCatalog.Number(item.Name, name);
                bag[name] = Math.Max(0, current + delta);
            }
            if (PropertyTable.Has("ReBuildInfo")) bag["ReBuildInfo"] = RecastInfo(state.Deltas);
            if (PropertyTable.Has("ReBuildSign")) bag["ReBuildSign"] = state.Sign;
            if (state.Skill.Length > 0 && PropertyTable.Has("ReBuildPSkill")) bag["ReBuildPSkill"] = state.Skill;
            if (PropertyTable.Has("Binding")) bag["Binding"] = 1;
        }
        int armType = ItemCatalog.Number(item.Name, "ArmType");
        if (armType > 0 && PropertyTable.Has("ArmType")) bag["ArmType"] = armType;
        RankArmItemProperties(bag, item);
    }

    public static int ArmRebuildBonus(ItemStack item, string property)
        => item.Rebuild?.Deltas.GetValueOrDefault(property) ?? 0;

    public bool HandleArmRebuildOption(int target, int option)
    {
        if (option < ArmRebuildOption.First || option > ArmRebuildOption.Last) return false;
        switch (option)
        {
            case ArmRebuildOption.Recast:
                OpenArmRebuild();
                return true;
            case ArmRebuildOption.Record:
                OpenArmRebuildRecord();
                return true;
            case ArmRebuildOption.Nobility:
                ShowNobleExchange(target);
                return true;
            case ArmRebuildOption.GeneralRebuild:
                CloseNpcDialogue();
                OpenAideArmRebuild();
                return true;
            case >= ArmRebuildOption.FirstNobility and <= ArmRebuildOption.LastNobility:
                NobleExchange(option - ArmRebuildOption.FirstNobility + 1);
                ShowNobleExchange(target);
                return true;
            default:
                CloseNpcDialogue();
                return true;
        }
    }

    public bool ArmRebuildSelect(int view, int how, int index)
    {
        switch (view)
        {
            case ArmRebuildPackets.RebuildView when _recastOpen:
                RecastSlot(how);
                return true;
            case ArmRebuildPackets.RecordView when _recordOpen:
                RecordSlot(how);
                return true;
            case ArmRebuildPackets.TransferView when _transferOpen:
                TransferSlot(how, index);
                return true;
            case RankArmPackets.WornView when _nobleWornOpen:
                NobleWornSelect(how, index);
                return true;
            case RankArmPackets.TemperView when _temperOpen:
                TemperSlot(how);
                return true;
            case AideArmView when _aideArmOpen:
                return AideArmSelect(view, how, index);
            default:
                return false;
        }
    }

    public bool ArmRebuildBoxItem(int view, int index, int targetView, int targetIndex)
    {
        if (targetView is not (ArmRebuildPackets.RebuildView or ArmRebuildPackets.RecordView or ArmRebuildPackets.TransferView
            or RankArmPackets.TemperView)) return false;
        ItemStack? item = RecastItemAt(view, index);
        DropHand();
        if (item == null) return true;
        switch (targetView)
        {
            case ArmRebuildPackets.RebuildView when _recastOpen:
                RecastPlace(item);
                break;
            case ArmRebuildPackets.RecordView when _recordOpen:
                RecordPlace(item);
                break;
            case ArmRebuildPackets.TransferView when _transferOpen:
                TransferPlace(targetIndex, item);
                break;
            case RankArmPackets.TemperView when _temperOpen:
                TemperPlace(item);
                break;
        }
        return true;
    }

    public bool UseArmRebuildItem(ItemStack item)
    {
        if (!RecastBook.TransferItems.Contains(item.Name)) return false;
        OpenArmTransfer(item);
        return true;
    }

    public void ArmTakeOff(int view, int index)
    {
        Send(ArmRebuildPackets.TakeOff(view, index), "(equipment to the open upgrade window)");
        Log.Info(Tag, $"{Name} takes view {view} slot {index} into an upgrade window");
    }

    public void OpenArmRebuild()
    {
        if (!_recastOpen) Send(ViewPackets.Create(ArmRebuildPackets.RebuildView, 1), "(recast view)");
        _recastOpen = true;
        _recastItem = null;
        _recastLevel = 1;
        _recastLocks.Clear();
    }

    public void CloseArmRebuild()
    {
        if (!_recastOpen) return;
        _recastOpen = false;
        _recastItem = null;
        _recastLocks.Clear();
        Send(ViewPackets.Delete(ArmRebuildPackets.RebuildView), "(close recast view)");
    }

    public void SelectRecastLevel(int level)
    {
        _recastLevel = Math.Clamp(level, 1, 4);
        Send(ArmRebuildPackets.Resources(RecastCost(_recastLevel)), "(recast materials)");
    }

    public void LockRecast(IReadOnlyList<int> types)
    {
        _recastLocks.Clear();
        if (types.Count == 0 || types.Contains(0)) return;
        foreach (int type in types.Where(type => type is >= 1 and <= 7).Take(4)) _recastLocks.Add(type);
        Send(ArmRebuildPackets.ExtraResources(RecastLockCost()), "(recast lock materials)");
    }

    public void StartRecast()
    {
        string? problem = RecastProblem(out ItemStack? item, out ArmRebuildLevel? entry);
        if (problem != null || item == null || entry == null)
        {
            Send(ArmRebuildPackets.Animation(false), "(recast refused)");
            SystemMessage(problem ?? "The recast failed.", SystemMessageKind.Prompt);
            return;
        }
        List<(string Key, int Have, int Need)> materials = RecastCost(entry.Level).Concat(RecastLockCost()).ToList();
        foreach ((string key, int _, int need) in materials.Where(line => line.Key != RecastBook.SilverKey))
            TakeFromBags(key, need);
        SetMoney(Money - entry.Silver);

        int armType = ItemCatalog.Number(item.Name, "ArmType");
        ArmRebuildState state = item.Rebuild ??= new ArmRebuildState();
        var lines = new List<string>();
        double quality = 0;
        string[] properties = RecastProperties(armType);
        foreach (string property in properties)
        {
            if (_recastLocks.Contains(RecastType(property, armType)) && state.Deltas.ContainsKey(property))
            {
                quality += 0.5;
                continue;
            }
            int percent = Random.Shared.Next(entry.MinPercent, entry.MaxPercent + 1);
            int magnitude = Math.Max(10, ItemCatalog.Number(item.Name, property));
            state.Deltas[property] = magnitude * percent / 100;
            quality += (double)(percent - entry.MinPercent) / Math.Max(1, entry.MaxPercent - entry.MinPercent);
        }
        quality /= properties.Length;
        state.Sign = Math.Clamp(entry.Level + (quality >= 0.75 ? 1 : 0) - (quality < 0.25 ? 1 : 0), 1, RecastGrades.Length - 1);
        if (!_recastLocks.Contains(SkillLockType) && Random.Shared.Next(100) < entry.SkillPercent)
        {
            IReadOnlyList<string> pool = RecastBook.SkillPool(armType);
            if (pool.Count >= SkillLevels)
            {
                int family = Random.Shared.Next(pool.Count / SkillLevels);
                int level = Random.Shared.Next(1, Math.Clamp(entry.SkillLevelMax, 1, SkillLevels) + 1);
                state.Skill = pool[family * SkillLevels + level - 1];
            }
        }
        state.Level = entry.Level;
        state.Today++;

        foreach (string property in properties)
        {
            int delta = state.Deltas.GetValueOrDefault(property);
            int total = Math.Max(0, ItemCatalog.Number(item.Name, property) + delta);
            lines.Add($"{RecastLabels.GetValueOrDefault(property, property)}: {total} ({delta.ToString("+#;-#;0", CultureInfo.InvariantCulture)})");
        }
        lines.Add($"Grade: {RecastGrades[state.Sign]}");
        if (state.Skill.Length > 0) lines.Add("The equipment carries a special effect.");

        RecastAfterChange(item, "(recast equipment)");
        Send(ArmRebuildPackets.Animation(true), "(recast animation)");
        foreach (string line in lines) Send(ArmRebuildPackets.ResultLine(line), "(recast result)");
        Send(ArmRebuildPackets.Resources(RecastCost(_recastLevel)), "(recast materials)");
        Log.Info(Tag, $"{Name} recasts {item.Name} at level {entry.Level}: {RecastInfo(state.Deltas)} grade {state.Sign} skill '{state.Skill}'");
    }

    public void OpenArmRebuildRecord()
    {
        if (!_recordOpen) Send(ViewPackets.Create(ArmRebuildPackets.RecordView, 1), "(recast record view)");
        _recordOpen = true;
        _recordItem = null;
    }

    public void CloseArmRebuildRecord()
    {
        if (!_recordOpen) return;
        _recordOpen = false;
        _recordItem = null;
        Send(ViewPackets.Delete(ArmRebuildPackets.RecordView), "(close recast record view)");
    }

    public void CopyRecastRecord(int index)
    {
        if (_recordItem is not { } item || RecastLocate(item) == null)
        {
            SystemMessage("Put the recast equipment in the slot first.", SystemMessageKind.Prompt);
            return;
        }
        if (item.Rebuild is not { Empty: false } state)
        {
            SystemMessage("This equipment has no recast attributes to record.", SystemMessageKind.Prompt);
            return;
        }
        List<ArmRebuildRecord> records = Character.ArmRebuildRecords;
        ArmRebuildRecord? record = index > 0 ? records.FirstOrDefault(entry => entry.Index == index) : null;
        if (record == null)
        {
            if (records.Count >= RecastBook.RecordSlots)
            {
                SystemMessage("All record slots are in use. Remove a record first.", SystemMessageKind.Prompt);
                return;
            }
            record = new ArmRebuildRecord { Index = records.Count == 0 ? 1 : records.Max(entry => entry.Index) + 1 };
            records.Add(record);
        }
        record.Item = item.Name;
        record.ArmType = ItemCatalog.Number(item.Name, "ArmType");
        record.Deltas = new Dictionary<string, int>(state.Deltas);
        record.Sign = state.Sign;
        record.Skill = state.Skill;
        SendRecastRecords();
        Save();
        SystemMessage("The recast attributes have been recorded.");
    }

    public void ApplyRecastRecord(int index)
    {
        ArmRebuildRecord? record = Character.ArmRebuildRecords.FirstOrDefault(entry => entry.Index == index);
        if (_recordItem is not { } item || RecastLocate(item) == null || record == null)
        {
            SystemMessage(ArmRebuildText(1), SystemMessageKind.Prompt);
            return;
        }
        if (RecastGroup(ItemCatalog.Number(item.Name, "ArmType")) != RecastGroup(record.ArmType))
        {
            SystemMessage("These attributes cannot be applied to this kind of equipment.", SystemMessageKind.Prompt);
            return;
        }
        string stone = RecastBook.RecordItem;
        if (stone.Length > 0)
        {
            if (BagTotal(stone) < 1)
            {
                SystemMessage("You need a Recovery Stone to retrieve recorded attributes.", SystemMessageKind.Prompt);
                return;
            }
            TakeFromBags(stone, 1);
        }
        ArmRebuildState state = item.Rebuild ??= new ArmRebuildState();
        state.Deltas = new Dictionary<string, int>(record.Deltas);
        state.Sign = record.Sign;
        state.Skill = record.Skill;
        RecastAfterChange(item, "(recorded attributes applied)");
        SendRecastRecords();
        SystemMessage("The recorded attributes have been applied to the equipment.");
    }

    public void RemoveRecastRecord(int index)
    {
        if (Character.ArmRebuildRecords.RemoveAll(entry => entry.Index == index) == 0) return;
        SendRecastRecords();
        Save();
    }

    public void OpenArmTransfer(ItemStack tool)
    {
        if (!_transferOpen) Send(ViewPackets.Create(ArmRebuildPackets.TransferView, 3), "(attribute transfer view)");
        _transferOpen = true;
        _transferTool = tool;
        _transferSource = null;
        _transferTarget = null;
        _transferPreview = false;
    }

    public void CloseArmTransfer()
    {
        if (!_transferOpen) return;
        _transferOpen = false;
        _transferTool = _transferSource = _transferTarget = null;
        _transferPreview = false;
        Send(ViewPackets.Delete(ArmRebuildPackets.TransferView), "(close attribute transfer view)");
    }

    public void TransferAttributes()
    {
        ArmRebuildBook book = RecastBook;
        if (_transferSource is not { } source || _transferTarget is not { } target || RecastLocate(source) == null || RecastLocate(target) == null)
        {
            SystemMessage("Put both pieces of equipment in the slots first.", SystemMessageKind.Prompt);
            return;
        }
        if (_transferTool is not { } tool || RecastLocate(tool) == null)
        {
            SystemMessage("You need an Attribute Transfer Item.", SystemMessageKind.Prompt);
            CloseArmTransfer();
            return;
        }
        if (source.Rebuild is not { Empty: false } state)
        {
            SystemMessage("The first equipment has no attributes to transfer.", SystemMessageKind.Prompt);
            return;
        }
        if (RecastGroup(ItemCatalog.Number(source.Name, "ArmType")) != RecastGroup(ItemCatalog.Number(target.Name, "ArmType")))
        {
            SystemMessage("Attributes can only be transferred between equipment of the same kind.", SystemMessageKind.Prompt);
            return;
        }
        if (tool.Name == book.DamagedTransferItem && new[] { source, target }.Any(item =>
            ItemCatalog.Number(item.Name, "NeedRank") > book.DamagedMaxNeedRank || ItemCatalog.Number(item.Name, "Level") > book.DamagedMaxLevel))
        {
            SystemMessage($"This item only works on equipment with Noble Rank {book.DamagedMaxNeedRank} or below and Lv {book.DamagedMaxLevel} or below.",
                SystemMessageKind.Prompt);
            return;
        }
        ArmRebuildState moved = state.Clone();
        moved.Day = "";
        moved.Today = 0;
        target.Rebuild = moved;
        RecastRemove(source, 1, "(attributes transferred away)");
        RecastRemove(tool, 1, "(transfer item used)");
        RecastAfterChange(target, "(attributes transferred)");
        CloseArmTransfer();
        SystemMessage("The attributes have been transferred.");
        Log.Info(Tag, $"{Name} transfers recast attributes from {source.Name} to {target.Name} with {tool.Name}");
    }

    private void RecastSlot(int how)
    {
        if (Hand != null && how == 0)
        {
            if (RecastFromHand() is { } item) RecastPlace(item);
            return;
        }
        DropHand();
        if (_recastItem == null) return;
        _recastItem = null;
        Send(ViewPackets.RemoveItem(ArmRebuildPackets.RebuildView, 1), "(recast slot emptied)");
    }

    private void RecastPlace(ItemStack item)
    {
        if (!RecastAllowed(item)) return;
        if (_recastItem != null) Send(ViewPackets.RemoveItem(ArmRebuildPackets.RebuildView, 1), "(recast slot)");
        _recastItem = item;
        _recastLocks.Clear();
        PropertyBag bag = ItemProperties(item, 1);
        if (PropertyTable.Has("LockSwitch")) bag["LockSwitch"] = item.Rebuild is { Empty: false } ? 1 : 0;
        Send(ViewPackets.AddItem(ArmRebuildPackets.RebuildView, 1, bag), "(recast slot)");
    }

    private bool RecastAllowed(ItemStack item)
    {
        if (ItemCatalog.Number(item.Name, "ArmType") is 1 or 2 or 4 or 5) return true;
        SystemMessage("Only weapons, armor and helmets can be recast.", SystemMessageKind.Prompt);
        return false;
    }

    private string? RecastProblem(out ItemStack? item, out ArmRebuildLevel? entry)
    {
        item = _recastItem;
        entry = RecastBook.Level(_recastLevel);
        if (!_recastOpen || item == null || RecastLocate(item) == null) return "Put the equipment to recast in the slot first.";
        if (entry == null) return "Please select Recast level.";
        int armType = ItemCatalog.Number(item.Name, "ArmType");
        ArmRebuildState? state = item.Rebuild;
        string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (state != null && state.Day != today)
        {
            state.Day = today;
            state.Today = 0;
        }
        if (state != null && state.Today >= RecastBook.DailyLimit) return $"This equipment can only be recast {RecastBook.DailyLimit} times a day.";
        if (armType is 4 or 5 && entry.Level > 1 && (state?.Sign ?? 0) < entry.Level)
            return $"Clothes and caps need the grade {RecastGrades[Math.Min(entry.Level, RecastGrades.Length - 1)]} before a level {entry.Level} recast.";
        if (Money < entry.Silver) return "You do not have enough silver coins.";
        foreach ((string key, int _, int need) in RecastCost(entry.Level).Concat(RecastLockCost()))
        {
            if (key == RecastBook.SilverKey) continue;
            if (BagTotal(key) < need) return "You do not have all the recast materials.";
        }
        return item.Rebuild == null && _recastLocks.Count > 0 ? "There is nothing to lock on this equipment yet." : null;
    }

    private List<(string Key, int Have, int Need)> RecastCost(int level)
    {
        var lines = new List<(string Key, int Have, int Need)>();
        if (RecastBook.Level(level) is not { } entry) return lines;
        lines.AddRange(entry.Materials.Select(material => (material.Item, BagTotal(material.Item), material.Amount)));
        if (entry.Silver > 0) lines.Add((RecastBook.SilverKey, Money, entry.Silver));
        return lines;
    }

    private List<(string Key, int Have, int Need)> RecastLockCost()
    {
        int attributes = _recastLocks.Count(type => type != SkillLockType);
        var need = new Dictionary<string, int>();
        foreach (ArmRebuildMaterial material in RecastBook.LockMaterials)
            need[material.Item] = need.GetValueOrDefault(material.Item) + material.Amount * attributes;
        if (_recastLocks.Contains(SkillLockType))
        {
            foreach (ArmRebuildMaterial material in RecastBook.SkillLockMaterials)
                need[material.Item] = need.GetValueOrDefault(material.Item) + material.Amount;
        }
        return need.Where(pair => pair.Value > 0).Select(pair => (pair.Key, BagTotal(pair.Key), pair.Value)).ToList();
    }

    private void RecastAfterChange(ItemStack item, string note)
    {
        RecastRefresh(item, note);
        if (_recastOpen && ReferenceEquals(_recastItem, item))
            Send(ViewPackets.UpdateItem(ArmRebuildPackets.RebuildView, 1, ItemProperties(item, 1)), note);
        if (_recordOpen && ReferenceEquals(_recordItem, item))
            Send(ViewPackets.UpdateItem(ArmRebuildPackets.RecordView, 1, ItemProperties(item, 1)), note);
        if (RecastLocate(item) is { View: ViewId.Equipped or RankArmPackets.WornView }) RefreshStats();
        Save();
    }

    private void RecordSlot(int how)
    {
        if (Hand != null && how == 0)
        {
            if (RecastFromHand() is { } item) RecordPlace(item);
            return;
        }
        DropHand();
        if (_recordItem == null) return;
        _recordItem = null;
        Send(ViewPackets.RemoveItem(ArmRebuildPackets.RecordView, 1), "(record slot emptied)");
    }

    private void RecordPlace(ItemStack item)
    {
        if (!RecastAllowed(item)) return;
        if (_recordItem != null) Send(ViewPackets.RemoveItem(ArmRebuildPackets.RecordView, 1), "(record slot)");
        _recordItem = item;
        Send(ViewPackets.AddItem(ArmRebuildPackets.RecordView, 1, ItemProperties(item, 1)), "(record slot)");
        SendRecastRecords();
    }

    private void SendRecastRecords()
    {
        Send(ArmRebuildPackets.ClearRecords(), "(clear recast records)");
        foreach (ArmRebuildRecord record in Character.ArmRebuildRecords.OrderBy(entry => entry.Index))
            Send(ArmRebuildPackets.Record(record.Index, record.Item, RecastInfo(record.Deltas), record.Sign, record.Skill), "(recast record)");
    }

    private void TransferSlot(int how, int index)
    {
        if (index is not (1 or 2))
        {
            DropHand();
            return;
        }
        if (Hand != null && how == 0)
        {
            if (RecastFromHand() is { } item) TransferPlace(index, item);
            return;
        }
        DropHand();
        if (index == 1 && _transferSource != null) _transferSource = null;
        else if (index == 2 && _transferTarget != null) _transferTarget = null;
        else return;
        Send(ViewPackets.RemoveItem(ArmRebuildPackets.TransferView, index), "(transfer slot emptied)");
        TransferPreview();
    }

    private void TransferPlace(int index, ItemStack item)
    {
        if (index is not (1 or 2) || !RecastAllowed(item)) return;
        if (index == 1 && item.Rebuild is not { Empty: false })
        {
            SystemMessage("This equipment has no attributes to transfer.", SystemMessageKind.Prompt);
            return;
        }
        if (ReferenceEquals(index == 1 ? _transferTarget : _transferSource, item)) return;
        if ((index == 1 ? _transferSource : _transferTarget) != null)
            Send(ViewPackets.RemoveItem(ArmRebuildPackets.TransferView, index), "(transfer slot)");
        if (index == 1) _transferSource = item;
        else _transferTarget = item;
        Send(ViewPackets.AddItem(ArmRebuildPackets.TransferView, index, ItemProperties(item, index)), "(transfer slot)");
        TransferPreview();
    }

    private void TransferPreview()
    {
        if (_transferPreview) Send(ViewPackets.RemoveItem(ArmRebuildPackets.TransferView, 3), "(transfer preview)");
        _transferPreview = false;
        if (_transferSource?.Rebuild is not { } state || _transferTarget is not { } target) return;
        ItemStack preview = target.Clone();
        preview.Rebuild = state.Clone();
        _transferPreview = true;
        Send(ViewPackets.AddItem(ArmRebuildPackets.TransferView, 3, ItemProperties(preview, 3)), "(transfer preview)");
    }

    private Dictionary<int, ItemStack>? RecastSlots(int view) => view switch
    {
        ViewId.BagItems => Items,
        ViewId.BagEquip or ViewId.BagArticle or ViewId.Equipped => Bags.TryGetValue(view, out Dictionary<int, ItemStack>? slots) ? slots : null,
        RankArmPackets.WornView => Character.RankWorn,
        _ => null,
    };

    private ItemStack? RecastItemAt(int view, int index)
        => RecastSlots(view) is { } slots && slots.TryGetValue(index, out ItemStack? item) ? item : null;

    private (int View, int Index)? RecastLocate(ItemStack? item)
    {
        if (item == null) return null;
        foreach (int view in new[] { ViewId.BagEquip, ViewId.BagArticle, ViewId.BagItems, ViewId.Equipped, RankArmPackets.WornView })
        {
            if (RecastSlots(view) is not { } slots) continue;
            foreach ((int index, ItemStack stack) in slots)
            {
                if (ReferenceEquals(stack, item)) return (view, index);
            }
        }
        return null;
    }

    private ItemStack? RecastFromHand()
    {
        (int View, int Index)? from = HandSource();
        DropHand();
        return from is { } source ? RecastItemAt(source.View, source.Index) : null;
    }

    private void RecastRefresh(ItemStack item, string note)
    {
        if (RecastLocate(item) is not { } at || (at.View == RankArmPackets.WornView && !_nobleWornOpen)) return;
        Send(ViewPackets.UpdateItem(at.View, at.Index, ItemProperties(item, at.Index)), note);
    }

    private void RecastRemove(ItemStack item, int amount, string note)
    {
        if (RecastLocate(item) is not { } at) return;
        item.Amount -= amount;
        bool shown = at.View != RankArmPackets.WornView || _nobleWornOpen;
        if (item.Amount > 0)
        {
            if (shown) Send(ViewPackets.UpdateItem(at.View, at.Index, new PropertyBag { ["Amount"] = item.Amount }), note);
            return;
        }
        RecastSlots(at.View)!.Remove(at.Index);
        if (shown) Send(ViewPackets.RemoveItem(at.View, at.Index), note);
        if (at.View is ViewId.Equipped or RankArmPackets.WornView) RefreshStats();
    }

    private static string[] RecastProperties(int armType)
        => armType is 4 or 5 ? ["AddDF", "AddDodgeRatio", "AddMaxHP"] : ["AddAT", "AddHitRatio", "AddMaxHP"];

    private static int RecastType(string property, int armType) => property switch
    {
        "AddAT" => 1,
        "AddHitRatio" => 2,
        "AddMaxHP" => armType is 4 or 5 ? 7 : 3,
        "AddDF" => 5,
        "AddDodgeRatio" => 6,
        _ => 0,
    };

    private static int RecastGroup(int armType) => armType is 1 or 2 ? 1 : armType;

    private static string RecastInfo(Dictionary<string, int> deltas)
        => string.Concat(deltas.Select(pair => $"{pair.Key} {pair.Value};"));

    private static string ArmRebuildText(int index) => index switch
    {
        0 => "Unable to close interface. Please retrieve the equipment!",
        7 => "Another process is ongoing. Please wait or try again!",
        _ => "You have not made a selection or your selection is invalid!",
    };
}
