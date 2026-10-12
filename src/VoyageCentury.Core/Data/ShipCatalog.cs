using System.Text.Json;
using VoyageCentury.Core.Configuration;

namespace VoyageCentury.Core.Data;

public sealed class ShipDefinition
{
    public int Model { get; set; } = 2011001;
    public string Name { get; set; } = "IDName00004746";
    public string Label { get; set; } = "Ship";
    public int Photo { get; set; } = 200101;
    public int Kind { get; set; }
    public int Price { get; set; }
    public int Level { get; set; } = 1;
    public int Hull { get; set; } = 100;
    public int Sail { get; set; } = 100;
    public int Sailors { get; set; } = 30;
    public int Cannons { get; set; } = 2;
    public int CannonsMax { get; set; } = 2;
    public int Load { get; set; } = 100;
    public float Speed { get; set; } = 1f;
    public int Turn { get; set; } = 10;
    public int MastFront { get; set; } = 2211101;
    public int MastMiddle { get; set; } = 2211401;
    public int MastBack { get; set; } = 2211101;
    public bool Dyeable { get; set; } = true;
}

public sealed class DyeDefinition
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
    public bool Sail { get; set; }
    public string Label { get; set; } = "";
    public int Price { get; set; }
    public string Item { get; set; } = "";
    public int ItemCount { get; set; } = 1;

    public string Key => Name.Length > 0 ? Name : Value.ToString();
}

public sealed class SupplyDefinition
{
    public string Item { get; set; } = "";
    public string Label { get; set; } = "";
    public int Photo { get; set; }
    public int Price { get; set; } = 10;
}

public sealed class UpgradeMaterial
{
    public string Section { get; set; } = "";
    public int Key { get; set; } = 1;
    public string Item { get; set; } = "";
    public int Count { get; set; } = 1;
}

public sealed class FleetRules
{
    public int FleetMax { get; set; } = 4;
    public int MaxLevel { get; set; } = 4;
    public int UpgradeCost { get; set; } = 500;
    public int UpgradePricePercent { get; set; } = 20;
    public int HullPercentPerLevel { get; set; } = 15;
    public int SailPercentPerLevel { get; set; } = 15;
    public int LoadPercentPerLevel { get; set; } = 10;
    public int CannonsPerLevel { get; set; } = 1;
    public string Port { get; set; } = "Athens";
    public List<UpgradeMaterial> UpgradeMaterials { get; set; } = [];
    public double WearChance { get; set; } = 0.25;
    public int WearAlertPercent { get; set; } = 20;
    public int RepairPricePerPoint { get; set; } = 2;
}

public sealed class ShipCatalogData
{
    public FleetRules Rules { get; set; } = new();
    public List<ShipDefinition> Ships { get; set; } = [];
    public List<DyeDefinition> Dyes { get; set; } = [];
    public List<SupplyDefinition> Supplies { get; set; } = [];

    public ShipDefinition? Find(int model) => Ships.FirstOrDefault(ship => ship.Model == model);

    public DyeDefinition? FindDye(bool sail, string key)
        => Dyes.FirstOrDefault(dye => dye.Sail == sail && (dye.Key == key || dye.Value.ToString() == key
            || (dye.Label.Length > 0 && dye.Label.Equals(key, StringComparison.OrdinalIgnoreCase))));

    public SupplyDefinition? FindSupply(string item) => Supplies.FirstOrDefault(supply => supply.Item == item);
}

public static class ShipCatalog
{
    private static readonly object Gate = new();
    private static ShipCatalogData? _data;

    public static ShipCatalogData Load(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_data != null) return _data;
            string path = settings.Resolve("data\\game\\ships.json");
            try
            {
                _data = File.Exists(path)
                    ? JsonSerializer.Deserialize<ShipCatalogData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new ShipCatalogData()
                    : new ShipCatalogData();
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                _data = new ShipCatalogData();
            }
            return _data;
        }
    }
}
