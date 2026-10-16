using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class AuctionLot
{
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
    public int ReservePrice { get; set; } = 1;
}

public sealed class AuctionHouseRules
{
    public int DurationMinutes { get; set; } = 60;
    public int GapMinutes { get; set; } = 5;
    public bool RefundLosingBids { get; set; }
    public bool ForfeitIfOffline { get; set; } = true;
    public bool AnnounceWinner { get; set; } = true;
    public int HistoryRows { get; set; } = 20;
    public List<AuctionLot> Lots { get; set; } = [];
}

public sealed class ImpawnRules
{
    public string PayItem { get; set; } = "GLMoney";
    public int SilverPerCoin { get; set; } = 1000;
    public int MinPercent { get; set; } = 30;
    public int MaxPercent { get; set; } = 60;
    public int MaxCancels { get; set; } = 5;
    public int CancelWindowMinutes { get; set; } = 10;
    public int BanMinutes { get; set; } = 10;
    public List<string> Refuse { get; set; } = [];
}

public sealed class AuctionRules
{
    private static readonly object Gate = new();
    private static AuctionRules? _current;

    public AuctionHouseRules Auction { get; set; } = new();
    public ImpawnRules Pawn { get; set; } = new();

    public static AuctionRules For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\auction.json"));
    }

    private static AuctionRules Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("Auction", $"game data file is missing: {path}");
            return new AuctionRules();
        }
        try
        {
            AuctionRules rules = JsonSerializer.Deserialize<AuctionRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new AuctionRules();
            rules.Auction.Lots = rules.Auction.Lots.Where(lot => lot.Item.Length > 0 && lot.Amount > 0 && lot.ReservePrice > 0).ToList();
            rules.Auction.DurationMinutes = Math.Max(1, rules.Auction.DurationMinutes);
            rules.Auction.GapMinutes = Math.Max(0, rules.Auction.GapMinutes);
            rules.Pawn.MinPercent = Math.Clamp(rules.Pawn.MinPercent, 1, 100);
            rules.Pawn.MaxPercent = Math.Clamp(rules.Pawn.MaxPercent, rules.Pawn.MinPercent, 100);
            rules.Pawn.SilverPerCoin = Math.Max(1, rules.Pawn.SilverPerCoin);
            Log.Info("Auction", $"{rules.Auction.Lots.Count} auction lot(s) loaded");
            return rules;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Auction", $"Could not read {path}: {exception.Message}");
            return new AuctionRules();
        }
    }
}
