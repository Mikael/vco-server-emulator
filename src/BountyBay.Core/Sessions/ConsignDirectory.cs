using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;

namespace BountyBay.Core.Sessions;

public sealed class ConsignListing
{
    public int Id { get; set; }
    public string Seller { get; set; } = "";
    public ItemStack Item { get; set; } = new();
    public int Price { get; set; }
    public int PayType { get; set; }
    public string Password { get; set; } = "";
    public DateTime Expires { get; set; }
}

public sealed class ConsignStore
{
    public int NextId { get; set; } = 1;
    public List<ConsignListing> Listings { get; set; } = [];
    public List<ConsignListing> Returns { get; set; } = [];
    public Dictionary<string, long> Proceeds { get; set; } = [];
}

public static class ConsignTables
{
    public const int Market = 40;
    public const int Mine = 41;
    public const int MarketColumns = 7;
    public const int MineColumns = 6;
    public const int RowsPerMessage = 8;
    public const string NoPassword = " ";
}

public static class ConsignDirectory
{
    public const int PageSize = 10;
    public const int ListingsPerSeller = 10;
    public const int MoneyPayType = 3;
    public const int PointPayType = 4;
    public const int ShortHours = 24;
    public const int LongHours = 48;
    public const int FeePercent = 1;

    private static ConsignStore? _store;
    private static string _path = "";

    public static int Fee(int price) => (int)Math.Max(1, (long)price * FeePercent / 100);

    public static List<ConsignListing> Of(ServerContext context, string seller)
        => Store(context).Listings.Where(listing => Same(listing.Seller, seller)).OrderBy(listing => listing.Id).ToList();

    public static List<ConsignListing> Search(ServerContext context, string filter)
        => Store(context).Listings
            .Where(listing => filter.Length == 0 || listing.Item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || listing.Seller.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(listing => listing.Id)
            .ToList();

    public static ConsignListing? Find(ServerContext context, int id) => Store(context).Listings.FirstOrDefault(listing => listing.Id == id);

    public static ConsignListing Add(ServerContext context, string seller, ItemStack item, int price, int payType, int hours, string password)
    {
        ConsignStore store = Store(context);
        var listing = new ConsignListing
        {
            Id = store.NextId++,
            Seller = seller,
            Item = item,
            Price = price,
            PayType = payType,
            Password = password,
            Expires = DateTime.UtcNow.AddHours(hours),
        };
        store.Listings.Add(listing);
        Save();
        return listing;
    }

    public static ConsignListing? Remove(ServerContext context, int id)
    {
        ConsignStore store = Store(context);
        ConsignListing? listing = store.Listings.FirstOrDefault(entry => entry.Id == id);
        if (listing == null) return null;
        store.Listings.Remove(listing);
        Save();
        return listing;
    }

    public static void Credit(ServerContext context, string seller, long amount)
    {
        if (amount <= 0) return;
        ConsignStore store = Store(context);
        string key = seller.ToLowerInvariant();
        store.Proceeds[key] = store.Proceeds.GetValueOrDefault(key) + amount;
        Save();
    }

    public static long TakeProceeds(ServerContext context, string seller)
    {
        ConsignStore store = Store(context);
        if (!store.Proceeds.Remove(seller.ToLowerInvariant(), out long amount)) return 0;
        Save();
        return amount;
    }

    public static List<ConsignListing> TakeReturns(ServerContext context, string seller)
    {
        ConsignStore store = Store(context);
        List<ConsignListing> back = store.Returns.Where(listing => Same(listing.Seller, seller)).ToList();
        if (back.Count == 0) return back;
        store.Returns.RemoveAll(listing => Same(listing.Seller, seller));
        Save();
        return back;
    }

    public static void KeepReturn(ServerContext context, ConsignListing listing)
    {
        Store(context).Returns.Add(listing);
        Save();
    }

    public static string TimeLeft(ConsignListing listing)
    {
        TimeSpan left = listing.Expires - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) return "0h";
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}h" : $"{Math.Max(1, (int)left.TotalMinutes)}m";
    }

    private static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static ConsignStore Store(ServerContext context)
    {
        if (_store == null)
        {
            _path = context.Settings.Resolve("data\\consign.json");
            _store = new ConsignStore();
            try
            {
                if (File.Exists(_path))
                    _store = JsonSerializer.Deserialize<ConsignStore>(File.ReadAllText(_path), EmulatorSettings.Json) ?? new ConsignStore();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error("Consign", $"Load failed: {exception.Message}");
            }
        }
        Expire(_store);
        return _store;
    }

    private static void Expire(ConsignStore store)
    {
        DateTime now = DateTime.UtcNow;
        List<ConsignListing> expired = store.Listings.Where(listing => listing.Expires <= now).ToList();
        if (expired.Count == 0) return;
        foreach (ConsignListing listing in expired)
        {
            store.Listings.Remove(listing);
            store.Returns.Add(listing);
            Log.Info("Consign", $"listing {listing.Id} of {listing.Seller} ({listing.Item.Amount} x {listing.Item.Name}) expired");
        }
        Save();
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
            Log.Error("Consign", $"Save failed: {exception.Message}");
        }
    }
}
