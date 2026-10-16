using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class ArmUpgradeAmount
{
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
}

public sealed class ArmUpgradePath
{
    public string Item { get; set; } = "";
    public int Option { get; set; }
    public string Recipe { get; set; } = "";
    public string Result { get; set; } = "";
    public string NeedProp { get; set; } = "";
    public int NeedValue { get; set; }
}

public sealed class ArmUpgradeSuit
{
    public string Header { get; set; } = "";
    public List<string> Members { get; set; } = [];
}

public sealed class ArmUpgradeSuitPiece
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Result { get; set; } = "";
    public List<ArmUpgradeAmount> Materials { get; set; } = [];
}

public sealed class ArmUpgradeStarGroup
{
    public List<int> ArmTypes { get; set; } = [];
    public List<string> Items { get; set; } = [];
}

public sealed class ArmUpgradeStarTier
{
    public int Tier { get; set; }
    public int UpTo { get; set; }
    public List<ArmUpgradeStarGroup> Groups { get; set; } = [];
}

public sealed class ArmUpgradeStarTable
{
    public int Table { get; set; }
    public List<ArmUpgradeStarTier> Tiers { get; set; } = [];
}

public sealed class ArmUpgradeRules
{
    public int EvolMax { get; set; } = 10;
    public int EvolMaterialBase { get; set; } = 1;
    public int EvolMaterialPerStep { get; set; } = 1;
    public int EvolSilverPerStep { get; set; } = 100000;
    public int EvolChancePercent { get; set; } = 100;
    public int EvolGainPercent { get; set; } = 2;
    public int EvolMinimumGain { get; set; } = 1;
    public int StarMax { get; set; } = 40;
    public int StarMaterialBase { get; set; } = 1;
    public int StarMaterialPerTier { get; set; } = 1;
    public int StarSilverPerStar { get; set; } = 50000;
    public int StarChanceStartPercent { get; set; } = 90;
    public int StarChanceLossPerStar { get; set; } = 2;
    public int StarChanceMinimumPercent { get; set; } = 20;
    public int StarLuckPerFailure { get; set; } = 10;
    public int StarLuckMax { get; set; } = 200;
    public int StarGainPerStar { get; set; } = 1;
    public string MemoryCostItem { get; set; } = "";
    public int MemoryCostAmount { get; set; }
    public int RankLevelMax { get; set; } = 10;
    public int RankQualityMax { get; set; } = 5;
    public int RankSilverPerLevel { get; set; } = 100000;
    public List<ArmUpgradeAmount> RankMaterials { get; set; } = [];
    public List<ArmUpgradeAmount> RankQualityMaterials { get; set; } = [];
    public List<int> SuitChanceMaterialPercent { get; set; } = [25, 50, 75, 100];
    public List<string> UpgradeMasters { get; set; } = [];
    public int SkillSaveSlots { get; set; } = 3;
    public int SoulSaveSlots { get; set; } = 8;
    public string SaveCostItem { get; set; } = "";
    public int SaveCostAmount { get; set; }
    public int PropPointsPerStar { get; set; } = 1;
}

public sealed class ArmUpgradeTotemProp
{
    public int Index { get; set; }
    public string Prop { get; set; } = "";
    public List<int> MaxPoint { get; set; } = [];
    public List<int> Values { get; set; } = [];
}

public sealed class ArmUpgradeExchange
{
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
    public List<ArmUpgradeAmount> Cost { get; set; } = [];
    public string Desc { get; set; } = "";
}

