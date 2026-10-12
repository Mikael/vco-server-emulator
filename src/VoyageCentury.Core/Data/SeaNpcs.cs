using System.Text.Json;
using VoyageCentury.Core.Configuration;

namespace VoyageCentury.Core.Data;

public sealed class SeaNpcDefinition
{
    public int Id { get; set; }
    public int Scene { get; set; }
    public int Model { get; set; } = 2011001;
    public string Name { get; set; } = "Pirate";
    public int Level { get; set; } = 1;
    public int Hull { get; set; } = 100;
    public int Cannons { get; set; } = 2;
    public int CannonModel { get; set; } = 2500001;
    public int ShellType { get; set; } = 1;
    public int DamageMin { get; set; } = 4;
    public int DamageMax { get; set; } = 8;
    public float Accuracy { get; set; } = 0.6f;
    public float Range { get; set; } = 150f;
    public float ReloadSeconds { get; set; } = 6f;
    public int Gold { get; set; } = 100;
    public int Experience { get; set; } = 50;
    public int RespawnSeconds { get; set; } = 60;
    public int SailCloth { get; set; } = 2900000;
    public float X { get; set; }
    public float Z { get; set; }
    public int Heading { get; set; }
}

public sealed class AmmunitionDefinition
{
    public string Name { get; set; } = "";
    public int ShellType { get; set; } = 1;
    public int DamageMin { get; set; } = 8;
    public int DamageMax { get; set; } = 14;
    public int Price { get; set; } = 5;
    public int Lot { get; set; } = 10;
}

public sealed class CannonDefinition
{
    public string Name { get; set; } = "";
    public int Model { get; set; } = 2500001;
    public float Range { get; set; } = 150f;
    public float FireAngle { get; set; } = 60f;
    public float ReloadSeconds { get; set; } = 5f;
    public float Accuracy { get; set; } = 0.75f;
    public float BallSpeed { get; set; } = 60f;
    public int Price { get; set; } = 300;
}

public sealed class SailClothDefinition
{
    public int Cloth { get; set; }
    public string Name { get; set; } = "";
    public string Item { get; set; } = "";
    public int Price { get; set; } = 100;
}

public sealed class ShipRules
{
    public int HullMax { get; set; } = 100;
    public int CannonsPerSide { get; set; } = 2;
    public int StartCannonsPerSide { get; set; } = 2;
    public int StartAmmunition { get; set; } = 60;
    public int SailorPrice { get; set; } = 20;
    public int SailorsMax { get; set; } = 30;
    public int RepairPricePerPoint { get; set; } = 2;
    public int RenamePrice { get; set; } = 50;
    public int PortScene { get; set; } = 1001001;
    public int SinkSeconds { get; set; } = 8;
    public int AggroSeconds { get; set; } = 30;
    public float BroadsideScatter { get; set; } = 6f;
}

public sealed class SeaData
{
    public ShipRules Rules { get; set; } = new();
    public List<SeaNpcDefinition> Ships { get; set; } = [];
    public List<AmmunitionDefinition> Ammunition { get; set; } = [];
    public List<CannonDefinition> Cannons { get; set; } = [];
    public List<SailClothDefinition> SailCloths { get; set; } = [];

    public AmmunitionDefinition? FindAmmunition(string name) => Ammunition.FirstOrDefault(entry => entry.Name == name);

    public CannonDefinition? FindCannon(string name) => Cannons.FirstOrDefault(entry => entry.Name == name);

    public CannonDefinition DefaultCannon => Cannons.Count > 0 ? Cannons[0] : new CannonDefinition { Name = "IDName00000579" };

    public AmmunitionDefinition DefaultAmmunition => Ammunition.Count > 0 ? Ammunition[0] : new AmmunitionDefinition { Name = "IDName00000333" };
}

public static class SeaNpcs
{
    private static readonly object Gate = new();
    private static SeaData? _data;

    public static SeaData Load(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_data != null) return _data;
            string path = settings.Resolve("data\\game\\seanpcs.json");
            _data = File.Exists(path)
                ? JsonSerializer.Deserialize<SeaData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new SeaData()
                : new SeaData();
            return _data;
        }
    }
}
