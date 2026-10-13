using System.Text.Json;
using VoyageCentury.Core.Configuration;

namespace VoyageCentury.Core.Data;

public sealed class ShipClass
{
    public int Kind { get; set; }
    public int Tier { get; set; }
    public int Cannons { get; set; } = 4;
    public int Hull { get; set; } = 400;
    public int Sail { get; set; } = 300;
    public int Sailors { get; set; } = 30;
    public int Load { get; set; } = 300;
    public int Triangular { get; set; } = 60;
    public int Square { get; set; }
    public int Turning { get; set; } = 5;
    public int Voyage { get; set; }
    public int SecondSkill { get; set; }
}

public sealed class ShipDefinition
{
    public int Model { get; set; } = 2011001;
    public string Name { get; set; } = "IDName00004695";
    public string Label { get; set; } = "Ship";
    public int Photo { get; set; } = 200101;
    public int Kind { get; set; }
    public int Tier { get; set; }
    public int Price { get; set; }
    public bool Sold { get; set; }
    public int Cannons { get; set; } = 4;
    public int Hull { get; set; } = 400;
    public int Sail { get; set; } = 300;
    public int Sailors { get; set; } = 30;
    public int Load { get; set; } = 300;
    public int Triangular { get; set; } = 60;
    public int Square { get; set; }
    public int Turning { get; set; } = 5;
    public int Voyage { get; set; }
    public int SecondSkill { get; set; }
    public int MastFront { get; set; } = 2211101;
    public int MastMiddle { get; set; } = 2211401;
    public int MastBack { get; set; } = 2211101;
    public bool Dyeable { get; set; } = true;

    public int CannonsPerSide => Math.Max(0, Cannons / 2);

    public string SecondSkillKey => Kind == 1 ? "SKILL_SPEECHCRAFT" : "SKILL_SEABATTLE";

    public void Take(ShipClass stats)
    {
        Cannons = stats.Cannons;
        Hull = stats.Hull;
        Sail = stats.Sail;
        Sailors = stats.Sailors;
        Load = stats.Load;
        Triangular = stats.Triangular;
        Square = stats.Square;
        Turning = stats.Turning;
        Voyage = stats.Voyage;
        SecondSkill = stats.SecondSkill;
    }
}

public sealed class RebuildStep
{
    public int Tier { get; set; } = 1;
    public int Silver { get; set; }
    public int Count { get; set; }
    public List<int> Timber { get; set; } = [];
    public List<int> Metal { get; set; } = [];
    public int AttributeLevel { get; set; }
    public List<string> Cities { get; set; } = [];
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

public sealed class FleetRules
{
    public int FleetMax { get; set; } = 4;
    public string Port { get; set; } = "Athens";
    public List<int> RebuildTiers { get; set; } = [1];
    public int AttributeLevelsPerTier { get; set; } = 10;
    public int ManeuverSail { get; set; } = 20;
    public int WeaponSail { get; set; } = 10;
    public int WeaponHull { get; set; } = 10;
    public int StructureHull { get; set; } = 40;
    public int StructureLoad { get; set; } = 5;
    public int PlainSailCloth { get; set; } = 2900000;
    public List<string> Timber { get; set; } = [];
    public List<string> Metal { get; set; } = [];
    public double WearChance { get; set; } = 0.25;
    public int WearAlertPercent { get; set; } = 20;
    public int RepairPricePerPoint { get; set; } = 2;
}

public sealed class ShipCatalogData
{
    public FleetRules Rules { get; set; } = new();
    public List<ShipClass> Classes { get; set; } = [];
    public List<RebuildStep> Rebuilds { get; set; } = [];
    public List<ShipDefinition> Ships { get; set; } = [];
    public List<DyeDefinition> Dyes { get; set; } = [];
    public List<SupplyDefinition> Supplies { get; set; } = [];

    public ShipDefinition? Find(int model) => Ships.FirstOrDefault(ship => ship.Model == model);

    public ShipDefinition? Find(int kind, int tier) => Ships.FirstOrDefault(ship => ship.Kind == kind && ship.Tier == tier);

    public ShipClass? FindClass(int kind, int tier) => Classes.FirstOrDefault(entry => entry.Tier == tier && (tier == 0 || entry.Kind == kind));

    public RebuildStep? FindRebuild(int tier) => Rebuilds.FirstOrDefault(step => step.Tier == tier);

    public DyeDefinition? FindDye(bool sail, string key)
        => Dyes.FirstOrDefault(dye => dye.Sail == sail && (dye.Key == key || dye.Value.ToString() == key
            || (dye.Label.Length > 0 && dye.Label.Equals(key, StringComparison.OrdinalIgnoreCase))));

    public SupplyDefinition? FindSupply(string item) => Supplies.FirstOrDefault(supply => supply.Item == item);

    public void Resolve()
    {
        foreach (ShipDefinition ship in Ships)
        {
            if (FindClass(ship.Kind, ship.Tier) is { } stats) ship.Take(stats);
        }
    }
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
            _data.Resolve();
            return _data;
        }
    }
}