public sealed class ArmUpgradeData
{
    public string SilverItem { get; set; } = "IDName00004390";
    public Dictionary<string, List<ArmUpgradeAmount>> Recipes { get; set; } = [];
    public List<ArmUpgradePath> Upgrades { get; set; } = [];
    public List<ArmUpgradeSuit> Suits { get; set; } = [];
    public Dictionary<string, List<ArmUpgradeSuitPiece>> SuitPieces { get; set; } = [];
    public Dictionary<string, List<string>> EvolMaterials { get; set; } = [];
    public List<ArmUpgradeStarTable> StarMaterials { get; set; } = [];
    public Dictionary<string, List<string>> SaveProps { get; set; } = [];
    public List<ArmUpgradeTotemProp> TotemProps { get; set; } = [];
    public Dictionary<string, List<ArmUpgradeExchange>> Exchanges { get; set; } = [];
    public ArmUpgradeRules Rules { get; set; } = new();
}

public sealed class ArmUpgradeBook
{
    private static readonly object Gate = new();
    private static ArmUpgradeBook? _current;

    private ArmUpgradeBook(ArmUpgradeData data) => Data = data;

    public ArmUpgradeData Data { get; }

    public ArmUpgradeRules Rules => Data.Rules;

    public bool IsSilver(string item) => item.Equals(Data.SilverItem, StringComparison.OrdinalIgnoreCase);

    public ArmUpgradePath? Path(string item, int option)
        => Data.Upgrades.FirstOrDefault(path => path.Option == option && path.Item.Equals(item, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ArmUpgradePath> PathsFrom(string item)
        => Data.Upgrades.Where(path => path.Item.Equals(item, StringComparison.OrdinalIgnoreCase)).OrderBy(path => path.Option);

    public List<ArmUpgradeAmount> Recipe(string key)
        => Data.Recipes.TryGetValue(key, out List<ArmUpgradeAmount>? list) ? list : [];

    public List<string> EvolMaterials(int needWorkLevel, int armType, int step)
        => Data.EvolMaterials.TryGetValue($"{needWorkLevel}-{armType}-{step}", out List<string>? list) ? list : [];

    public List<string> StarMaterials(bool sea, int armType, int stars)
    {
        ArmUpgradeStarTable? table = Data.StarMaterials.FirstOrDefault(entry => entry.Table == (sea ? 1 : 0));
        ArmUpgradeStarTier? tier = table?.Tiers.OrderBy(entry => entry.UpTo).FirstOrDefault(entry => stars < entry.UpTo);
        ArmUpgradeStarGroup? group = tier?.Groups.FirstOrDefault(entry => entry.ArmTypes.Contains(armType)) ?? tier?.Groups.FirstOrDefault();
        return group?.Items ?? [];
    }

    public int StarTier(bool sea, int stars)
    {
        ArmUpgradeStarTable? table = Data.StarMaterials.FirstOrDefault(entry => entry.Table == (sea ? 1 : 0));
        return table?.Tiers.OrderBy(entry => entry.UpTo).FirstOrDefault(entry => stars < entry.UpTo)?.Tier ?? 0;
    }

    public List<ArmUpgradeExchange> Exchanges(string npc)
        => Data.Exchanges.FirstOrDefault(pair => pair.Key.Equals(npc, StringComparison.OrdinalIgnoreCase)).Value ?? [];

    public ArmUpgradeTotemProp? TotemProp(int index) => Data.TotemProps.FirstOrDefault(prop => prop.Index == index);

    public List<string> SaveProps(int armClass) => Data.SaveProps.TryGetValue(armClass.ToString(), out List<string>? list) ? list : [];

    public static ArmUpgradeBook For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\armupgrade.json"));
    }

    private static ArmUpgradeBook Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("ArmUpgrade", $"game data file is missing: {path}");
            return new ArmUpgradeBook(new ArmUpgradeData());
        }
        try
        {
            ArmUpgradeData data = JsonSerializer.Deserialize<ArmUpgradeData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new ArmUpgradeData();
            Log.Info("ArmUpgrade", $"{data.Upgrades.Count} upgrade path(s), {data.Recipes.Count} recipe(s), {data.Suits.Count} suit group(s) loaded");
            return new ArmUpgradeBook(data);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("ArmUpgrade", $"Could not read {path}: {exception.Message}");
            return new ArmUpgradeBook(new ArmUpgradeData());
        }
    }
}
