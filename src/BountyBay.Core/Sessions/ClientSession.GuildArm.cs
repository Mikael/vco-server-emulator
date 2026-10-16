using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int GuildArmComposeView = GuildArmPackets.ComposeView;
    public const int GuildArmMedalView = GuildArmPackets.MedalView;

    private int _guildArmView;
    private bool _guildArmShown;
    private (int View, int Index, string Item)? _guildArmFrom;

    private GuildArmRules GuildArmRuleSet => GuildBuildDirectory.Rules(Context).Medals;

    public void OpenGuildArm(int view)
    {
        if (Guild == null)
        {
            SystemMessage("You can use the Honor Medal services after joining a guild.", SystemMessageKind.Prompt);
            return;
        }
        CloseGuildArm();
        _guildArmView = view;
        Send(ViewPackets.Create(view, 1), view == GuildArmComposeView ? "(honor medal synthesis)" : "(honor medal management)");
    }

    public void CloseGuildArm()
    {
        if (_guildArmView == 0) return;
        Send(ViewPackets.Delete(_guildArmView), "(close honor medal window)");
        _guildArmView = 0;
        _guildArmShown = false;
        _guildArmFrom = null;
    }

    public bool GuildArmSelect(int view, int index)
    {
        if (view != _guildArmView || _guildArmView == 0) return false;
        if (Hand != null && HandSource() is { } from)
        {
            ItemStack held = Hand;
            DropHand();
            if (view == GuildArmMedalView && GuildArmRuleSet.Medal(held.Name).Kind == 0)
            {
                SystemMessage("Only an Honor Medal can be placed here.", SystemMessageKind.Prompt);
                return true;
            }
            if (view == GuildArmComposeView)
            {
                SystemMessage($"Synthesis takes {GuildArmRuleSet.FragmentsPerMedal} x {GuildArmItemName(GuildArmRuleSet.Fragment)} straight from your bags.", SystemMessageKind.Prompt);
                return true;
            }
            _guildArmFrom = (from.View, from.Index, held.Name);
            ShowGuildArmSlot(held);
            return true;
        }
        DropHand();
        _guildArmFrom = null;
        ShowGuildArmSlot(null);
        return true;
    }

    public void MakeGuildArm(int kind, int mode)
    {
        if (Guild is not { } guild)
        {
            SystemMessage("You can use the Honor Medal services after joining a guild.", SystemMessageKind.Prompt);
            return;
        }
        if (mode == 2)
        {
            UpgradeGuildArm();
            return;
        }
        GuildArmRules rules = GuildArmRuleSet;
        string medal = rules.MedalItem(kind, 1);
        if (medal.Length == 0) return;
        if (BagTotal(rules.Fragment) < rules.FragmentsPerMedal)
        {
            SystemMessage($"Each synthesis consumes {rules.FragmentsPerMedal} x {GuildArmItemName(rules.Fragment)}.", SystemMessageKind.Prompt);
            return;
        }
        List<ItemStack> taken = TakeFromBags(rules.Fragment, rules.FragmentsPerMedal);
        var result = new ItemStack { Name = medal, Photo = Context.Data.PhotoOf(medal), Amount = 1 };
        if (!GiveItem(result.Clone(), "(honor medal synthesized)"))
        {
            foreach (ItemStack back in taken) GiveItem(back, "(synthesis refunded)");
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        if (_guildArmView == GuildArmComposeView) ShowGuildArmSlot(result);
        RefreshLoad();
        Save();
        GuildBuildDirectory.Note(Context, guild, $"{Name} synthesized {GuildArmItemName(medal)}.");
        SystemMessage($"You synthesized {GuildArmItemName(medal)}.");
        Log.Info(Tag, $"{Name} synthesizes {medal}");
    }

    public void UpgradeGuildArm()
    {
        if (!GuildArmInSlot(out string medal, out int kind, out int level)) return;
        GuildArmRules rules = GuildArmRuleSet;
        if (level >= rules.MaxLevel)
        {
            SystemMessage("This Honor Medal has reached the highest level.", SystemMessageKind.Prompt);
            return;
        }
        int cost = rules.UpgradeCost(level);
        if (BagTotal(rules.Fragment) < cost)
        {
            SystemMessage($"Upgrading this Honor Medal needs {cost} x {GuildArmItemName(rules.Fragment)}.", SystemMessageKind.Prompt);
            return;
        }
        string next = rules.MedalItem(kind, level + 1);
        (int view, int index, _) = _guildArmFrom!.Value;
        TakeFromBags(rules.Fragment, cost);
        TakeFrom(view, index, 1, "(honor medal upgraded)");
        var result = new ItemStack { Name = next, Photo = Context.Data.PhotoOf(next), Amount = 1 };
        GiveItem(result.Clone(), "(honor medal upgraded)");
        _guildArmFrom = FindGuildArmItem(next) is { } found ? (found.View, found.Index, next) : null;
        ShowGuildArmSlot(_guildArmFrom == null ? null : result);
        SendGuildArmStuff();
        RefreshLoad();
        Save();
        SystemMessage($"Your Honor Medal rises to {GuildArmItemName(next)}.");
        Log.Info(Tag, $"{Name} upgrades {medal} to {next} for {cost} fragments");
    }

    public void SplitGuildArm()
    {
        if (!GuildArmInSlot(out string medal, out _, out _)) return;
        GuildArmRules rules = GuildArmRuleSet;
        (int view, int index, _) = _guildArmFrom!.Value;
        TakeFrom(view, index, 1, "(honor medal split)");
        var fragments = new ItemStack { Name = rules.Fragment, Photo = Context.Data.PhotoOf(rules.Fragment), Amount = rules.SplitFragments };
        if (!GiveItem(fragments, "(honor medal fragments)"))
        {
            GiveItem(new ItemStack { Name = medal, Photo = Context.Data.PhotoOf(medal), Amount = 1 }, "(split refunded)");
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        _guildArmFrom = null;
        ShowGuildArmSlot(null);
        RefreshLoad();
        Save();
        SystemMessage($"The Honor Medal breaks into {rules.SplitFragments} x {GuildArmItemName(rules.Fragment)}.");
        Log.Info(Tag, $"{Name} splits {medal}");
    }

    public void RebindGuildArm()
    {
        if (!GuildArmInSlot(out string medal, out _, out _)) return;
        SystemMessage($"{GuildArmItemName(medal)} is now bound to your guild membership again.");
        Log.Info(Tag, $"{Name} rebinds {medal}");
    }

    public void ViewGuildArmUpgrade(int type)
    {
        Log.Debug(Tag, $"{Name} views the honor medal upgrade (type {type})");
        SendGuildArmStuff();
    }

    public void CloseGuildArmWindow(int mode)
    {
        int view = mode == 2 ? GuildArmMedalView : GuildArmComposeView;
        if (_guildArmView == view) CloseGuildArm();
    }

    public static void AddGuildArmProperties(GuildArmRules rules, PropertyBag bag, string item)
    {
        (int kind, int level) = rules.Medal(item);
        if (kind == 0) return;
        if (PropertyTable.Has("ConsArmType")) bag["ConsArmType"] = (byte)kind;
        if (PropertyTable.Has("QualityLevel")) bag["QualityLevel"] = level;
    }

    private void SendGuildArmStuff()
    {
        GuildArmRules rules = GuildArmRuleSet;
        int need = GuildArmInSlot(out _, out _, out int level, quiet: true) ? rules.UpgradeCost(level) : rules.FragmentsPerMedal;
        Send(GuildArmPackets.Stuff([(rules.Fragment, BagTotal(rules.Fragment), need)]), "(honor medal materials)");
    }

    private bool GuildArmInSlot(out string medal, out int kind, out int level, bool quiet = false)
    {
        medal = "";
        kind = 0;
        level = 0;
        if (_guildArmFrom is not { } from || MailBagOf(from.View) is not { } bag || !bag.TryGetValue(from.Index, out ItemStack? item)
            || item.Name != from.Item)
        {
            _guildArmFrom = null;
            if (!quiet) SystemMessage("Place an Honor Medal in the slot first.", SystemMessageKind.Prompt);
            return false;
        }
        medal = item.Name;
        (kind, level) = GuildArmRuleSet.Medal(medal);
        return kind != 0;
    }

    private (int View, int Index)? FindGuildArmItem(string name)
    {
        foreach (int view in BboBagViews)
        {
            foreach ((int slot, ItemStack stack) in MailBagOf(view)!.OrderBy(pair => pair.Key))
            {
                if (stack.Name == name) return (view, slot);
            }
        }
        return null;
    }

    private void ShowGuildArmSlot(ItemStack? item)
    {
        if (_guildArmView == 0) return;
        if (_guildArmShown) Send(ViewPackets.RemoveItem(_guildArmView, 1), "(honor medal slot)");
        _guildArmShown = item != null;
        if (item == null) return;
        PropertyBag bag = ItemProperties(item, 1);
        AddGuildArmProperties(GuildArmRuleSet, bag, item.Name);
        Send(ViewPackets.AddItem(_guildArmView, 1, bag), "(honor medal slot)");
    }

    private string GuildArmItemName(string item) => ItemCatalog.Text(item, "ShowName") is { Length: > 0 } shown ? shown : item;
}
