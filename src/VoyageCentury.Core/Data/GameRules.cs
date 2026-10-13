using System.Text.Json;
using VoyageCentury.Core.Configuration;

namespace VoyageCentury.Core.Data;

public sealed class SkillRules
{
    public int ExperienceFactor { get; set; } = 50;
    public int PhaseOneCap { get; set; } = 31;
    public int PhaseTwoCap { get; set; } = 100;
    public int PromotedSkillLimit { get; set; } = 8;
    public int FreePromotions { get; set; } = 7;
    public int PromotionFee { get; set; }
    public int PaidPromotionFee { get; set; } = 5000000;
    public string BornPromoted { get; set; } = "SKILL_NAVIGATE";
    public int TotalLevelCap { get; set; } = 1700;
    public int StuntPointsAtLearning { get; set; } = 1;
    public int LevelsPerStuntPoint { get; set; } = 4;
    public int StuntLevelStep { get; set; } = 6;
    public int StuntLevelOffset { get; set; } = 5;
    public int StuntLevelMax { get; set; } = 10;
    public int UltimateStuntLevelMax { get; set; } = 1;
    public int UltimateStuntSkillLevel { get; set; } = 40;
    public int LateStuntSkillLevel { get; set; } = 60;
    public int StuntPrice { get; set; } = 300;
    public int TitlePrice { get; set; } = 500;
    public int DemotePoints { get; set; } = 500;
    public int ForgetStuntPoints { get; set; } = 300;
    public int BareHandedSharePercent { get; set; } = 80;
    public int CombatExperiencePerDamage { get; set; } = 10;
    public int VoyageExperience { get; set; } = 500;
    public int VoyageExperienceSeconds { get; set; } = 10;
    public int SeaBattleExperiencePerDamage { get; set; } = 5;
    public int EloquenceExperiencePerProfit { get; set; } = 10;
    public int MedicineExperiencePerPoint { get; set; } = 5;
    public int ManeuverExperience { get; set; } = 5;
    public int StructureSharePercent { get; set; } = 80;
    public int PartyExperiencePercent { get; set; } = 10;
    public List<int> WorkExperience { get; set; } = [792, 1781, 2854, 3929, 5004, 6052, 7017, 7921, 8789, 9618];
    public List<string> InitialSkills { get; set; } = ["SKILL_NAVIGATE", "SKILL_SEABATTLE", "SKILL_SPEECHCRAFT", "SKILL_HAND"];
    public Dictionary<string, string> GrantedStunts { get; set; } = new()
    {
        ["SKILL_MINING"] = "US_MINING2",
        ["SKILL_LUMBER"] = "US_LUMBER2",
        ["SKILL_PLANTING"] = "US_PLANTING2",
    };
    public Dictionary<string, string> GrantedTools { get; set; } = new()
    {
        ["SKILL_MINING"] = "IDName00003397",
        ["SKILL_LUMBER"] = "IDName00003400",
        ["SKILL_PLANTING"] = "IDName00003403",
    };

    public int ExperienceToNext(int level) => (int)Math.Min(int.MaxValue, (long)ExperienceFactor * (level + 1) * (level + 1) * (level + 1));

    public int StuntPoints(int skillLevel) => skillLevel <= 0 ? 0 : StuntPointsAtLearning + skillLevel / Math.Max(1, LevelsPerStuntPoint);

    public int StuntSkillLevel(int stuntLevel) => Math.Max(1, StuntLevelStep * stuntLevel - StuntLevelOffset);

    public int WorkExperienceAt(int level)
    {
        if (WorkExperience.Count == 0 || level <= 0) return 0;
        if (level <= WorkExperience.Count) return WorkExperience[level - 1];
        int last = WorkExperience[^1];
        int step = WorkExperience.Count > 1 ? last - WorkExperience[^2] : last;
        return (int)Math.Min(int.MaxValue, last + (long)step * (level - WorkExperience.Count));
    }
}

public sealed class CharacterRules
{
    public int BaseHp { get; set; } = 100;
    public int HpPerBareHandedLevel { get; set; } = 10;
    public int BaseSp { get; set; } = 100;
    public int SpPerMedicineLevel { get; set; } = 10;
    public int SpPerSpiritStuntLevel { get; set; } = 5;
    public float BaseLoad { get; set; } = 100f;
    public int LoadPerPracticeStuntLevel { get; set; } = 5;
    public int RunLoadPercent { get; set; } = 90;
    public int RegenerationSeconds { get; set; } = 6;
    public int HpRegenerationPermille { get; set; } = 50;
    public int SpRegenerationPermille { get; set; } = 50;
    public int PotionPerSecond { get; set; } = 5;
    public int PotionPerHealingLevel { get; set; } = 1;
    public int DeathReputationLossPercent { get; set; } = 1;
}

