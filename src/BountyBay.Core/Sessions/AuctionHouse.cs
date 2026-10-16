using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;

namespace BountyBay.Core.Sessions;

public static class AuctionHouse
{
    private const int TickSeconds = 5;

    private static AuctionStore? _store;
    private static string _path = "";
    private static Timer? _timer;

    public static void Start(ServerContext context, CancellationToken cancellationToken)
    {
        if (_timer != null) return;
        _timer = new Timer(_ => Tick(context), null, TimeSpan.FromSeconds(TickSeconds), TimeSpan.FromSeconds(TickSeconds));
        cancellationToken.Register(() => _timer?.Dispose());
        Log.Info("Auction", "auction house started");
    }

    public static void Update(ServerContext context)
    {
        AuctionStore store = Store(context);
        AuctionHouseRules rules = AuctionRules.For(context.Settings).Auction;
        DateTime now = DateTime.UtcNow;
        if (!store.Settled && now >= store.EndsAt) Settle(context, store, rules);
        if (store.Settled && rules.Lots.Count > 0 && now >= store.NextStart) Begin(context, store, rules, now);
    }

    public static AuctionStore? Running(ServerContext context)
    {
        Update(context);
        AuctionStore store = Store(context);
        return !store.Settled && DateTime.UtcNow < store.EndsAt ? store : null;
    }

