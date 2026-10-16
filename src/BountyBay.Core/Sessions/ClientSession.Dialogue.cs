using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int BankStep = 100;

    public void Talk(NpcDefinition npc)
    {
        if (TalkArmUpgrade(npc)) return;
        if (npc.Shop?.ContainsKey(ViewId.Shipyard) == true && OpenShipyardMenu(npc.Id)) return;
        if (BattleTalk(npc)) return;
        TalkingTo = npc.Id;
        if (TalkWithClientMenu(npc)) return;
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, npc.Greeting) };
        if (npc.Options.Count == 0) entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        foreach (DialogueOption option in npc.Options)
            entries.Add((DialogueEntry.Option, (int)option.Action, option.Text));
        Send(ChatPackets.NpcDialogue(npc.Id, entries), "(npc dialogue)");
    }

    public void ChooseOption(int target, DialogueAction action)
    {
        action = ClientMenuAction(action);
        if (HandleBattleOption(target, (int)action)) return;
        if (HandleGuildHallOption(target, (int)action)) return;
        if (HandleAideOption(target, (int)action)) return;
        if (HandleArmUpgradeOption(target, (int)action)) return;
        if (HandleAuctionOption(target, (int)action)) return;
        if (HandleArmRebuildOption(target, (int)action)) return;
        if (HandleBboItemOption(target, (int)action)) return;
        if (HandleShipModelOption(target, (int)action) || HandleWhistleOption(target, (int)action)) return;
        if (HandleTotemOption(target, (int)action)) return;
        if (HandleSoulOption(target, (int)action)) return;
        if (HandleFlagFuseOption(target, (int)action)) return;
        if (HandleBboSocialOption(target, (int)action)) return;
        if (HandleLeisureOption(target, (int)action)) return;
        if ((int)action == 390 && BboChooseShipOption(target, (int)action)) return;
        if (HandleProgressOption(target, (int)action)) return;
        if (ChooseShipyardOption(target, (int)action)) return;
        if (HandleFleetOption(target, (int)action)) return;
        if (HandleGuildOption(target, (int)action)) return;
        if (HandleGuildWarOption(target, (int)action)) return;
        Context.Data.Npcs.TryGetValue(target, out NpcDefinition? npc);
        switch (action)
        {
            case DialogueAction.Mail:
                OpenParcelDesk();
                break;
            case DialogueAction.Collect:
                ReceiveMail();
                break;
            case DialogueAction.Warehouse:
                OpenWarehouse(target);
                break;
            case DialogueAction.WarehouseExpand:
                ExpandWarehouse();
                OpenWarehouse(target);
                break;
            case DialogueAction.Nobility:
                ShowNobility(target);
                break;
            case DialogueAction.NobilityApply:
                ApplyForNobility();
                ShowNobility(target);
                break;
            case DialogueAction.Trade when npc?.Shop != null:
                OpenShop(npc.Shop);
                break;
            case DialogueAction.Bank:
                ShowBank(target);
                break;
            case DialogueAction.Deposit:
                Deposit(BankStep);
                ShowBank(target);
                break;
            case DialogueAction.Withdraw:
                Withdraw(BankStep);
                ShowBank(target);
                break;
            case DialogueAction.Sail:
                SailOff();
                break;
            default:
                CloseShop();
                CloseWarehouse();
                Send(ChatPackets.NpcDialogue(target, [(DialogueEntry.Text, 0, $"Fair winds, {Name}.")]), "(npc dialogue)");
                break;
        }
    }

    public void ShowNobility(int target)
    {
        NobilityRules rules = Rule.Nobility;
        int next = Character.Rank + 1;
        string held = Character.Rank > 0 && Character.Rank <= rules.Titles.Count ? rules.Titles[Character.Rank - 1] : "Commoner";
        var entries = new List<(DialogueEntry, int, string)>();
        if (next > rules.Titles.Count)
        {
            entries.Add((DialogueEntry.Text, 0, $"You are a {held}. There is no higher title in the realm."));
        }
        else
        {
            entries.Add((DialogueEntry.Text, 0, $"You are a {held} with {Character.Reputation} reputation and {Character.Amity} amity. "
                + $"The title of {rules.Titles[next - 1]} asks for {rules.Reputation(next)} reputation"
                + (rules.RequireAmity ? $", {rules.Amity(next)} amity" : "") + $" and a fee of {rules.Fee(next)} silver coins."));
            entries.Add((DialogueEntry.Option, (int)DialogueAction.NobilityApply, $"Apply for the title of {rules.Titles[next - 1]}"));
        }
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(nobility dialogue)");
    }

    public void ApplyForNobility()
    {
        NobilityRules rules = Rule.Nobility;
        int next = Character.Rank + 1;
        if (next > rules.Titles.Count) return;
        if (Character.Reputation < rules.Reputation(next))
        {
            SystemMessage($"Your reputation is too low: {rules.Reputation(next)} is needed.");
            return;
        }
        if (rules.RequireAmity && Character.Amity < rules.Amity(next))
        {
            SystemMessage($"Your amity with this country is too low: {rules.Amity(next)} is needed.");
            return;
        }
        if (Money < rules.Fee(next))
        {
            SystemMessage($"The application costs {rules.Fee(next)} silver coins.");
            return;
        }
        SetMoney(Money - rules.Fee(next));
        Character.Rank = next;
        SetProperty("Rank", next, visibleToOthers: true);
        GrantTitle(rules.Titles[next - 1]);
        Save();
        Log.Info(Tag, $"{Name} is raised to noble rank {next}");
    }

    public void Deposit(int amount)
    {
        if (amount <= 0 || amount > Money) return;
        amount = (int)Math.Min(amount, (long)Rule.Economy.SilverCap - Character.Bank);
        if (amount <= 0)
        {
            SystemMessage($"The bank keeps at most {Rule.Economy.SilverCap} silver coins for you.");
            return;
        }
        Character.Bank += amount;
        SetMoney(Money - amount);
        BankChanged($"deposits {amount}");
    }

    public void Withdraw(int amount)
    {
        if (amount <= 0 || amount > Character.Bank) return;
        amount = (int)Math.Min(amount, (long)Rule.Economy.SilverCap - Money);
        if (amount <= 0)
        {
            SystemMessage($"You can hold at most {Rule.Economy.SilverCap} silver coins.");
            return;
        }
        Character.Bank -= amount;
        SetMoney(Money + amount);
        BankChanged($"withdraws {amount}");
    }

    private void BankChanged(string what)
    {
        Save();
        SetProperty("BankMoney", Character.Bank);
        Log.Info(Tag, $"{Name} {what} (bank {Character.Bank}, carried {Money})");
    }

    private void ShowBank(int target)
        => Send(ChatPackets.NpcDialogue(target,
        [
            (DialogueEntry.Text, 0, $"You have {Character.Bank} gold in the bank and carry {Money}."),
            (DialogueEntry.Option, (int)DialogueAction.Deposit, $"Deposit {BankStep}"),
            (DialogueEntry.Option, (int)DialogueAction.Withdraw, $"Withdraw {BankStep}"),
            (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
        ]), "(npc dialogue)");
}
