using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public static class ServiceOption
    {
        public const int First = 600;
        public const int PriestHeal = 600;
        public const int Donation = 601;
        public const int FirstDonation = 602;
        public const int LastDonation = 604;
        public const int FieldRepair = 605;
        public const int Identify = 606;
        public const int FieldHeal = 607;
        public const int Last = 619;
    }

    private ServiceRules Services => ServiceRules.For(Context.Settings);

    private bool HandleServiceOption(int target, int option)
    {
        if (option is < ServiceOption.First or > ServiceOption.Last) return false;
        switch (option)
        {
            case ServiceOption.PriestHeal:
                Heal(target, Services.SilverPerHealPoint);
                break;
            case ServiceOption.FieldHeal:
                Heal(target, 0);
                break;
            case ServiceOption.Donation:
                ShowDonations(target);
                break;
            case >= ServiceOption.FirstDonation and <= ServiceOption.LastDonation when option - ServiceOption.FirstDonation < Services.Donations.Count:
                Donate(target, Services.Donations[option - ServiceOption.FirstDonation]);
                break;
            case ServiceOption.FieldRepair:
                FieldRepair(target);
                break;
            case ServiceOption.Identify:
                IdentifyArticles(target);
                break;
            default:
                CloseNpcDialogue();
                break;
        }
        return true;
    }

    private void ServiceSay(int target, string text)
        => Send(ChatPackets.NpcDialogue(target,
        [
            (DialogueEntry.Text, 0, text),
            (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
        ]), "(npc service)");

    private void Heal(int target, int silverPerPoint)
    {
        int missing = MaxHp - Hp + MaxSp - Sp;
        if (missing <= 0)
        {
            ServiceSay(target, "You are in good health. Go in peace.");
            return;
        }
        int cost = missing * silverPerPoint;
        if (Money < cost)
        {
            ServiceSay(target, $"Healing your wounds asks a donation of {cost} silver coins.");
            return;
        }
        if (cost > 0) SetMoney(Money - cost);
        SetHp(MaxHp);
        SetSp(MaxSp);
        Save();
        ServiceSay(target, cost > 0 ? $"Your wounds are healed. Thank you for your donation of {cost} silver coins." : "Your wounds are healed.");
        Log.Info(Tag, $"{Name} is healed for {cost}");
    }

    private void ShowDonations(int target)
    {
        var entries = new List<(DialogueEntry, int, string)>
        {
            (DialogueEntry.Text, 0, "The Lord loves a cheerful giver. Those who give to the Church are remembered in every port."),
        };
        for (int i = 0; i < Math.Min(Services.Donations.Count, ServiceOption.LastDonation - ServiceOption.FirstDonation + 1); i++)
            entries.Add((DialogueEntry.Option, ServiceOption.FirstDonation + i, $"Donate {Services.Donations[i]:N0} silver coins"));
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(donation)");
    }

    private void Donate(int target, int amount)
    {
        if (Money < amount)
        {
            ServiceSay(target, "You do not carry that much, child. God sees the will as well as the gift.");
            return;
        }
        SetMoney(Money - amount);
        GainReputation(amount / Math.Max(1, Services.SilverPerReputation));
        Save();
        ServiceSay(target, "God bless you for your generosity!");
        Log.Info(Tag, $"{Name} donates {amount}");
    }

    private void FieldRepair(int target)
    {
        EnsureShipState();
        if (ShipDamage <= 0)
        {
            ServiceSay(target, "Your ship does not need any repairs.");
            return;
        }
        SetShipHull(ShipHullMax);
        SetShipSail(FleetSailMax);
        Save();
        ServiceSay(target, "Your ship has been repaired.");
        Log.Info(Tag, $"{Name} repairs the ship at a field booth");
    }

    private void IdentifyArticles(int target)
    {
        var found = BboBagViews
            .SelectMany(view => MailBagOf(view)!.Values)
            .Where(stack => Services.AuthenticatingArticles.Contains(stack.Name))
            .Select(stack => stack.Name)
            .Distinct()
            .ToList();
        if (found.Count == 0)
        {
            ServiceSay(target, "Bring me Authenticating Articles from your travels and I will verify them for you.");
            return;
        }
        int count = 0;
        foreach (string name in found) count += TakeFromBags(name, BagTotal(name)).Sum(stack => stack.Amount);
        GainReputation(count * Services.ReputationPerArticle);
        RefreshLoad();
        Save();
        ServiceSay(target, $"I have verified {count} article{(count == 1 ? "" : "s")} for you. They are genuine.");
        Log.Info(Tag, $"{Name} authenticates {count} articles");
    }
}