    public static TimeSpan UntilNext(ServerContext context)
    {
        AuctionStore store = Store(context);
        TimeSpan left = store.NextStart - DateTime.UtcNow;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public static int SecondsLeft(AuctionStore store)
        => (int)Math.Clamp((store.EndsAt - DateTime.UtcNow).TotalSeconds, 0, int.MaxValue);

    public static int? BidOf(ServerContext context, string name)
        => Store(context).Bids.FirstOrDefault(bid => Same(bid.Name, name))?.Amount;

    public static void PlaceBid(ServerContext context, string name, int amount)
    {
        Store(context).Bids.Add(new AuctionBid { Name = name, Amount = amount, At = DateTime.UtcNow });
        Save();
    }

    public static IReadOnlyList<AuctionRecord> History(ServerContext context) => Store(context).History;

    public static int PrizeCount(ServerContext context, string name) => Store(context).Prizes.Count(prize => Same(prize.Winner, name));

    public static List<AuctionPrize> TakePrizes(ServerContext context, string name)
    {
        AuctionStore store = Store(context);
        List<AuctionPrize> taken = store.Prizes.Where(prize => Same(prize.Winner, name)).ToList();
        if (taken.Count == 0) return taken;
        store.Prizes.RemoveAll(prize => Same(prize.Winner, name));
        Save();
        return taken;
    }

    public static void KeepPrize(ServerContext context, AuctionPrize prize)
    {
        Store(context).Prizes.Add(prize);
        Save();
    }

    public static long TakeRefund(ServerContext context, string name)
    {
        AuctionStore store = Store(context);
        if (!store.Refunds.Remove(name.ToLowerInvariant(), out long amount)) return 0;
        Save();
        return amount;
    }

    public static void Credit(ServerContext context, string name, long amount)
    {
        if (amount <= 0) return;
        AuctionStore store = Store(context);
        string key = name.ToLowerInvariant();
        store.Refunds[key] = store.Refunds.GetValueOrDefault(key) + amount;
        Save();
    }

    public static string Label(ServerContext context, string key)
        => context.Data.Items.Goods.TryGetValue(key, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : key;

    private static void Tick(ServerContext context)
    {
        try
        {
            lock (context.World.Sync) Update(context);
        }
        catch (Exception exception)
        {
            Log.Error("Auction", $"tick failed: {exception.Message}");
        }
    }

    private static void Begin(ServerContext context, AuctionStore store, AuctionHouseRules rules, DateTime now)
    {
        store.LotIndex = (Math.Max(-1, store.LotIndex) + 1) % rules.Lots.Count;
        AuctionLot lot = rules.Lots[store.LotIndex];
        store.Round++;
        store.Item = lot.Item;
        store.Amount = lot.Amount;
        store.ReservePrice = lot.ReservePrice;
        store.StartsAt = now;
        store.EndsAt = now.AddMinutes(rules.DurationMinutes);
        store.Settled = false;
        store.Bids.Clear();
        Save();
        Log.Info("Auction", $"round {store.Round}: {lot.Amount} x {lot.Item} from {lot.ReservePrice} until {store.EndsAt:HH:mm} UTC");
        foreach (ClientSession session in Watching(context)) session.RefreshAuction();
    }

    private static void Settle(ServerContext context, AuctionStore store, AuctionHouseRules rules)
    {
        store.Settled = true;
        store.NextStart = store.EndsAt.AddMinutes(rules.GapMinutes);
        string label = Label(context, store.Item);
        AuctionBid? best = store.Bids.OrderByDescending(bid => bid.Amount).ThenBy(bid => bid.At).FirstOrDefault();
        ClientSession? winner = best == null ? null : Online(context, best.Name);
        bool sold = best != null && (winner != null || !rules.ForfeitIfOffline);
        if (sold)
        {
            store.Prizes.Add(new AuctionPrize
            {
                Winner = best!.Name,
                Item = new ItemStack { Name = store.Item, Photo = context.Data.PhotoOf(store.Item), Amount = store.Amount },
                Price = best.Amount,
                Won = store.EndsAt,
            });
            store.History.Insert(0, new AuctionRecord
            {
                Winner = best.Name, Date = store.EndsAt, Item = store.Item, Amount = store.Amount, Price = best.Amount,
            });
            if (store.History.Count > rules.HistoryRows) store.History.RemoveRange(rules.HistoryRows, store.History.Count - rules.HistoryRows);
        }
        if (rules.RefundLosingBids)
        {
            foreach (AuctionBid losing in store.Bids.Where(entry => !sold || entry != best)) Credit(context, losing.Name, losing.Amount);
        }
        Save();
        if (best == null)
        {
            Log.Info("Auction", $"round {store.Round} ({store.Item}) ended without bids");
        }
        else if (!sold)
        {
            Log.Info("Auction", $"round {store.Round} ({store.Item}) forfeited: {best.Name} was offline at the end");
        }
        else
        {
            Log.Info("Auction", $"round {store.Round}: {best!.Name} wins {store.Amount} x {store.Item} for {best.Amount}");
            winner?.SystemMessage($"You won the auction for {label} with a bid of {best.Amount} silver coins. Collect it from a warehouse manager.");
            if (rules.AnnounceWinner)
            {
                foreach (ClientSession other in context.World.Sessions.Where(session => session.InWorld && session != winner).ToList())
                    other.SystemMessage($"{best.Name} won the auction for {label} with {best.Amount} silver coins.");
            }
        }
        foreach (ClientSession bidder in context.World.Sessions.Where(session => session.InWorld).ToList()) bidder.PayAuctionRefunds();
        foreach (ClientSession session in Watching(context)) session.RefreshAuction();
    }

    private static List<ClientSession> Watching(ServerContext context)
        => context.World.Sessions.Where(session => session.InWorld && session.AuctionWatching).ToList();

    private static ClientSession? Online(ServerContext context, string name)
        => context.World.Sessions.FirstOrDefault(session => session.InWorld && Same(session.Name, name));

    private static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static AuctionStore Store(ServerContext context)
    {
        if (_store != null) return _store;
        _path = context.Settings.Resolve("data\\auctions.json");
        _store = new AuctionStore();
        try
        {
            if (File.Exists(_path))
                _store = JsonSerializer.Deserialize<AuctionStore>(File.ReadAllText(_path), EmulatorSettings.Json) ?? new AuctionStore();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Auction", $"Load failed: {exception.Message}");
        }
        return _store;
    }

    private static void Save()
    {
        if (_store == null || _path.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_store, EmulatorSettings.Json));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Auction", $"Save failed: {exception.Message}");
        }
    }
}
