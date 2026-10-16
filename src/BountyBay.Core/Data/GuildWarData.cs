using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class GuildWarSchedule
{
    public bool Open { get; set; } = true;
    public int Week { get; set; } = 6;
    public int Hour { get; set; } = 20;
    public int Minute { get; set; }
    public int Prepare { get; set; } = 30;
    public int Recon { get; set; } = 10;
    public int Battle { get; set; } = 60;

    public GuildWarSchedule Copy() => (GuildWarSchedule)MemberwiseClone();
}

public sealed class GuildWarRules
{
    public List<string> SiegeCities { get; set; } =
        ["Athens", "Madeira", "Cape Town", "Malacca", "Nagasaki", "Barcelona", "London", "Oslo", "Aden", "Zhigu", "Seoul Port"];
    public GuildWarSchedule Schedule { get; set; } = new();
    public int BidUnit { get; set; } = 10000;
    public int MinBidUnits { get; set; } = 1;
    public int MaxBidUnits { get; set; } = 2000000;
    public int MinRaiseUnits { get; set; } = 1;
    public int MinGuildLevel { get; set; } = 1;
    public int BidderDuty { get; set; } = 4;
    public int OfficerDuty { get; set; } = 2;
    public bool RequireBattleState { get; set; } = true;
    public int MaxRegistrations { get; set; } = 50;
    public int WeeklyEntries { get; set; } = 1;
    public int WarScene { get; set; } = 1900023;
    public int SceneKind { get; set; } = 28;
    public int BulletinKind { get; set; } = 42;
    public int WinnerRewardPercent { get; set; } = 10;
    public int LoserCompensationPercent { get; set; }
    public int KillMerit { get; set; } = 10;
    public int StatueMessageLength { get; set; } = 280;
    public bool GmOpenToAll { get; set; } = true;
    public List<string> Gms { get; set; } = [];
    public List<int> BarMax { get; set; } = [100, 100, 100, 100, 100, 100, 100, 100, 100];
    public int ImpeachSeconds { get; set; } = 30;
    public List<int> ImpeachSceneKinds { get; set; } = [0x1a, 0x1f, 0x21, 0x29, 0x30, 0x31];
}

public sealed class GuildWarData
{
    private static readonly object Gate = new();
    private static GuildWarData? _loaded;
    private static Dictionary<int, string>? _cities;

    public GuildWarRules War { get; set; } = new();

    public static GuildWarData For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;
            string path = settings.Resolve("data\\game\\guildwar.json");
            try
            {
                _loaded = File.Exists(path)
                    ? JsonSerializer.Deserialize<GuildWarData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new GuildWarData()
                    : new GuildWarData();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error("GuildWar", $"Load failed: {exception.Message}");
                _loaded = new GuildWarData();
            }
            return _loaded;
        }
    }

    public static string CityOf(EmulatorSettings settings, int scene)
    {
        lock (Gate)
        {
            _cities ??= LoadCities(settings);
            return _cities.GetValueOrDefault(scene / 1000, "");
        }
    }

    public static int SceneOf(EmulatorSettings settings, string city)
    {
        lock (Gate)
        {
            _cities ??= LoadCities(settings);
            foreach ((int number, string name) in _cities)
            {
                if (name.Equals(city, StringComparison.OrdinalIgnoreCase)) return number * 1000 + 1;
            }
            return 0;
        }
    }

    private static Dictionary<int, string> LoadCities(EmulatorSettings settings)
    {
        var cities = new Dictionary<int, string>();
        string path = settings.Resolve("data\\game\\bbo\\world_markers.json");
        try
        {
            if (!File.Exists(path)) return cities;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("Cities", out JsonElement list)) return cities;
            foreach (JsonProperty city in list.EnumerateObject())
            {
                if (int.TryParse(city.Name, out int number)) cities[number] = city.Value.GetString() ?? "";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("GuildWar", $"City list failed: {exception.Message}");
        }
        return cities;
    }
}
