using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Persistence;

public sealed class GuildMatchSlot
{
    public int Slot { get; set; }
    public string Left { get; set; } = "";
    public string Right { get; set; } = "";
    public int State { get; set; }
    public string Winner { get; set; } = "";
    public string Score { get; set; } = "";
    public List<int> Rounds { get; set; } = [];
}

public sealed class GuildMatchBet
{
    public string Id { get; set; } = "";
    public string Player { get; set; } = "";
    public string Season { get; set; } = "";
    public int Slot { get; set; }
    public int Side { get; set; }
    public string Guild { get; set; } = "";
    public string Match { get; set; } = "";
    public string Type { get; set; } = "";
    public long Amount { get; set; }
    public int Result { get; set; }
    public long Reward { get; set; }
}

public sealed class GuildMatchFile
{
    public string Season { get; set; } = "";
    public string Champion { get; set; } = "";
    public int NextBet { get; set; } = 1;
    public List<GuildMatchSlot> Slots { get; set; } = [];
    public List<GuildMatchBet> Bets { get; set; } = [];
}

public static class GuildMatchStore
{
    private static readonly object Gate = new();
    private static GuildMatchFile? _file;
    private static string _path = "";

    public static GuildMatchFile Load(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_file != null) return _file;
            _path = settings.Resolve("data\\guildmatches.json");
            _file = new GuildMatchFile();
            try
            {
                if (File.Exists(_path))
                    _file = JsonSerializer.Deserialize<GuildMatchFile>(File.ReadAllText(_path), EmulatorSettings.Json) ?? new GuildMatchFile();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error("GuildMatch", $"Load failed: {exception.Message}");
            }
            return _file;
        }
    }

    public static void Save()
    {
        lock (Gate)
        {
            if (_file == null || _path.Length == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_file, EmulatorSettings.Json));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error("GuildMatch", $"Save failed: {exception.Message}");
            }
        }
    }
}
