using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int AideArmView = 54;

    private bool _aideArmOpen;
    private ItemStack? _aideArmItem;

    private AideArmBook AideArms => AideArmBook.For(Context.Settings);

    private static string AideDay => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static void AideArmItemProperties(PropertyBag bag, ItemStack item) => AideArmItemProperties(bag, item, null);

    private static void AideArmItemProperties(PropertyBag bag, ItemStack item, List<AideArmLine>? lines)
    {
        if (!AideArmBook.IsGeneralEquipment(item.Name)) return;
        AideArmState state = EnsureAideArm(item);
        AideArmBook book = AideArmBook.Shared;
        lines ??= state.Lines;
        if (PropertyTable.Has("Quality")) bag["Quality"] = AideArmBook.QualityOf(item.Name);
        if (PropertyTable.Has("RandProp")) bag["RandProp"] = string.Join(' ', lines.Select(line => line.Prop));
        if (PropertyTable.Has("LockProp")) bag["LockProp"] = string.Join(' ', lines.Where((_, position) => !state.Active.Contains(position)).Select(line => line.Prop));
        foreach (AideArmLine line in lines)
        {
            string name = line.Prop >= 0 && line.Prop < book.Props.Count ? book.Props[line.Prop] : "";
            if (name.Length > 0 && PropertyTable.Has(name)) bag[name] = line.Value;
        }
    }

    private static AideArmState EnsureAideArm(ItemStack item)
    {
        if (item.AideArm is { } existing) return existing;
        AideArmBook book = AideArmBook.Shared;
        int quality = AideArmBook.QualityOf(item.Name);
        List<AideArmLine> lines = Enumerable.Range(0, book.Props.Count).OrderBy(_ => Random.Shared.Next()).Take(book.Rules.Lines)
            .Select(prop => new AideArmLine { Prop = prop, Value = book.RollValue(quality, prop) }).ToList();
        item.AideArm = new AideArmState { Lines = lines };
        return item.AideArm;
    }

    public void OpenAideArmRebuild()
    {
        if (!_aideArmOpen) Send(ViewPackets.Create(AideArmView, 1), "(general equipment rebuild view)");
        _aideArmOpen = true;
        _aideArmItem = null;
        SendAideArmCounts();
    }

    public void CloseAideArmRebuild()
    {
        if (_aideArmItem?.AideArm is { } state) state.Pending = null;
        _aideArmItem = null;
        if (!_aideArmOpen) return;
        _aideArmOpen = false;
        Send(ViewPackets.Delete(AideArmView), "(close general equipment rebuild)");
    }

    public bool AideArmSelect(int view, int how, int index)
    {
        if (view != AideArmView || !_aideArmOpen) return false;
        if (Hand == null || how != 0 || index != 1)
        {
            DropHand();
            return true;
        }
        if (RecastFromHand() is not { } item) return true;
        if (!AideArmBook.IsGeneralEquipment(item.Name))
        {
            SystemMessage("Only General equipment can be rebuilt here.", SystemMessageKind.Prompt);
            return true;
        }
        if (_aideArmItem?.AideArm is { } previous) previous.Pending = null;
        EnsureAideArm(item);
        _aideArmItem = item;
        Send(ViewPackets.AddItem(AideArmView, 1, ItemProperties(item, 1)), "(general equipment placed)");
        Send(AideArmPackets.Result(false), "(general equipment rebuild ready)");
        SendAideArmCounts();
        return true;
    }

    public void RebuildAideArm(int mode, IReadOnlyCollection<int> locked)
    {
        if (AideArmTarget() is not { } item || item.AideArm is not { } state) return;
        AideArmBook book = AideArms;
        int quality = AideArmBook.QualityOf(item.Name);
        var kept = state.Lines.Select(line => line.Prop).Where(locked.Contains).ToHashSet();
        if (kept.Count >= state.Lines.Count)
        {
            SystemMessage("At least one attribute must stay unlocked to rebuild.", SystemMessageKind.Prompt);
            Send(AideArmPackets.Result(false), "(general equipment rebuild refused)");
            return;
        }
        ResetAideFreeDay();
        bool free = Character.AideFreeRebuildsUsed < book.Rules.FreeRebuildsPerDay;
        if (free)
        {
            Character.AideFreeRebuildsUsed++;
        }
        else
        {
            string material = book.Material(mode);
            int cost = book.Cost(mode, quality, kept.Count);
            if (material.Length == 0 || BagTotal(material) < cost)
            {
                string label = Context.Data.Items.Goods.GetValueOrDefault(material)?.Label ?? material;
                SystemMessage($"The rebuild needs {cost} x {label}.", SystemMessageKind.Prompt);
                Send(AideArmPackets.Result(false), "(general equipment rebuild refused)");
                return;
            }
            if (cost > 0) TakeFromBags(material, cost);
        }

        var used = new HashSet<int>(kept);
        state.Pending = state.Lines.Select(line =>
        {
            if (kept.Contains(line.Prop)) return line.Clone();
            int prop = Enumerable.Range(0, book.Props.Count).Where(candidate => !used.Contains(candidate))
                .OrderBy(_ => Random.Shared.Next()).DefaultIfEmpty(line.Prop).First();
            used.Add(prop);
            return new AideArmLine { Prop = prop, Value = book.RollValue(quality, prop) };
        }).ToList();
        Save();
        PropertyBag result = ItemProperties(item, 1);
        AideArmItemProperties(result, item, state.Pending);
        Send(ViewPackets.UpdateItem(AideArmView, 1, result), "(general equipment result)");
        Send(AideArmPackets.Result(true), "(general equipment rebuilt)");
        SendAideArmCounts();
        Log.Info(Tag, $"{Name} rebuilds {item.Name}{(free ? " for free" : $" (mode {mode})")}, keeping {string.Join(',', kept)}");
    }

    public void SaveAideArm()
    {
        if (AideArmTarget() is not { } item || item.AideArm is not { Pending: { } pending } state)
        {
            SystemMessage("There is no rebuilt attribute to keep.", SystemMessageKind.Prompt);
            return;
        }
        state.Lines = pending;
        state.Pending = null;
        Save();
        RecastRefresh(item, "(general equipment rebuilt)");
        Send(ViewPackets.UpdateItem(AideArmView, 1, ItemProperties(item, 1)), "(general equipment rebuilt)");
        Send(AideArmPackets.Result(false), "(general equipment rebuild kept)");
    }

    public void RecoverAideArm()
    {
        if (_aideArmItem is { AideArm: { } state } item)
        {
            state.Pending = null;
            Send(ViewPackets.UpdateItem(AideArmView, 1, ItemProperties(item, 1)), "(general equipment result dropped)");
        }
        Send(AideArmPackets.Result(false), "(general equipment rebuild dropped)");
    }

    public void ActivateAideArmLine(int prop)
    {
        if (AideArmTarget() is not { } item || item.AideArm is not { } state || state.Lines.All(line => line.Prop != prop)) return;
        int position = state.Lines.FindIndex(line => line.Prop == prop);
        if (state.Active.Contains(position)) return;
        state.Active.Add(position);
        Save();
        RecastRefresh(item, "(general equipment attribute activated)");
        Send(ViewPackets.UpdateItem(AideArmView, 1, ItemProperties(item, 1)), "(general equipment attribute activated)");
    }

    private ItemStack? AideArmTarget()
    {
        if (_aideArmOpen && _aideArmItem is { } item && RecastLocate(item) != null) return item;
        SystemMessage("Put a piece of General equipment in the rebuild slot first.", SystemMessageKind.Prompt);
        return null;
    }

    private void ResetAideFreeDay()
    {
        if (Character.AideRebuildDay == AideDay) return;
        Character.AideRebuildDay = AideDay;
        Character.AideFreeRebuildsUsed = 0;
    }

    private void SendAideArmCounts()
    {
        AideArmBook book = AideArms;
        ResetAideFreeDay();
        Send(AideArmPackets.FreeCount(Math.Max(0, book.Rules.FreeRebuildsPerDay - Character.AideFreeRebuildsUsed)), "(free general rebuilds)");
        for (int mode = 0; mode < book.Rules.Materials.Count; mode++)
            Send(AideArmPackets.Owned(mode, BagTotal(book.Material(mode))), "(general rebuild materials)");
    }
}
