using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class AideTrainRegion
{
    public int UpToLevel { get; set; }
    public int Types { get; set; }
}

public sealed class AideTrainCost
{
    public int Silver { get; set; }
    public string Item { get; set; } = "";
    public int Amount { get; set; }
}

public sealed class AideData
{
    public List<string> FirstNames { get; set; } = [];
    public List<string> SecondNames { get; set; } = [];
    public Dictionary<string, List<string>> SkillPools { get; set; } = [];
    public Dictionary<string, string> SkillNames { get; set; } = [];
    public List<int> Photos { get; set; } = [];
    public List<List<double>> TrainRange { get; set; } = [];
    public List<AideTrainRegion> TrainRegion { get; set; } = [];
    public List<AideTrainCost> TrainCosts { get; set; } = [];
    public List<List<int>> TrainDeltaPercent { get; set; } = [];
    public List<int> TrainExp { get; set; } = [];
    public int ExpPerLevel { get; set; } = 100;
    public int MaxLevel { get; set; } = 100;
    public List<int> EquipLevels { get; set; } = [1, 30, 50, 70, 90];
    public int StartRestSlots { get; set; } = 3;
    public int MaxRestSlots { get; set; } = 10;
    public List<int> UnlockCosts { get; set; } = [];
    public int Candidates { get; set; } = 10;
    public List<int> QualityWeights { get; set; } = [70, 25, 5];
    public List<int> RecruitPrices { get; set; } = [];
    public List<int> StartStatPercent { get; set; } = [];
    public int RefreshCost { get; set; }
    public int TransferLossPercent { get; set; } = 10;
    public int SkillCooldownSeconds { get; set; } = 90;
    public Dictionary<string, int> Equipment { get; set; } = [];
}

public sealed class AideBook
{
    private static readonly object Gate = new();
    private static AideBook? _current;

    private AideBook(AideData data) => Data = data;

    public AideData Data { get; }

    public const int Stats = 4;

    public int MaxLevel => Math.Clamp(Data.MaxLevel, 1, Math.Max(1, Data.TrainRange.Count));

    public int MaxSlots => Math.Clamp(Data.MaxRestSlots, 1, 10);

    public int StartSlots => Math.Clamp(Data.StartRestSlots, 1, MaxSlots);

    public string NameOf(int code)
    {
        int first = code / 1000;
        int second = code % 1000;
        string a = first >= 0 && first < Data.FirstNames.Count ? Data.FirstNames[first] : "General";
        string b = second >= 0 && second < Data.SecondNames.Count ? Data.SecondNames[second] : code.ToString();
        return $"{a}.{b}";
    }

    public int RandomCode()
        => Random.Shared.Next(Math.Max(1, Data.FirstNames.Count)) * 1000 + Random.Shared.Next(Math.Max(1, Data.SecondNames.Count));

    public int RandomQuality()
    {
        int total = Data.QualityWeights.Sum(weight => Math.Max(0, weight));
        if (total <= 0) return 1;
        int roll = Random.Shared.Next(total);
        for (int i = 0; i < Data.QualityWeights.Count; i++)
        {
            roll -= Math.Max(0, Data.QualityWeights[i]);
            if (roll < 0) return i + 1;
        }
        return 1;
    }

    public string RandomSkill(int quality)
        => Data.SkillPools.TryGetValue(quality.ToString(), out List<string>? pool) && pool.Count > 0
            ? pool[Random.Shared.Next(pool.Count)]
            : "FGSkill001";

    public string SkillName(string skill) => Data.SkillNames.TryGetValue(skill, out string? name) ? name : skill;

    public int RandomPhoto() => Data.Photos.Count > 0 ? Data.Photos[Random.Shared.Next(Data.Photos.Count)] : 4005180;

    public int Price(int quality) => At(Data.RecruitPrices, quality - 1, 100000);

    public int StartPercent(int quality) => At(Data.StartStatPercent, quality - 1, 20);

    public int Maximum(int level, int stat)
    {
        if (Data.TrainRange.Count == 0) return 0;
        List<double> row = Data.TrainRange[Math.Clamp(level, 1, Data.TrainRange.Count) - 1];
        if (stat < 0 || stat >= row.Count) return 0;
        return (int)Math.Round(row[stat] * 100);
    }

    public bool TypeAllowed(int level, int type)
    {
        if (type < 1 || type > 6) return false;
        AideTrainRegion? region = Data.TrainRegion.OrderBy(entry => entry.UpToLevel).FirstOrDefault(entry => level <= entry.UpToLevel)
            ?? Data.TrainRegion.OrderBy(entry => entry.UpToLevel).LastOrDefault();
        return region == null || (region.Types & (1 << (type - 1))) != 0;
    }

    public AideTrainCost? Cost(int type) => type >= 1 && type <= Data.TrainCosts.Count ? Data.TrainCosts[type - 1] : null;

    public (int Low, int High) Delta(int type)
    {
        if (type < 1 || type > Data.TrainDeltaPercent.Count) return (-5, 6);
        List<int> range = Data.TrainDeltaPercent[type - 1];
        return range.Count >= 2 ? (Math.Min(range[0], range[1]), Math.Max(range[0], range[1])) : (-5, 6);
    }

    public int TrainExp(int type) => At(Data.TrainExp, type - 1, 10);

    public int ExpToLevel(int level) => Math.Max(1, Data.ExpPerLevel) * Math.Max(1, level);

    public int EquipLevel(int slot) => At(Data.EquipLevels, slot, 1);

    public int UnlockCost(int slot) => At(Data.UnlockCosts, slot - 1, 0);

    public int EquipSlot(string item) => Data.Equipment.TryGetValue(item, out int slot) ? slot : -1;

    private static int At(List<int> list, int index, int fallback) => index >= 0 && index < list.Count ? list[index] : fallback;

    public static AideBook For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\aides.json"));
    }

    private static AideBook Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("Aides", $"game data file is missing: {path}");
            return new AideBook(new AideData());
        }
        try
        {
            AideData data = JsonSerializer.Deserialize<AideData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new AideData();
            Log.Info("Aides", $"{data.FirstNames.Count}x{data.SecondNames.Count} names, {data.TrainRange.Count} training levels loaded");
            return new AideBook(data);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Aides", $"Could not read {path}: {exception.Message}");
            return new AideBook(new AideData());
        }
    }
}
