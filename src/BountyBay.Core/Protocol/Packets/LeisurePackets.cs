using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public static class LeisureTables
{
    public const int BoatRunners = 19;
    public const int BoatTotalBets = 20;
    public const int BoatOwnBets = 21;
    public const int BoatSingleWinners = 22;
    public const int BoatTotalWinners = 23;
    public const int BoatRace = 24;
    public const int QuizRanking = 42;
    public const int LinkRanking = 48;
    public const int MilitaryTraining = 50;
    public const int HonorAward = 51;
    public const int CampGoods = 110;
}

public static class LeisurePackets
{
    public const int QuizOpenCards = 0;
    public const int QuizOpenAnswer = 1;
    public const int QuizOpenBlank = 2;
    public const int BoatClosed = 0;
    public const int BoatBetting = 1;
    public const int BoatRacing = 2;

    public static byte[] Clear(int table) => UiPackets.Custom($"ct {table}");

    public static byte[] Row(int table, IEnumerable<string> cells)
        => UiPackets.Custom($"ut {table}" + string.Concat(cells.Select(cell => UiPackets.FieldSeparator + Cell(cell))));

    public static IEnumerable<byte[]> Rows(int table, IEnumerable<string[]> rows)
    {
        yield return Clear(table);
        foreach (string[] row in rows) yield return Row(table, row);
    }

    public static byte[] OpenQuizMatch(int mode) => UiPackets.Custom($"scOpenQuizMatch {mode}");

    public static byte[] QuizLucky(int lucky) => UiPackets.Custom($"scQuizExp {Math.Clamp(lucky, 0, 2)}");

    public static byte[] QuizText(int number, string question, IReadOnlyList<string> answers, int seconds)
        => UiPackets.Custom(string.Join(' ', new[] { "scQuizText", Number(number), UiPackets.Token(question) }
            .Concat(Enumerable.Range(0, 4).Select(i => UiPackets.Token(i < answers.Count ? answers[i] : "-")))
            .Append(Number(Math.Max(0, seconds)))));

    public static byte[] QuizResult(int right, int yours) => UiPackets.Custom($"scQuizResult {right} {yours}");

    public static byte[] QuizOver(long total) => UiPackets.Custom($"scQuizOver {Math.Clamp(total, 0, 99999999)}");

    public static byte[] ShipMatchInfo(int phase, int seconds) => UiPackets.Custom($"scShipMatchInfo {phase} {Math.Max(0, seconds)}");

    public static byte[] ShipMatchStart() => UiPackets.Custom("scShipMatchStart");

    public static byte[] ShipMatchArrive(int boat) => UiPackets.Custom($"scShipMatchArrive {boat}");

    public static byte[] FootballGuess(int type, string data) => UiPackets.Custom($"scFBall {type} {(data.Length == 0 ? "-1" : data)}");

    public static byte[] ShowGuessOlympic() => UiPackets.Custom("scGuessOlympic");

    public static byte[] BeginTowerGame() => UiPackets.Custom("scBeginTowerGame");

    public static byte[] ShowTurnCard(int timesLeft, int freeTimes) => UiPackets.Custom($"scShowTurnCard {Math.Max(0, timesLeft)} {Math.Max(0, freeTimes)}");

    public static byte[] TurnCardInfo(int position, int photo, string name, int count, int timesLeft, int freeTimes)
        => UiPackets.Custom($"scTurnCardInfo {position} {photo} {UiPackets.Token(name)} {Math.Max(1, count)} {Math.Max(0, timesLeft)} {Math.Max(0, freeTimes)}");

    public static byte[] CloseTurnCard() => UiPackets.Custom("scCloseTurnCard");

    public static byte[] ShowMakeExploit(string property, int value) => UiPackets.Custom($"scShowMakeExploit {property},{value}");

    public static byte[] ReferItemDone() => UiPackets.Custom("scReferItem");

    public static byte[] ThingShow() => UiPackets.Custom("scThingShow");

    public static byte[] ShowChangeModel() => UiPackets.Custom("scShowChangeModel");

    public static byte[] ShowVoteForm() => UiPackets.Custom("scShowVoteForm");

    public static string Cell(string? text)
    {
        string cleaned = (text ?? "").Replace("\r", "").Replace('\n', ' ').Replace(UiPackets.FieldSeparator, ' ').Trim();
        return cleaned.Length == 0 ? "-" : cleaned;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
