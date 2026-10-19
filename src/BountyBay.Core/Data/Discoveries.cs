using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

// Discoveries (data\game\bbo\discoveries.json, built by tools\bbo\extract\discoveries.py).
public sealed class DiscoverySpot
{
    public int Scene { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 40f;
}

public sealed class DiscoveryEntry
{
    public string Category { get; set; } = "";
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Grade { get; set; } = 1;
    public string Logbook { get; set; } = "";
    public List<DiscoverySpot> Spots { get; set; } = [];

    public string Table => "finding_" + Category;
}

public sealed class DiscoveryBook
{
    private static readonly object Gate = new();
    private static DiscoveryBook? _current;

    public int SearchSeconds { get; set; } = 5;
    public int FindPercent { get; set; } = 35;
    public int QuestPercent { get; set; } = 60;
    public List<DiscoveryEntry> Entries { get; set; } = [];

    public static DiscoveryBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_current != null) return _current;
            string path = settings.Resolve("data\\game\\bbo\\discoveries.json");
            DiscoveryBook book = new();
            if (File.Exists(path))
            {
                try
                {
                    book = JsonSerializer.Deserialize<DiscoveryBook>(File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new DiscoveryBook();
                }
                catch (JsonException exception)
                {
                    Log.Warn("Discoveries", $"{path}: {exception.Message}");
                }
            }
            Log.Info("Discoveries", $"{book.Entries.Count} discoveries, {book.Entries.Sum(entry => entry.Spots.Count)} spot(s) loaded");
            return _current = book;
        }
    }
}
