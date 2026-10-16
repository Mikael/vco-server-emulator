using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class AuctionOption
{
    public const int First = 420;
    public const int Pawn = 420;
    public const int Prizes = 421;
    public const int Leave = 429;
    public const int Last = 439;
}

public sealed partial class ClientSession
{
    public const int AuctionView = 50;

    private bool _auctionViewOpen;
    private bool _auctionItemShown;
    private int _auctionHistoryShown;

    public bool AuctionWatching => _auctionViewOpen;

    public void AskAuction()
    {
        AuctionHouse.Update(Context);
        if (!_auctionViewOpen)
        {
            _auctionViewOpen = true;
            _auctionItemShown = false;
            _auctionHistoryShown = 0;
            Send(ViewPackets.Create(AuctionView, 1), "(auction view)");
        }
        PayAuctionRefunds();
        RefreshAuction();
        if (AuctionHouse.Running(Context) == null)
        {
            TimeSpan wait = AuctionHouse.UntilNext(Context);
            SystemMessage(AuctionRules.For(Context.Settings).Auction.Lots.Count == 0
                ? "The auction house has nothing to offer at the moment."
                : $"The next auction starts in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minute(s).", SystemMessageKind.Prompt);
        }
        Log.Debug(Tag, $"{Name} looks at the auction house");
    }

    public void RefreshAuction()
    {
        if (!_auctionViewOpen) return;
        if (_auctionItemShown)
        {
            _auctionItemShown = false;
            Send(ViewPackets.RemoveItem(AuctionView, 1), "(auction lot cleared)");
        }
        AuctionStore? lot = AuctionHouse.Running(Context);
        if (lot != null)
        {
            var item = new ItemStack { Name = lot.Item, Photo = Context.Data.PhotoOf(lot.Item), Amount = lot.Amount };
            _auctionItemShown = true;
            Send(ViewPackets.AddItem(AuctionView, 1, ItemProperties(item, 1)), "(auction lot)");
            Send(AuctionPackets.ItemInfo(lot.ReservePrice, AuctionHouse.Label(Context, lot.Item), AuctionHouse.SecondsLeft(lot),
                AuctionHouse.BidOf(Context, Name)), "(auction lot info)");
        }
        SendAuctionHistory();
    }

    public void AuctionBid(int amount)
    {
        AuctionStore? lot = AuctionHouse.Running(Context);
        if (lot == null)
        {
            SystemMessage("No auction is running at the moment.", SystemMessageKind.Prompt);
            RefreshAuction();
            return;
        }
        if (AuctionHouse.BidOf(Context, Name) != null)
        {
            SystemMessage("You can bid only once in each auction.", SystemMessageKind.Prompt);
            RefreshAuction();
            return;
        }
        if (amount < lot.ReservePrice)
        {
            SystemMessage($"Your bid must be at least the reserve price of {lot.ReservePrice} silver coins.", SystemMessageKind.Prompt);
            RefreshAuction();
            return;
        }
        if (Money < amount)
        {
            SystemMessage("You do not have that many silver coins.", SystemMessageKind.Prompt);
            RefreshAuction();
            return;
        }
        SetMoney(Money - amount);
        AuctionHouse.PlaceBid(Context, Name, amount);
        Save();
        RefreshAuction();
        SystemMessage($"You bid {amount} silver coins for {AuctionHouse.Label(Context, lot.Item)}.");
        Log.Info(Tag, $"{Name} bids {amount} in auction round {lot.Round} ({lot.Item})");
    }

    public void PayAuctionRefunds()
    {
        long owed = AuctionHouse.TakeRefund(Context, Name);
        if (owed <= 0) return;
        long pay = Math.Min(owed, (long)Rule.Economy.SilverCap - Money);
        if (pay > 0)
        {
            SetMoney(Money + pay);
            SystemMessage($"The auction house returns {pay} silver coins of your bid.");
            Save();
        }
        if (owed > pay) AuctionHouse.Credit(Context, Name, owed - pay);
    }

    public void AuctionLoginNotice()
    {
        AuctionHouse.Update(Context);
        PayAuctionRefunds();
        int prizes = AuctionHouse.PrizeCount(Context, Name);
        if (prizes > 0) SystemMessage($"{prizes} auction prize(s) wait for you at the warehouse manager.");
    }

    public bool HandleAuctionOption(int target, int option)
    {
        if (option < AuctionOption.First || option > AuctionOption.Last) return false;
        switch (option)
        {
            case AuctionOption.Pawn:
                OpenImpawn();
                Send(ChatPackets.NpcDialogue(target,
                [
                    (DialogueEntry.Text, 0, "Put the articles you want to pawn on the counter and ask for my price."),
                    (DialogueEntry.Option, AuctionOption.Leave, "Goodbye"),
                ]), "(pawn shop)");
                return true;
            case AuctionOption.Prizes:
                int handed = CollectAuctionPrizes();
                Send(ChatPackets.NpcDialogue(target,
                [
                    (DialogueEntry.Text, 0, handed > 0
                        ? "Here is what you won at the auction house."
                        : AuctionHouse.PrizeCount(Context, Name) > 0
                            ? "Your bag and your warehouse are full. Make room and come back."
                            : "There is no auction prize waiting for you."),
                    (DialogueEntry.Option, AuctionOption.Leave, "Goodbye"),
                ]), "(auction prizes)");
                return true;
            default:
                Send(ChatPackets.NpcDialogue(target, [(DialogueEntry.Text, 0, $"Fair winds, {Name}.")]), "(npc dialogue)");
                return true;
        }
    }

    public int CollectAuctionPrizes()
    {
        int handed = 0;
        foreach (AuctionPrize prize in AuctionHouse.TakePrizes(Context, Name))
        {
            if (GiveItem(prize.Item.Clone(), "(auction prize)") || WarehouseStore(prize.Item.Clone()))
            {
                handed++;
                Log.Info(Tag, $"{Name} collects the auction prize {prize.Item.Amount} x {prize.Item.Name}");
                continue;
            }
            AuctionHouse.KeepPrize(Context, prize);
        }
        if (handed > 0)
        {
            RefreshLoad();
            Save();
        }
        return handed;
    }

    private void SendAuctionHistory()
    {
        IReadOnlyList<AuctionRecord> history = AuctionHouse.History(Context);
        for (int row = 0; row < history.Count; row++)
        {
            AuctionRecord record = history[row];
            string item = record.Amount > 1 ? $"{AuctionHouse.Label(Context, record.Item)} x{record.Amount}" : AuctionHouse.Label(Context, record.Item);
            Send(AuctionPackets.History(row, record.Date.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), record.Winner, item,
                record.Price), "(auction history)");
        }
        for (int row = history.Count; row < _auctionHistoryShown; row++)
            Send(AuctionPackets.History(row, "-", "-", "-", 0), "(auction history cleared)");
        _auctionHistoryShown = Math.Max(_auctionHistoryShown, history.Count);
    }
}
