using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

// Planting (data\game\bbo\planting.json, built by tools\bbo\extract\planting.py).
public sealed class SeedDefinition
{
    public string Seed { get; set; } = "";
    public string Crop { get; set; } = "";
    public string Name { get; set; } = "";
    public int Grade { get; set; } = 1;
    public int Model { get; set; } = 4000548;
    public int GrowSeconds { get; set; } = 180;
    public int[] Yield { get; set; } = [2, 4];
}

public sealed class FarmDefinition
{
    public int Scene { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
}

public sealed class ByProduct
{
    public string Item { get; set; } = "";
    public int Grade { get; set; } = 1;
}

public sealed class PlantingBook
{
    private static readonly object Gate = new();
    private static PlantingBook? _current;

    public float FarmRadius { get; set; } = 30f;
    public float HarvestReach { get; set; } = 4f;
    public int HarvestSeconds { get; set; } = 3;
    public int ExperiencePerGrade { get; set; } = 40;
    public List<ByProduct> ByProducts { get; set; } = [];
    public List<SeedDefinition> Seeds { get; set; } = [];
    public List<FarmDefinition> Farms { get; set; } = [];

    public SeedDefinition? Seed(string item) => Seeds.FirstOrDefault(seed => seed.Seed == item);

    public FarmDefinition? FarmAt(int scene, float x, float z)
        => Farms.FirstOrDefault(farm => farm.Scene == scene && (farm.X - x) * (farm.X - x) + (farm.Z - z) * (farm.Z - z) <= FarmRadius * FarmRadius);

    public static PlantingBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_current != null) return _current;
            string path = settings.Resolve("data\\game\\bbo\\planting.json");
            PlantingBook book = new();
            if (File.Exists(path))
            {
                try
                {
                    book = JsonSerializer.Deserialize<PlantingBook>(File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PlantingBook();
                }
                catch (JsonException exception)
                {
                    Log.Warn("Planting", $"{path}: {exception.Message}");
                }
            }
            Log.Info("Planting", $"{book.Seeds.Count} seed(s), {book.Farms.Count} farm(s) loaded");
            return _current = book;
        }
    }
}
