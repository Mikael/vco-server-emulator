using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class ParcelCostRules
{
    public double CostPerArticle { get; set; } = 50;
    public double CostPerWeight { get; set; } = 10;
    public int Minimum { get; set; } = 1;
    public int Maximum { get; set; } = 100000;
}

public static class ParcelCostCatalog
{
    private static readonly object Gate = new();
    private static ParcelCostRules? _rules;

    public static ParcelCostRules Get(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_rules != null) return _rules;
            string path = settings.Resolve("data\\game\\ParcelCost.json");
            try
            {
                _rules = File.Exists(path)
                    ? JsonSerializer.Deserialize<ParcelCostRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new ParcelCostRules()
                    : new ParcelCostRules();
            }
            catch (JsonException error)
            {
                Log.Warn("parcel", $"{path}: {error.Message}");
                _rules = new ParcelCostRules();
            }
            return _rules;
        }
    }
}
