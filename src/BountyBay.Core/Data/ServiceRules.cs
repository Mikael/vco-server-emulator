using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class ServiceExchange
{
    public List<CompoundIngredient> Give { get; set; } = [];
    public int Fee { get; set; }
    public string Get { get; set; } = "";
    public int Amount { get; set; } = 1;
}

public sealed class ServiceExchangeGroup
{
    public string Title { get; set; } = "";
    public List<ServiceExchange> Entries { get; set; } = [];
}

public sealed class ServiceRules
{
    private static readonly object Gate = new();
    private static ServiceRules? _loaded;

    public int SilverPerHealPoint { get; set; } = 1;
    public List<int> Donations { get; set; } = [1000, 10000, 100000];
    public int SilverPerReputation { get; set; } = 1000;
    public int ReputationPerArticle { get; set; } = 50;
    public List<string> AuthenticatingArticles { get; set; } = [];
    public Dictionary<string, List<ServiceExchangeGroup>> Exchanges { get; set; } = [];

    public static ServiceRules For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;
            string path = settings.Resolve("data\\game\\services.json");
            try
            {
                _loaded = File.Exists(path)
                    ? JsonSerializer.Deserialize<ServiceRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new ServiceRules()
                    : new ServiceRules();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error("Services", $"Load failed: {exception.Message}");
                _loaded = new ServiceRules();
            }
            return _loaded;
        }
    }
}
