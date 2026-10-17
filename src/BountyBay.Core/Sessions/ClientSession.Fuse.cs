using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class FlagFuseOption
{
    public const int First = 400;
    public const int BeliefFuse = 400;
    public const int Leave = 419;
    public const int Last = 419;
}

public sealed partial class ClientSession
{
    public const int BeliefFuseView = 51;

    private const int FuseInputIndex = 1;
    private const int FuseOutputIndex = 2;
    private const int FuseButtonsMerge = 1;
    private const int FuseButtonsSave = 2;

    private readonly HashSet<int> _fuseShown = [];
    private bool _fuseOpen;
    private (int View, int Slot)? _fuseInput;
    private BeliefFuseEntry? _fusePending;

    public bool HandleFlagFuseOption(int target, int option)
    {
        if (option < FlagFuseOption.First || option > FlagFuseOption.Last) return false;
        if (option == FlagFuseOption.BeliefFuse)
        {
            Send(UiPackets.CloseMenu(), "(close menu)");
            OpenBeliefFuse();
            return true;
        }
        CloseNpcDialogue();
        return true;
    }

    public void OpenBeliefFuse()
    {
        if (_fuseOpen) return;
        _fuseOpen = true;
        _fuseShown.Clear();
        _fuseInput = null;
        _fusePending = null;
        Send(ViewPackets.Create(BeliefFuseView, 2), "(faith fusion view)");
    }

    public void CloseBeliefFuse()
    {
        if (!_fuseOpen) return;
        _fuseOpen = false;
        _fuseShown.Clear();
        _fuseInput = null;
        _fusePending = null;
        Send(ViewPackets.Delete(BeliefFuseView), "(close faith fusion)");
    }

    public void BeliefFuseSlot(int index)
    {
        if (!_fuseOpen)
        {
            DropHand();
            return;
        }
        if (index != FuseInputIndex)
        {
            DropHand();
            return;
        }
        if (Hand != null && HandSource() is { } from)
        {
            DropHand();
            ItemStack item = MailBagOf(from.View)![from.Index];
            if (ItemCatalog.Number(item.Name, "ArmType") != BeliefFuseRules.BeliefArmType)
            {
                SystemMessage("Only a faith can be placed here.", SystemMessageKind.Prompt);
                return;
            }
            _fuseInput = from;
            _fusePending = null;
            ShowFuseItem(FuseOutputIndex, null);
            ShowFuseItem(FuseInputIndex, FuseItem(item, FuseInputIndex, FuseInfoOf(item.Name)));
            Send(FlagFusePackets.FuseButtons(FuseButtonsMerge), "(faith fusion buttons)");
            return;
        }
        DropHand();
        _fuseInput = null;
        _fusePending = null;
        ShowFuseItem(FuseOutputIndex, null);
        ShowFuseItem(FuseInputIndex, null);
    }

    public void StartBeliefFuse(string key)
    {
        BeliefFuseRules rules = BeliefFuseRules.For(Context.Settings);
        if (FuseInputItem() is not { } input)
        {
            SystemMessage("Put the faith you want to strengthen in the left column first.", SystemMessageKind.Prompt);
            return;
        }
        if (rules.Find(key) is not { } entry || entry.FuseInfo.Length == 0)
        {
            SystemMessage("This faith has no effect that can be fused.", SystemMessageKind.Prompt);
            return;
        }
        if (entry.Item.Equals(input.Item.Name, StringComparison.OrdinalIgnoreCase))
        {
            SystemMessage("A faith cannot be fused with itself.", SystemMessageKind.Prompt);
            return;
        }
        if (!CanPayFuse(rules, entry, input)) return;
        _fusePending = entry;
        ShowFuseItem(FuseOutputIndex, FuseItem(input.Item, FuseOutputIndex, entry.FuseInfo));
        Send(FlagFusePackets.FuseButtons(FuseButtonsSave), "(faith fusion buttons)");
        Log.Info(Tag, $"{Name} previews fusing {entry.Item} into {input.Item.Name}");
    }

