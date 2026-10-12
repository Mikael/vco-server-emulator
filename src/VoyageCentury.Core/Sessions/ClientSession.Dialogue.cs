using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed partial class ClientSession
{
    private const int BankStep = 100;

    public void Talk(NpcDefinition npc)
    {
        if (npc.Shop?.ContainsKey(ViewId.Shipyard) == true && OpenShipyardMenu(npc.Id)) return;
        TalkingTo = npc.Id;
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, npc.Greeting) };
        if (npc.Options.Count == 0) entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        foreach (DialogueOption option in npc.Options)
            entries.Add((DialogueEntry.Option, (int)option.Action, option.Text));
        Send(ChatPackets.NpcDialogue(npc.Id, entries), "(npc dialogue)");
    }

    public void ChooseOption(int target, DialogueAction action)
    {
        if (HandleProgressOption(target, (int)action)) return;
        if (ChooseShipyardOption(target, (int)action)) return;
        if (HandleFleetOption(target, (int)action)) return;
        if (HandleGuildOption(target, (int)action)) return;
        Context.Data.Npcs.TryGetValue(target, out NpcDefinition? npc);
        switch (action)
        {
            case DialogueAction.Mail:
                OpenParcelDesk();
                break;
            case DialogueAction.Collect:
                ReceiveMail();
                break;
            case DialogueAction.Trade when npc?.Shop != null:
                OpenShop(npc.Shop);
                Send(ChatPackets.NpcDialogue(target,
                [
                    (DialogueEntry.Text, 0, "Click an item to buy it. Press Sell, then click one of yours."),
                    (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
                ]), "(npc dialogue)");
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
                Send(ChatPackets.NpcDialogue(target, [(DialogueEntry.Text, 0, $"Fair winds, {Name}.")]), "(npc dialogue)");
                break;
        }
    }

    public void Deposit(int amount)
    {
        if (amount <= 0 || amount > Money) return;
        Character.Bank += amount;
        SetMoney(Money - amount);
        BankChanged($"deposits {amount}");
    }

    public void Withdraw(int amount)
    {
        if (amount <= 0 || amount > Character.Bank) return;
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
