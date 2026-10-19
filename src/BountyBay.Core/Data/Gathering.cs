using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

// Mining and timber-felling points (data\game\bbo\gathering.json, built by tools\bbo\extract\gathering.py).
public sealed class GatherNode
{
    public int Scene { get; set; }
    public string Skill { get; set; } = "";
    public int Grade { get; set; } = 1;
    public string Item { get; set; } = "";
    public int Model { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    // Object ids 0x6e000000 + index; the client draws a land goods object (kind 8) at the spot.
    public int ObjectId { get; set; }
}

public sealed class GatheringBook
{
    public const int FirstObjectId = 0x6e000000;

    private static readonly object Gate = new();
    private static GatheringBook? _current;

    public int GatherSeconds { get; set; } = 4;
    public int RespawnSeconds { get; set; } = 90;
    public int ExperiencePerGrade { get; set; } = 40;
    public List<GatherNode> Nodes { get; set; } = [];

    public IEnumerable<GatherNode> InScene(int scene) => Nodes.Where(node => node.Scene == scene);

    public GatherNode? Find(int objectId)
    {
        int index = objectId - FirstObjectId;
        return index >= 0 && index < Nodes.Count ? Nodes[index] : null;
    }

    public static GatheringBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_current != null) return _current;
            string path = settings.Resolve("data\\game\\bbo\\gathering.json");
            GatheringBook book = new();
            if (File.Exists(path))
            {
                try
                {
                    book = JsonSerializer.Deserialize<GatheringBook>(File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip }) ?? new GatheringBook();
                }
                catch (JsonException exception)
                {
                    Log.Warn("Gathering", $"{path}: {exception.Message}");
                }
            }
            for (int i = 0; i < book.Nodes.Count; i++) book.Nodes[i].ObjectId = FirstObjectId + i;
            Log.Info("Gathering", $"{book.Nodes.Count} gathering point(s) loaded");
            return _current = book;
        }
    }
}