    public void SaveBeliefFuse()
    {
        BeliefFuseRules rules = BeliefFuseRules.For(Context.Settings);
        if (_fusePending is not { } entry || FuseInputItem() is not { } input)
        {
            Send(FlagFusePackets.FuseButtons(FuseButtonsMerge), "(faith fusion buttons)");
            return;
        }
        if (!CanPayFuse(rules, entry, input)) return;
        int fee = rules.Fee(entry.Type);
        if (rules.ConsumeTarget) TakeFromBags(entry.Item, 1);
        if (fee > 0) SetMoney(Money - fee);
        string name = input.Item.Name;
        Character.BeliefFusions[name] = entry.FuseInfo;
        _fusePending = null;
        if (FuseInputItem() is { } kept)
        {
            ShowFuseItem(FuseInputIndex, FuseItem(kept.Item, FuseInputIndex, entry.FuseInfo));
            if (PropertyTable.Has("FuseInfo"))
                Send(ViewPackets.UpdateItem(kept.View, kept.Slot, new PropertyBag { ["FuseInfo"] = entry.FuseInfo }), "(faith fused)");
        }
        else
        {
            _fuseInput = null;
            ShowFuseItem(FuseInputIndex, null);
        }
        ShowFuseItem(FuseOutputIndex, null);
        Send(FlagFusePackets.FuseButtons(FuseButtonsMerge), "(faith fusion buttons)");
        RefreshLoad();
        Save();
        SystemMessage($"The effect of {FuseLabel(entry.Item)} was fused into {FuseLabel(name)}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} fuses {entry.Item} into {name} ({entry.FuseInfo}) for {fee}");
    }

    public void CancelBeliefFuse()
    {
        _fusePending = null;
        ShowFuseItem(FuseOutputIndex, null);
        Send(FlagFusePackets.FuseButtons(FuseButtonsMerge), "(faith fusion buttons)");
    }

    public string FuseInfoOf(string item) => Character.BeliefFusions.TryGetValue(item, out string? info) ? info : "";

    private bool CanPayFuse(BeliefFuseRules rules, BeliefFuseEntry entry, (int View, int Slot, ItemStack Item) input)
    {
        int needed = rules.ConsumeTarget ? 1 : 0;
        if (needed > 0)
        {
            int owned = BagTotal(entry.Item) - (input.Item.Name == entry.Item ? 1 : 0);
            if (owned < needed)
            {
                SystemMessage($"You need a {FuseLabel(entry.Item)} in your bag to fuse its effect.", SystemMessageKind.Prompt);
                return false;
            }
        }
        int fee = rules.Fee(entry.Type);
        if (Money < fee)
        {
            SystemMessage($"This fusion costs {fee} silver coins.", SystemMessageKind.Prompt);
            return false;
        }
        return true;
    }

    private (int View, int Slot, ItemStack Item)? FuseInputItem()
    {
        if (_fuseInput is not { } input || MailBagOf(input.View) is not { } bag || !bag.TryGetValue(input.Slot, out ItemStack? item)) return null;
        return ItemCatalog.Number(item.Name, "ArmType") == BeliefFuseRules.BeliefArmType ? (input.View, input.Slot, item) : null;
    }

    private static PropertyBag FuseItem(ItemStack item, int index, string fuseInfo)
    {
        PropertyBag bag = ItemProperties(item, index);
        if (PropertyTable.Has("FuseInfo")) bag["FuseInfo"] = fuseInfo;
        return bag;
    }

    private void ShowFuseItem(int index, PropertyBag? bag)
    {
        if (_fuseShown.Remove(index)) Send(ViewPackets.RemoveItem(BeliefFuseView, index), "(faith fusion slot)");
        if (bag == null) return;
        _fuseShown.Add(index);
        Send(ViewPackets.AddItem(BeliefFuseView, index, bag), "(faith fusion slot)");
    }

    private string FuseLabel(string key)
        => Context.Data.Items.Goods.TryGetValue(key, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : key;
}
