using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public void ShowGuildMatches(int list)
    {
        GuildMatchFile file = GuildMatchDirectory.Refresh(Context);
        Send(GuildMatchPackets.ClearList(list), "(guild match bracket)");
        foreach (GuildMatchSlot slot in file.Slots.OrderBy(slot => slot.Slot))
        {
            if (slot.Left.Length == 0 && slot.Right.Length == 0) continue;
            Send(GuildMatchPackets.Slot(list, slot.Slot, slot.Left, slot.Right, slot.State), "(guild match)");
        }
        if (list != 2 && file.Champion.Length > 0) Send(GuildMatchPackets.Champion(file.Champion), "(guild match champion)");
    }

    public void ViewGuildMatchWager(int slotNumber, int list)
    {
        GuildMatchFile file = GuildMatchDirectory.Refresh(Context);
        GuildMatchSlot slot = GuildMatchDirectory.Slot(file, slotNumber);
        if (slot.State != GuildMatchPackets.Betting)
        {
            SystemMessage("Wagers are not accepted for this match now.", SystemMessageKind.Prompt);
            return;
        }
        GuildMatchRules rules = GuildMatchDirectory.Rules(Context);
        Send(GuildMatchPackets.Detail(list, slot.Slot, GuildMatchDirectory.Side(Context, slot.Left), GuildMatchDirectory.Side(Context, slot.Right),
            rules.WagerTypes.Select(wager => (wager.Name, wager.Unit))), "(guild match wager)");
    }

    public void ViewGuildMatchReport(int slotNumber)
    {
        GuildMatchFile file = GuildMatchDirectory.Refresh(Context);
        GuildMatchSlot slot = GuildMatchDirectory.Slot(file, slotNumber);
        if (slot.State < GuildMatchPackets.LeftWon)
        {
            SystemMessage("This match has not been fought yet.", SystemMessageKind.Prompt);
            return;
        }
        Send(GuildMatchPackets.Report(slot.Left.Length > 0 ? slot.Left : "-", slot.Right.Length > 0 ? slot.Right : "-", slot.Score, slot.Rounds),
            "(guild match report)");
    }

    public void WagerGuildMatch(int slotNumber, int side, string type, int amountIndex)
    {
        GuildMatchFile file = GuildMatchDirectory.Refresh(Context);
        GuildMatchSlot slot = GuildMatchDirectory.Slot(file, slotNumber);
        GuildMatchRules rules = GuildMatchDirectory.Rules(Context);
        GuildMatchWager? wager = rules.WagerTypes.FirstOrDefault(entry => UiPackets.Token(entry.Name) == type || entry.Name == type.Replace(UiPackets.Blank, ' '));
        if (slot.State != GuildMatchPackets.Betting || side is < 1 or > 2 || amountIndex is < 1 or > 3 || wager == null)
        {
            SystemMessage("This wager cannot be placed.", SystemMessageKind.Prompt);
            return;
        }
        if (file.Bets.Any(bet => bet.Season == file.Season && bet.Slot == slot.Slot && GuildDirectory.Same(bet.Player, Name)))
        {
            SystemMessage("You have already placed a wager on this match.", SystemMessageKind.Prompt);
            return;
        }
        int honor = Properties.TryGetValue("HonorLevel", out object? level) ? Convert.ToInt32(level) : 0;
        long amount = (long)Math.Max(1, honor) * wager.Unit * amountIndex;
        if (Money < amount)
        {
            SystemMessage($"This wager costs {amount} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        SetMoney(Money - amount);
        string guild = side == 1 ? slot.Left : slot.Right;
        var bet = new GuildMatchBet
        {
            Id = $"{file.Season.Replace("-", "")}{file.NextBet++}",
            Player = Name,
            Season = file.Season,
            Slot = slot.Slot,
            Side = side,
            Guild = guild,
            Match = $"{slot.Left} vs {slot.Right}",
            Type = wager.Name,
            Amount = amount,
        };
        file.Bets.Add(bet);
        GuildMatchStore.Save();
        Save();
        SystemMessage($"You wager {amount} silver coins on {guild}.");
        Log.Info(Tag, $"{Name} wagers {amount} on {guild} in match {slot.Slot}");
    }

    public void ViewGuildMatchBets()
    {
        GuildMatchFile file = GuildMatchDirectory.Refresh(Context);
        Send(GuildMatchPackets.ClearBets(), "(my wagers)");
        IEnumerable<GuildMatchBetRow> rows = file.Bets.Where(bet => GuildDirectory.Same(bet.Player, Name))
            .Select(bet => new GuildMatchBetRow(bet.Id, 1, GuildMatchDirectory.Round(bet.Slot), bet.Match, bet.Guild, bet.Type, bet.Amount,
                bet.Result, bet.Reward));
        foreach (byte[] packet in GuildMatchPackets.Bets(rows)) Send(packet, "(my wagers)");
    }

    public void ClaimGuildMatchReward(string id)
    {
        GuildMatchFile file = GuildMatchDirectory.Refresh(Context);
        GuildMatchBet? bet = file.Bets.FirstOrDefault(entry => entry.Id == id && GuildDirectory.Same(entry.Player, Name));
        if (bet == null) return;
        if (bet.Result == 0)
        {
            SystemMessage("This match has not been decided yet.", SystemMessageKind.Prompt);
            return;
        }
        file.Bets.Remove(bet);
        if (bet.Result is GuildMatchDirectory.Profit or GuildMatchDirectory.Returned && bet.Reward > 0)
        {
            SetMoney(Money + bet.Reward);
            SystemMessage($"You receive {bet.Reward} silver coins from your wager.");
        }
        GuildMatchStore.Save();
        Save();
        Send(GuildMatchPackets.RemoveBet(bet.Id), "(my wagers)");
        Log.Info(Tag, $"{Name} claims wager {bet.Id}: {bet.Reward}");
    }
}
