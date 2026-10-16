namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public long MilitaryHonor { get; set; }
    public Dictionary<string, int> MilitaryTraining { get; set; } = [];
    public SortedSet<int> MilitaryAwards { get; set; } = [];
    public int PopularityCamp { get; set; }
    public int PopularityUp { get; set; }
    public int PopularityDown { get; set; }
    public int CampTrack { get; set; }
    public Dictionary<string, int> CampBought { get; set; } = [];
    public string CampBoughtDay { get; set; } = "";
    public Dictionary<string, int> ModelChanges { get; set; } = [];
    public int Special1 { get; set; }
    public string QuizDay { get; set; } = "";
    public int QuizAsked { get; set; }
    public int QuizRight { get; set; }
    public long QuizGain { get; set; }
    public int QuizLucky { get; set; } = -1;
    public List<int> QuizOrder { get; set; } = [];
    public int LinkBestScore { get; set; }
    public string LinkDay { get; set; } = "";
    public string TowerGameDay { get; set; } = "";
    public int TowerGamesWon { get; set; }
    public string TurnCardDay { get; set; } = "";
    public int TurnCardUsed { get; set; }
    public Dictionary<int, string> TurnCardOpened { get; set; } = [];
    public List<string> TurnCardBoard { get; set; } = [];
    public int FootballChampion { get; set; } = -1;
    public Dictionary<int, string> FootballRounds { get; set; } = [];
    public string OlympicGuess { get; set; } = "";
    public long LeisurePendingMoney { get; set; }
}
