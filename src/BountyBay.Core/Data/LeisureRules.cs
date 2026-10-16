using System.Globalization;
using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class MilitaryLevelRow
{
    public string Value { get; set; } = "0";
    public int Rank { get; set; }

    public double Number => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0;
}

public sealed class MilitaryAttribute
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public List<MilitaryLevelRow> Levels { get; set; } = [];
}

public sealed class MilitaryAward
{
    public int Rank { get; set; }
    public string Text { get; set; } = "";
    public int Money { get; set; }
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
}

public sealed class MilitaryRankRules
{
    public const int TopRank = 18;

    public List<int> RankHonor { get; set; } = [];
    public int TrainingPointsPerRank { get; set; } = 10;
    public List<MilitaryAttribute> Attributes { get; set; } = [];
    public List<MilitaryAward> Awards { get; set; } = [];

    public int RankFor(long honor)
    {
        int rank = 0;
        for (int i = 0; i < RankHonor.Count && i < TopRank; i++)
        {
            if (honor >= RankHonor[i]) rank = i + 1;
        }
        return rank;
    }

    public MilitaryAttribute? Attribute(string key) => Attributes.FirstOrDefault(attribute => attribute.Key == key);
}

public sealed class ExploitMedal
{
    public string Key { get; set; } = "";
    public int Level { get; set; }
    public string Next { get; set; } = "";
    public int SuccessPercent { get; set; } = 100;
    public int FailDrop { get; set; }
}

public sealed class ExploitRules
{
    public int Money { get; set; } = 50000000;
    public string Badge { get; set; } = "honor_badge";
    public int Badges { get; set; } = 5;
    public string MatchProperty { get; set; } = "Kind";
    public int MatchValue { get; set; } = 1;
    public List<ExploitMedal> Medals { get; set; } = [];

    public ExploitMedal? Medal(string key) => Medals.FirstOrDefault(medal => medal.Key == key);

    public ExploitMedal? AtLevel(int level) => Medals.FirstOrDefault(medal => medal.Level == level && medal.Next.Length > 0)
        ?? Medals.FirstOrDefault(medal => medal.Level == level);
}

public sealed class CampGoods
{
    public string Key { get; set; } = "";
    public int Camp { get; set; }
    public string Item { get; set; } = "";
    public string Name { get; set; } = "";
    public int Photo { get; set; }
    public List<int> Limit { get; set; } = [];
    public int PriceType { get; set; }
    public List<int> Price { get; set; } = [];
    public List<int> PopularityCost { get; set; } = [];

    public int At(List<int> values, int tier) => tier >= 0 && tier < values.Count ? values[tier] : 0;
}

public sealed class CampSubmission
{
    public int Camp { get; set; }
    public string Item { get; set; } = "";
    public int Popularity { get; set; }
}

public sealed class CampRules
{
    public List<int> UpTiers { get; set; } = [0, 50000, 150000, 300000, 500000];
    public List<int> DownTiers { get; set; } = [0, 10000, 50000];
    public int PriceTiers { get; set; } = 4;
    public int CouponMoney { get; set; }
    public List<CampGoods> Goods { get; set; } = [];
    public Dictionary<string, Dictionary<string, int>> Monsters { get; set; } = [];
    public List<CampSubmission> Submissions { get; set; } = [];
    public Dictionary<string, int> Tasks { get; set; } = [];

    public int Tier(int popularity)
    {
        int tier = 0;
        for (int i = 0; i < UpTiers.Count; i++)
        {
            if (popularity >= UpTiers[i]) tier = i;
        }
        return tier;
    }

    public CampGoods? Find(string key) => Goods.FirstOrDefault(goods => goods.Key == key);
}

public sealed class ChangeModelEntry
{
    public int Index { get; set; }
    public int Type { get; set; }
    public Dictionary<string, int> Male { get; set; } = [];
    public Dictionary<string, int> Female { get; set; } = [];
}

public sealed class ModelChangeRules
{
    public int ArmType { get; set; } = 10;
    public int Money { get; set; }
    public string Item { get; set; } = "";
    public List<ChangeModelEntry> Models { get; set; } = [];

    public ChangeModelEntry? Find(int index) => Models.FirstOrDefault(model => model.Index == index);
}

public sealed class QuizQuestion
{
    public string Question { get; set; } = "";
    public List<string> Answers { get; set; } = [];
    public int Right { get; set; } = 1;
}

public sealed class QuizRules
{
    public int PerDay { get; set; } = 20;
    public int Seconds { get; set; } = 15;
    public int RewardMoney { get; set; } = 500;
    public List<int> LuckyBonusPercent { get; set; } = [0, 50, 100];
    public List<QuizQuestion> Questions { get; set; } = [];
}

public sealed class BoatRaceRules
{
    public int BetSeconds { get; set; } = 180;
    public int RaceSeconds { get; set; } = 60;
    public int BetMoney { get; set; } = 1000000;
    public int MaxBetsPerRace { get; set; } = 10;
    public int PayoutFactor { get; set; } = 7;
    public int MaxOnlineMinutes { get; set; } = 180;
    public List<string> Boats { get; set; } = [];
}

public sealed class MiniGameRules
{
    public int TowerWinMoney { get; set; } = 5000;
    public int TowerWinsPerDay { get; set; } = 5;
    public int LinkMoneyPerPoint { get; set; }
}

public sealed class TreasureCardPrize
{
    public string Item { get; set; } = "";
    public int Count { get; set; } = 1;
    public int Weight { get; set; } = 1;
}

public sealed class TreasureCardRules
{
    public int Cards { get; set; } = 24;
    public int GoldFrom { get; set; } = 19;
    public int FreePerDay { get; set; } = 1;
    public int TurnsPerDay { get; set; } = 6;
    public int TurnMoney { get; set; } = 10000;
    public int GoldTurnMoney { get; set; } = 50000;
    public List<TreasureCardPrize> Silver { get; set; } = [];
    public List<TreasureCardPrize> Gold { get; set; } = [];
}

public sealed class GuessRules
{
    public int Teams { get; set; } = 32;
    public int FootballRound { get; set; } = 1;
    public List<int> RoundPicks { get; set; } = [16, 8, 4, 2, 1];
    public bool OlympicOpen { get; set; } = true;
}

public sealed class LeisureRules
{
    private static readonly object Gate = new();
    private static LeisureRules? _current;

    public MilitaryRankRules MilitaryRank { get; set; } = new();
    public ExploitRules Exploit { get; set; } = new();
    public CampRules Camp { get; set; } = new();
    public ModelChangeRules ModelChange { get; set; } = new();
    public QuizRules Quiz { get; set; } = new();
    public BoatRaceRules BoatRace { get; set; } = new();
    public MiniGameRules MiniGame { get; set; } = new();
    public TreasureCardRules TreasureCard { get; set; } = new();
    public GuessRules Guess { get; set; } = new();

    public static LeisureRules For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\leisure.json"));
    }

    private static LeisureRules Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("Leisure", $"game data file is missing: {path}");
            return new LeisureRules();
        }
        try
        {
            LeisureRules rules = JsonSerializer.Deserialize<LeisureRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new LeisureRules();
            Log.Info("Leisure", $"{rules.MilitaryRank.Attributes.Count} rank attribute(s), {rules.Camp.Goods.Count} camp goods, "
                + $"{rules.ModelChange.Models.Count} model(s), {rules.Quiz.Questions.Count} quiz question(s) loaded");
            return rules;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Leisure", $"Could not read {path}: {exception.Message}");
            return new LeisureRules();
        }
    }
}
