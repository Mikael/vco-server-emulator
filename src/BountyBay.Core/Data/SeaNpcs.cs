using System.Text.Json;
using BountyBay.Core.Configuration;

namespace BountyBay.Core.Data;

public sealed class SeaNpcDefinition
{
    public int Id { get; set; }
    public int Scene { get; set; }
    public int Model { get; set; } = 2011001;
    public string Name { get; set; } = "Pirate";
    public int Level { get; set; } = 1;
    public int Hull { get; set; } = 400;
    public int Sail { get; set; } = 300;
    public int Sailors { get; set; } = 30;
    public int Cannons { get; set; } = 2;
    public string Cannon { get; set; } = "IDName00000579";
    public string Ammunition { get; set; } = "IDName00000333";
    public int ArmourHull { get; set; }
    public int ArmourSail { get; set; }
    public int HitPercent { get; set; } = 60;
    public int Gold { get; set; } = 100;
    public int Reputation { get; set; }
    public int RespawnSeconds { get; set; } = 60;
    public int SailCloth { get; set; } = 2900000;
    public float X { get; set; }
    public float Z { get; set; }
    public int Heading { get; set; }
    public bool Roams { get; set; }
    public float RoamRadius { get; set; } = 250f;
    public bool Friendly { get; set; }
}

public sealed class AmmunitionDefinition
{
    public string Name { get; set; } = "";
    public int ShellType { get; set; } = 1;
    public double Hull { get; set; } = 1.4;
    public double Sailor { get; set; } = 0.03;
    public double Sail { get; set; } = 1.4;
    public int Price { get; set; } = 3;
    public int Lot { get; set; } = 10;
    public int Photo { get; set; } = 190104;
    public bool Sold { get; set; }
}

public sealed class CannonDefinition
{
    public string Name { get; set; } = "";
    public int Model { get; set; } = 2500001;
    public int Grade { get; set; } = 1;
    public int Attack { get; set; } = 10;
    public float Range { get; set; } = 150f;
    public float FireAngle { get; set; } = 90f;
    public float ReloadSeconds { get; set; } = 5f;
    public float BallSpeed { get; set; } = 60f;
    public int Weight { get; set; } = 3;
    public int Price { get; set; } = 50;
    public int Photo { get; set; } = 160101;
    public bool Sold { get; set; }
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
    public int HullMax { get; set; } = 400;
    public int CannonsPerSide { get; set; } = 2;
    public int StartCannonsPerSide { get; set; } = 2;
    public int StartAmmunition { get; set; } = 100;
    public int SailorPrice { get; set; } = 20;
    public int SailorsMax { get; set; } = 30;
    public int RepairPricePerPoint { get; set; } = 2;
    public int RenamePrice { get; set; } = 50;
    public int PortScene { get; set; } = 1001001;
    public int SinkSeconds { get; set; } = 30;
    public int AggroSeconds { get; set; } = 30;
    public float NpcPatrolRadius { get; set; } = 80f;
    public float NpcPatrolSpeed { get; set; } = 5f;
    public float NpcChaseSpeed { get; set; } = 9f;
    public int NpcPatrolMinSeconds { get; set; } = 20;
    public int NpcPatrolMaxSeconds { get; set; } = 45;
    public float NpcLeashDistance { get; set; } = 450f;
    public float BroadsideScatter { get; set; } = 6f;
    public int SupplyDaySeconds { get; set; } = 60;
    public int SupplyTicksPerDay { get; set; } = 24;
    public int SailorsPerProvision { get; set; } = 10;
    public float PursuitSpeedFactor { get; set; } = 1.25f;
    public float FullSpeedFactor { get; set; } = 1.5f;
    public int BaseHitPercent { get; set; } = 60;
    public int HitPercentPerCannonFightLevel { get; set; } = 3;
    public int GuardDamagePercent { get; set; } = 120;
    public int GuardHullTakenPercent { get; set; } = 80;
    public int PursuitHullTakenPercent { get; set; } = 120;
    public int PursuitHitPenalty { get; set; } = 15;
    public int FullSpeedSailPercent { get; set; } = 60;
    public bool LoseCannonsOnSinking { get; set; } = true;
    public int SunkDurability { get; set; } = 1;
    public int SinkReputationLossPercent { get; set; } = 1;
    public int FreeServiceTier { get; set; }
    public float MinimumSpeed { get; set; } = 3f;
    public int OverloadPercent { get; set; } = 70;
    public int ReferencePropulsion { get; set; } = 60;
    public float PropulsionWeight { get; set; } = 0.5f;
    public float NoSailSpeedFactor { get; set; } = 0.3f;
    public float NoCrewSpeedFactor { get; set; } = 0.3f;
    public float FullLoadSpeedFactor { get; set; } = 0.5f;
    public float WreckedTurnFactor { get; set; } = 0.5f;
    public int TurnRatePerPoint { get; set; } = 30;
    public int NpcVolleyMaxHullPercent { get; set; } = 20;
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
            string path = settings.Resolve("data\\game\\bbo\\seanpcs.json");
            _data = File.Exists(path)
                ? JsonSerializer.Deserialize<SeaData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new SeaData()
                : new SeaData();
            return _data;
        }
    }
}
