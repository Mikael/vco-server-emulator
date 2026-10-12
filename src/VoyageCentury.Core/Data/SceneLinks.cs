using System.Text.Json;
using VoyageCentury.Core.Configuration;
using VoyageCentury.Core.Logging;

namespace VoyageCentury.Core.Data;

public sealed class SceneLink
{
    public int Id { get; set; }
    public int Scene { get; set; }
    public string Name { get; set; } = "";
    public int Model { get; set; }
    public int ModelId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Orient { get; set; }
    public float Radius { get; set; }
    public bool Sail { get; set; }
    public int TargetScene { get; set; }
    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public float TargetZ { get; set; }
    public string Message { get; set; } = "";

    public bool HasObject => Model > 0;

    public float DistanceTo(float x, float z) => MathF.Sqrt((x - X) * (x - X) + (z - Z) * (z - Z));

    public bool Covers(float x, float z) => Radius > 0f && DistanceTo(x, z) <= Radius;
}

public sealed class SceneLinkData
{
    public float UseRange { get; set; } = 6f;
    public List<SceneLink> Links { get; set; } = [];

    public IEnumerable<SceneLink> InScene(int scene) => Links.Where(link => link.Scene == scene);

    public SceneLink? Find(int scene, int id) => Links.FirstOrDefault(link => link.Scene == scene && link.Id == id);

    public SceneLink? WalkInto(int scene, float fromX, float fromZ, float toX, float toZ)
        => Links.FirstOrDefault(link => link.Scene == scene && link.Covers(toX, toZ) && !link.Covers(fromX, fromZ));

    public SceneLink? SailFrom(int scene) => Links.FirstOrDefault(link => link.Sail && link.Scene == scene);

    public SceneLink? SailBackFrom(int scene) => Links.FirstOrDefault(link => link.Sail && link.TargetScene == scene);
}

public static class SceneLinks
{
    private static readonly object Gate = new();
    private static SceneLinkData? _data;

    public static SceneLinkData Load(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_data != null) return _data;
            string path = settings.Resolve("data\\game\\scenelinks.json");
            try
            {
                _data = File.Exists(path)
                    ? JsonSerializer.Deserialize<SceneLinkData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new SceneLinkData()
                    : new SceneLinkData();
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Error("SceneLinks", $"Could not read {path}: {exception.Message}");
                _data = new SceneLinkData();
            }
            _data.Links = _data.Links.Where(link => link.Id > 0 && link.Scene > 0).DistinctBy(link => link.Id).ToList();
            Log.Info("SceneLinks", $"{_data.Links.Count} scene link(s) loaded");
            return _data;
        }
    }
}
