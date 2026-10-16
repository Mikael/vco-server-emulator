using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class LeisureCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csShowSailorBarWnd", "csHideSailorBarWnd", "csQuizSubject", "csQuizAnswer", "csLinkGameScore", "csGuessWinShip",
        "csOpenFBall", "csGuessFBall", "csGuessOlympic", "csSmallGameScore", "csTurnCard", "csLookAllCard",
        "csSendPoInfo", "csItemInfo", "csGetItem", "csOpenExpSystem", "csUpHonorListInfo", "csUpPropetylist", "csGetHonorRaward",
        "ReferItem", "csSaveNorthAmePlayer", "csChangeModel", "csChangeHatSwitch", "csChangeSpecial1",
        "csPhoneAid", "csCheckPhoneVerify", "csSucMake", "csSucMake1", "open", "State",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csShowSailorBarWnd" when Int(words, 1, out int game):
                session.ShowSailorBarGame(game, Int(words, 2, out int previous) ? previous : -1);
                return true;
            case "csHideSailorBarWnd":
                session.HideSailorBar(words);
                return true;
            case "csQuizSubject":
                session.StartQuiz();
                return true;
            case "csQuizAnswer" when Int(words, 1, out int number) && Int(words, 2, out int answer):
                session.AnswerQuiz(number, answer, true);
                return true;
            case "csLinkGameScore" when Int(words, 1, out int score):
                session.SaveLinkScore(score);
                return true;
            case "csGuessWinShip" when Int(words, 2, out int boat):
                SailorBarRace.Bet(session, boat);
                return true;
            case "csOpenFBall":
                session.OpenFootballGuess();
                return true;
            case "csGuessFBall" when words.Length >= 3 && Int(words, 1, out int round):
                session.GuessFootball(round, words[2]);
                return true;
            case "csGuessOlympic" when words.Length >= 2:
                session.GuessOlympic(words[1]);
                return true;
            case "csSmallGameScore" when Int(words, 1, out int won):
                session.FinishTowerGame(won != 0);
                return true;
            case "csTurnCard" when Int(words, 1, out int position):
                session.TurnTreasureCard(position);
                return true;
            case "csLookAllCard":
                session.LookAllTreasureCards();
                return true;
            case "csSendPoInfo" when Int(words, 1, out int track):
                session.SetCampTrack(track);
                return true;
            case "csItemInfo":
                session.SendCampGoodsInfo();
                return true;
            case "csGetItem" when words.Length >= 3 && Int(words, 2, out int count):
                session.BuyCampGoods(words[1], count);
                return true;
            case "csOpenExpSystem":
                session.ApplyCampProperties();
                return true;
            case "csUpHonorListInfo":
                session.SendMilitaryRankInfo();
                return true;
            case "csUpPropetylist" when words.Length >= 2:
                session.TrainMilitaryAttribute(words[1]);
                return true;
            case "csGetHonorRaward" when Int(words, 1, out int rank):
                session.ClaimMilitaryAward(rank);
                return true;
            case "ReferItem" when Int(words, 1, out int view) && Int(words, 2, out int index):
                session.MakeExploit(view, index);
                return true;
            case "csSaveNorthAmePlayer":
                session.SaveStatistics(words);
                return true;
            case "csChangeModel" when Int(words, 1, out int view) && Int(words, 2, out int index) && Int(words, 3, out int model):
                session.ChangeModel(view, index, model);
                return true;
            case "csChangeHatSwitch" when Int(words, 1, out int value):
                session.ChangeHatSwitch(value);
                return true;
            case "csChangeSpecial1" when Int(words, 1, out int bit) && Int(words, 2, out int value):
                session.ChangeSpecial1(bit, value);
                return true;
            case "csPhoneAid":
                session.SystemMessage("Phone verification is not needed on this server.", SystemMessageKind.Prompt);
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} leisure notice: {string.Join(' ', words)}");
                return true;
        }
    }

    private static bool Int(string[] words, int index, out int value)
    {
        value = 0;
        return words.Length > index && int.TryParse(words[index], out value);
    }
}
