using System.Text.Json;
using BountyBay.Core.Configuration;

namespace BountyBay.Core.Data;

// Open water of the coastal scenes (data\game\bbo\coastwater.json, built by tools\bbo\extract\coastwater.py from the client's
// island meshes). NPC ships only sail to open cells along straight lines of open cells, so they stay out of the islands.
public static class CoastWater
{
    private static readonly object Gate = new();
    private static Dictionary<int, HashSet<(int I, int J)>>? _scenes;
    private static float _step = 8f;

    private sealed class WaterFile
    {
        public float Step { get; set; } = 8f;
        public Dictionary<string, int[][]> Scenes { get; set; } = [];
    }

    private static HashSet<(int I, int J)>? Cells(EmulatorSettings settings, int scene)
    {
        lock (Gate)
        {
            if (_scenes == null)
            {
                _scenes = [];
                try
                {
                    string path = settings.Resolve("data\\game\\bbo\\coastwater.json");
                    WaterFile? file = File.Exists(path) ? JsonSerializer.Deserialize<WaterFile>(File.ReadAllText(path), EmulatorSettings.Json) : null;
                    if (file != null) _step = file.Step;
                    foreach ((string key, int[][] runs) in file?.Scenes ?? [])
                    {
                        if (!int.TryParse(key, out int id)) continue;
                        HashSet<(int, int)> cells = [];
                        foreach (int[] run in runs.Where(run => run.Length >= 3))
                            for (int i = run[1]; i <= run[2]; i++) cells.Add((i, run[0]));
                        _scenes[id] = cells;
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                }
            }
            return _scenes.GetValueOrDefault(scene);
        }
    }

    public static bool Known(EmulatorSettings settings, int scene) => Cells(settings, scene) != null;

    private static bool Open(HashSet<(int I, int J)> cells, float x, float z)
        => cells.Contains(((int)MathF.Round(x / _step), (int)MathF.Round(z / _step)));

    // True when the straight line from (x0, z0) to (x1, z1) stays on open water (the start may be off it, e.g. a ship
    // that drifted while chasing).
    public static bool Clear(EmulatorSettings settings, int scene, float x0, float z0, float x1, float z1)
    {
        if (Cells(settings, scene) is not { } cells) return true;
        float length = MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        int samples = Math.Max(1, (int)MathF.Ceiling(length / (_step * 0.5f)));
        bool started = false;
        for (int k = 0; k <= samples; k++)
        {
            float t = (float)k / samples;
            bool open = Open(cells, x0 + (x1 - x0) * t, z0 + (z1 - z0) * t);
            if (open) started = true;
            else if (started || k == samples) return false;
        }
        return true;
    }

    // A random open point between least and most units from the centre that a ship at (fromX, fromZ) reaches in a straight line.
    public static (float X, float Z)? RandomRoute(EmulatorSettings settings, int scene, float fromX, float fromZ,
        float centreX, float centreZ, float least, float most)
    {
        if (Cells(settings, scene) is not { } cells) return null;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
            float distance = least + (most - least) * (float)Random.Shared.NextDouble();
            float x = centreX + MathF.Cos(angle) * distance, z = centreZ + MathF.Sin(angle) * distance;
            if (Open(cells, x, z) && Clear(settings, scene, fromX, fromZ, x, z)) return (x, z);
        }
        return null;
    }
}