public sealed class CombatRuleSet
{
    public int BaseAttack { get; set; } = 1;
    public int BaseHitRatio { get; set; } = 80;
    public int HitFloorPercent { get; set; } = 33;
    public int DamageSpreadPercent { get; set; } = 10;
    public Dictionary<string, int> DefencePercent { get; set; } = new()
    {
        ["SKILL_FALCHION"] = 20,
        ["SKILL_AXE"] = -20,
        ["SKILL_SHOT"] = -70,
    };
    public Dictionary<string, int> HitBonus { get; set; } = new()
    {
        ["SKILL_SWORD"] = 10,
        ["SKILL_SHOT"] = -10,
    };
    public Dictionary<string, int> AttackMilliseconds { get; set; } = new()
    {
        ["SKILL_HAND"] = 700,
        ["SKILL_SWORD"] = 700,
        ["SKILL_FALCHION"] = 700,
        ["SKILL_AXE"] = 1000,
        ["SKILL_SHOT"] = 1500,
    };
    public int DefencePromotionPercentPerLevel { get; set; } = 1;
    public int DebuffPercentPerLevel { get; set; } = 2;
    public int BuffPercentPerLevel { get; set; } = 2;
    public int PassiveChancePercentPerLevel { get; set; } = 3;
    public int CriticalDamagePercent { get; set; } = 200;
    public int SecondHitPercent { get; set; } = 50;
    public int BlockDamagePercent { get; set; } = 50;
    public int CounterDamagePercent { get; set; } = 50;
    public int RageOfGodPercent { get; set; } = 300;
    public int SnipingBonusPercentPerLevel { get; set; } = 10;
    public int SwordDanceHits { get; set; } = 5;
    public int StuntSeconds { get; set; } = 20;
    public int HolyGunSeconds { get; set; } = 30;
    public int DefensiveStanceDefencePerLevel { get; set; } = 2;
    public int MonsterAttackMilliseconds { get; set; } = 2000;
    public float MonsterReach { get; set; } = 14f;
    public float AggroRadius { get; set; } = 10f;
    public int FleeHpPercent { get; set; } = 25;
    public float FleeDistance { get; set; } = 25f;
    public float FleeSpeed { get; set; } = 5f;
    public int MonsterHitBase { get; set; } = 85;
    public int MonsterDodgeBase { get; set; } = 5;
    public int MonsterRatingPerTier { get; set; } = 9;
}

public sealed class ClockRules
{
    public int HourSeconds { get; set; } = 60;
    public int StartYear { get; set; } = 1500;
    public string Epoch { get; set; } = "2026-01-01";
}

public sealed class NobilityRules
{
    public int ReputationFactor { get; set; } = 50;
    public int AmityStep { get; set; } = 8;
    public int AmityBase { get; set; } = 4;
    public int FeeFactor { get; set; } = 200;
    public bool RequireAmity { get; set; } = true;
    public List<string> Titles { get; set; } =
    [
        "Warrior", "Hero", "Knight", "Chevalier", "Baronet", "Baron", "Viscount", "Count", "Marquis", "Duke", "Arch-Duke", "Royal Highness",
    ];

    public long Reputation(int rank) => (long)ReputationFactor * rank * rank * rank * rank;

    public int Amity(int rank) => AmityStep * rank + AmityBase;

    public long Fee(int rank) => (long)FeeFactor * rank * rank * rank * rank;
}

public sealed class EconomyRules
{
    public int SilverCap { get; set; } = 2100000000;
    public int WarehouseSlots { get; set; } = 18;
    public int WarehouseLoad { get; set; } = 500;
    public int WarehouseLoadPrice { get; set; } = 500;
    public int WarehouseLoadLot { get; set; } = 10;
    public int TradeDiscountPercent { get; set; } = 10;
    public int TradeBonusPercent { get; set; } = 10;
    public int TradeTaxPercent { get; set; } = 1;
    public int TradePriceCapFactor { get; set; } = 20;
    public int SellBackPercent { get; set; } = 50;
    public int PirateReputationPerLevel { get; set; } = 10;
}

public sealed class GuildRules
{
    public int FounderSkillLevel { get; set; } = 11;
    public int CreationFee { get; set; } = 100000;
    public int Supporters { get; set; } = 4;
    public bool RequireSupporters { get; set; } = true;
    public int MemberLimit { get; set; } = 20;
}

public sealed class GameRules
{
    public SkillRules Skills { get; set; } = new();
    public CharacterRules Character { get; set; } = new();
    public CombatRuleSet Combat { get; set; } = new();
    public ClockRules Clock { get; set; } = new();
    public NobilityRules Nobility { get; set; } = new();
    public EconomyRules Economy { get; set; } = new();
    public GuildRules Guild { get; set; } = new();

    public static GameRules Load(EmulatorSettings settings)
    {
        string path = settings.Resolve("data\\game\\rules.json");
        if (!File.Exists(path)) return new GameRules();
        return JsonSerializer.Deserialize<GameRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new GameRules();
    }
}

public readonly record struct GameTime(int Year, int Month, int Day, int Hour)
{
    public static GameTime Now(ClockRules rules)
    {
        DateTime epoch = DateTime.TryParse(rules.Epoch, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime parsed)
            ? parsed
            : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long hours = Math.Max(0, (long)((DateTime.UtcNow - epoch).TotalSeconds / Math.Max(1, rules.HourSeconds)));
        long days = hours / 24;
        long months = days / 30;
        return new GameTime(rules.StartYear + (int)(months / 12), (int)(months % 12) + 1, (int)(days % 30) + 1, (int)(hours % 24));
    }

    public bool Night => Hour < 6 || Hour >= 20;
}
