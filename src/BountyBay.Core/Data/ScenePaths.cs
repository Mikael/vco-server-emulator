using System.Text.Json;
using BountyBay.Core.Configuration;

namespace BountyBay.Core.Data;

// Open-water points per ocean scene (data/game/bbo/oceanwater.json), each checked in the client to be sea, not land.
public static class ScenePaths
{
    private static readonly object Gate = new();
    private static Dictionary<int, (float X, float Z)[]>? _points;

    private sealed class WaterFile
    {
        public Dictionary<string, float[][]> Points { get; set; } = [];
    }

    public static (float X, float Z)[] Nodes(EmulatorSettings settings, int scene)
    {
        lock (Gate)
        {
            if (_points == null)
            {
                _points = [];
                try
                {
                    string path = settings.Resolve("data\\game\\bbo\\oceanwater.json");
                    WaterFile? file = File.Exists(path) ? JsonSerializer.Deserialize<WaterFile>(File.ReadAllText(path), EmulatorSettings.Json) : null;
                    foreach ((string key, float[][] points) in file?.Points ?? [])
                    {
                        if (int.TryParse(key, out int id))
                            _points[id] = [.. points.Where(point => point.Length >= 2).Select(point => (point[0], point[1]))];
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                }
            }
            return _points.TryGetValue(scene, out (float X, float Z)[]? nodes) ? nodes : [];
        }
    }

    public static (float X, float Z) Nearest(EmulatorSettings settings, int scene, float x, float z)
    {
        (float X, float Z)[] nodes = Nodes(settings, scene);
        if (nodes.Length == 0) return (x, z);
        return nodes.MinBy(node => (node.X - x) * (node.X - x) + (node.Z - z) * (node.Z - z));
    }

    public static (float X, float Z)? RandomNear(EmulatorSettings settings, int scene, float x, float z, float least, float most)
    {
        (float X, float Z)[] near = Nodes(settings, scene).Where(node =>
        {
            float distance = MathF.Sqrt((node.X - x) * (node.X - x) + (node.Z - z) * (node.Z - z));
            return distance >= least && distance <= most;
        }).ToArray();
        return near.Length == 0 ? null : near[Random.Shared.Next(near.Length)];
    }
}
