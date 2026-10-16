using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class ArmRebuildMaterial
{
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
}

public sealed class ArmRebuildLevel
{
    public int Level { get; set; }
    public int Silver { get; set; }
    public List<ArmRebuildMaterial> Materials { get; set; } = [];
    public int MinPercent { get; set; }
    public int MaxPercent { get; set; }
    public int SkillPercent { get; set; }
    public int SkillLevelMax { get; set; } = 1;
}

public sealed class ArmRebuildBook
{
    private static readonly object Gate = new();
    private static ArmRebuildBook? _current;

    public int DailyLimit { get; set; } = 20;
    public List<ArmRebuildLevel> Levels { get; set; } = [];
    public List<ArmRebuildMaterial> LockMaterials { get; set; } = [];
    public List<ArmRebuildMaterial> SkillLockMaterials { get; set; } = [];
    public string SilverKey { get; set; } = "IDName00003506";
    public string RecordItem { get; set; } = "";
    public int RecordSlots { get; set; } = 8;
    public List<string> TransferItems { get; set; } = [];
    public string DamagedTransferItem { get; set; } = "";
    public int DamagedMaxNeedRank { get; set; } = 12;
    public int DamagedMaxLevel { get; set; } = 114;
    public Dictionary<string, List<string>> Skills { get; set; } = [];

    public ArmRebuildLevel? Level(int level) => Levels.FirstOrDefault(entry => entry.Level == level);

    public IReadOnlyList<string> SkillPool(int armType)
        => Skills.TryGetValue(armType.ToString(), out List<string>? pool) ? pool : [];

    public static ArmRebuildBook For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\armrebuild.json"));
    }

    private static ArmRebuildBook Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("ArmRebuild", $"game data file is missing: {path}");
            return new ArmRebuildBook();
        }
        try
        {
            ArmRebuildBook book = JsonSerializer.Deserialize<ArmRebuildBook>(File.ReadAllText(path), EmulatorSettings.Json) ?? new ArmRebuildBook();
            Log.Info("ArmRebuild", $"{book.Levels.Count} recast level(s) loaded");
            return book;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("ArmRebuild", $"Could not read {path}: {exception.Message}");
            return new ArmRebuildBook();
        }
    }
}
