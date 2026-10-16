using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Persistence;

public sealed class GuildCopyApplicant
{
    public string Name { get; set; } = "";
    public int State { get; set; } = 1;
    public bool Entered { get; set; }
}

public sealed class GuildCopyRun
{
    public string Season { get; set; } = "";
    public int Runs { get; set; }
    public bool Committed { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Stage { get; set; } = 1;
    public int Kills { get; set; }
    public List<GuildCopyApplicant> Applicants { get; set; } = [];
}

public sealed class GuildBuildState
{
    public string Guild { get; set; } = "";
    public long Activity { get; set; }
    public int[] Degrees { get; set; } = new int[4];
    public long BuildExp { get; set; }
    public Dictionary<string, string> LastActive { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Log { get; set; } = [];
    public GuildCopyRun Copy { get; set; } = new();
}

public sealed class GuildBuildFile
{
    public List<GuildBuildState> Guilds { get; set; } = [];
}

public static class GuildBuildStore
{
    private static readonly object Gate = new();
    private static List<GuildBuildState>? _states;
    private static string _path = "";

    public static GuildBuildState Of(EmulatorSettings settings, string guild)
    {
        lock (Gate)
        {
            List<GuildBuildState> states = Load(settings);
            GuildBuildState? state = states.FirstOrDefault(entry => entry.Guild.Equals(guild, StringComparison.OrdinalIgnoreCase));
            if (state != null) return state;
            state = new GuildBuildState { Guild = guild };
            states.Add(state);
            return state;
        }
    }

    public static void Forget(EmulatorSettings settings, string guild)
    {
        lock (Gate)
        {
            if (Load(settings).RemoveAll(entry => entry.Guild.Equals(guild, StringComparison.OrdinalIgnoreCase)) > 0) Save();
        }
    }

    public static void Save()
    {
        lock (Gate)
        {
            if (_states == null || _path.Length == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(new GuildBuildFile { Guilds = _states }, EmulatorSettings.Json));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error("GuildBuild", $"Save failed: {exception.Message}");
            }
        }
    }

    private static List<GuildBuildState> Load(EmulatorSettings settings)
    {
        if (_states != null) return _states;
        _path = settings.Resolve("data\\guildbuild.json");
        _states = [];
        try
        {
            if (File.Exists(_path))
                _states = JsonSerializer.Deserialize<GuildBuildFile>(File.ReadAllText(_path), EmulatorSettings.Json)?.Guilds ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("GuildBuild", $"Load failed: {exception.Message}");
        }
        foreach (GuildBuildState state in _states.Where(state => state.Degrees.Length != 4))
            state.Degrees = state.Degrees.Concat(new int[4]).Take(4).ToArray();
        return _states;
    }
}
