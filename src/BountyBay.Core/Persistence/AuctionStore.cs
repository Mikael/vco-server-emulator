namespace BountyBay.Core.Persistence;

public sealed class AuctionBid
{
    public string Name { get; set; } = "";
    public int Amount { get; set; }
    public DateTime At { get; set; }
}

public sealed class AuctionRecord
{
    public string Winner { get; set; } = "";
    public DateTime Date { get; set; }
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
    public int Price { get; set; }
}

public sealed class AuctionPrize
{
    public string Winner { get; set; } = "";
    public ItemStack Item { get; set; } = new();
    public int Price { get; set; }
    public DateTime Won { get; set; }
}

public sealed class AuctionStore
{
    public int Round { get; set; }
    public int LotIndex { get; set; } = -1;
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
    public int ReservePrice { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool Settled { get; set; } = true;
    public DateTime NextStart { get; set; }
    public List<AuctionBid> Bids { get; set; } = [];
    public List<AuctionRecord> History { get; set; } = [];
    public List<AuctionPrize> Prizes { get; set; } = [];
    public Dictionary<string, long> Refunds { get; set; } = [];
}
