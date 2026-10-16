using System.Text.Json;
using System.Text.Json.Serialization;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Configuration;

public sealed class LoggingSettings
{
    public LogLevel ConsoleLevel { get; set; } = LogLevel.Info;
    public bool PacketsToConsole { get; set; }
    public string File { get; set; } = "logs\\emulator.log";
}

public sealed class WorldSettings
{
    public int StartScene { get; set; } = 1001001;
    public float StartX { get; set; } = -87.65f;
    public float StartY { get; set; } = 13.81f;
    public float StartZ { get; set; } = 116.74f;
    public float RunSpeed { get; set; } = 4.0f;
    public float ShipSpeed { get; set; } = 14.0f;
    public int ShipModel { get; set; } = 2011001;
    public int StartMoney { get; set; } = 1000;
    public int RespawnSeconds { get; set; } = 30;
    public int Hour { get; set; } = 12;
    public int Weather { get; set; } = 3;
}

public sealed class BotSettings
{
    public bool Enabled { get; set; }
    public int Count { get; set; } = 3;
}

public sealed class EmulatorSettings
{
    public string ServerName { get; set; } = "Localhost";
    public string BindAddress { get; set; } = "127.0.0.1";
    public string PublicAddress { get; set; } = "127.0.0.1";
    public int ListPort { get; set; } = 3000;
    public int GamePort { get; set; } = 3001;
    public string ClientPath { get; set; } = "..\\voyage";
    public LoggingSettings Logging { get; set; } = new();
    public WorldSettings World { get; set; } = new();
    public BotSettings Bots { get; set; } = new();

    [JsonIgnore]
    public string Root { get; private set; } = "";

    [JsonIgnore]
    public string ResourcePath => Path.GetFullPath(Path.Combine(Root, ClientPath, "resource"));

    public string Resolve(string relative) => Path.GetFullPath(Path.Combine(Root, relative));

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static EmulatorSettings Load()
    {
        string root = FindRoot();
        string path = Environment.GetEnvironmentVariable("VCO_CONFIG") ?? Path.Combine(root, "config", "emulator.json");
        EmulatorSettings settings = File.Exists(path)
            ? JsonSerializer.Deserialize<EmulatorSettings>(File.ReadAllText(path), Json) ?? new EmulatorSettings()
            : new EmulatorSettings();
        settings.Root = root;
        return settings;
    }

    private static string FindRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config", "emulator.json")))
                    return dir.FullName;
            }
        }
        return Directory.GetCurrentDirectory();
    }
}
