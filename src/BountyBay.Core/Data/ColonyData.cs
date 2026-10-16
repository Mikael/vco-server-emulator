using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class ColonyRouteButton
{
    public string Name { get; set; } = "";
    public int Max { get; set; } = 1;
    public int RequiresIndex { get; set; } = -1;
    public int RequiresLevel { get; set; }
}

public sealed class ColonyRoute
{
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public List<ColonyRouteButton> Buttons { get; set; } = [];
}

public sealed class ColonyUnion
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Headquarters { get; set; } = "";
}

public sealed class ColonyData
{
    private static readonly object Gate = new();
    private static ColonyData? _loaded;

    public int FoundCost { get; set; } = 100000;
    public int FounderDuty { get; set; } = 4;
    public int DevelopDuty { get; set; } = 3;
    public int NameLength { get; set; } = 16;
    public List<string> Styles { get; set; } = ["Pastoral Village", "Industrial Town", "Pirate Hideout"];
    public List<int> LevelContribution { get; set; } = [0, 1000, 5000, 20000, 50000];
    public List<int> LevelUnionContribution { get; set; } = [0, 0, 0, 0, 0];
    public int PointsPerLevel { get; set; } = 5;
    public int CityType { get; set; } = 2;
    public int IndexPerLevel { get; set; } = 100;
    public int IndexMax { get; set; } = 1000;
    public int BasePopulation { get; set; } = 1000;
    public int PopulationPerLevel { get; set; } = 500;
    public int BaseFood { get; set; } = 1000;
    public int FoodPerGatheringLevel { get; set; } = 100;
    public int UnionTarget { get; set; } = 20000;
    public int UnionPageSize { get; set; } = 3;
    public List<ColonyUnion> Unions { get; set; } = [];
    public List<ColonyRoute> Routes { get; set; } = [];

    public ColonyRoute? Route(int type) => Routes.FirstOrDefault(route => route.Type == type);

    public ColonyUnion? Union(string code) => Unions.FirstOrDefault(union => union.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public static ColonyData For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;
            string path = settings.Resolve("data\\game\\colony.json");
            try
            {
                _loaded = File.Exists(path)
                    ? JsonSerializer.Deserialize<ColonyData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new ColonyData()
                    : new ColonyData();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error("Colony", $"Load failed: {exception.Message}");
                _loaded = new ColonyData();
            }
            if (_loaded.Unions.Count == 0)
            {
                _loaded.Unions.AddRange(
                [
                    new ColonyUnion { Code = "ldl", Name = "The Nederland's Merchant's Alliance", Headquarters = "Amsterdam" },
                    new ColonyUnion { Code = "bld", Name = "The English Channel Merchant's Alliance", Headquarters = "London" },
                    new ColonyUnion { Code = "xdzh", Name = "The Western Mediterranean Merchant's Alliance", Headquarters = "Genoa" },
                    new ColonyUnion { Code = "ybly", Name = "The Iberian Merchant's Alliance", Headquarters = "Lisbon" },
                    new ColonyUnion { Code = "ysl", Name = "The Islandi Merchant's Alliance", Headquarters = "Istanbul" },
                ]);
            }
            return _loaded;
        }
    }
}
