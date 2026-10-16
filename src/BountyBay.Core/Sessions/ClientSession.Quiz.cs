using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int SailorBarBoatRace = 0;
    public const int SailorBarQuiz = 1;
    public const int SailorBarLink = 2;
    public const int SailorBarAuction = 3;
    public const int QuizNoAnswer = -1;

    private const int LeisureRankingRows = 10;

    private QuizRules QuizData => LeisureData.Quiz;

    private bool QuizRunning => Character.QuizLucky >= 0 && Character.QuizAsked < QuizLimit && Character.QuizOrder.Count > 0
        && Character.QuizOrder.All(index => index >= 0 && index < QuizData.Questions.Count);

    private int QuizLimit => Math.Min(QuizData.PerDay, Character.QuizOrder.Count > 0 ? Character.QuizOrder.Count : QuizData.Questions.Count);

    public void ShowSailorBarGame(int game, int previous)
    {
        if (previous == SailorBarBoatRace && game != SailorBarBoatRace) SailorBarRace.Leave(this);
        switch (game)
        {
            case SailorBarBoatRace:
                SailorBarRace.Watch(this);
                break;
            case SailorBarQuiz:
                OpenQuiz();
                break;
            case SailorBarLink:
                SendLinkRanking();
                break;
            default:
                Log.Debug(Tag, $"{Name} sailor bar game {game}");
                break;
        }
    }

    public void HideSailorBar(string[] words)
    {
        int game = words.Length > 1 && int.TryParse(words[1], out int value) ? value : -1;
        if (game == 1)
        {
            SailorBarRace.Leave(this);
            return;
        }
        if (game != 0 || words.Length < 4 || !int.TryParse(words[2], out int number) || !int.TryParse(words[3], out int answer)) return;
        if (!QuizRunning) return;
        if (answer is >= 1 and <= 4 && number == Character.QuizAsked + 1) AnswerQuiz(number, answer, false);
        else if (answer == QuizNoAnswer) SendQuizQuestion();
    }

    public void StartQuiz()
    {
        ResetQuizDay();
        QuizRules rules = QuizData;
        if (rules.Questions.Count == 0)
        {
            SystemMessage("There are no quiz questions today.", SystemMessageKind.Prompt);
            return;
        }
        if (Character.QuizAsked >= Math.Min(rules.PerDay, rules.Questions.Count))
        {
            SystemMessage("You have already answered all of today's questions.", SystemMessageKind.Prompt);
            return;
        }
        if (!QuizRunning)
        {
            Character.QuizLucky = Random.Shared.Next(3);
            Character.QuizOrder = Enumerable.Range(0, rules.Questions.Count).OrderBy(_ => Random.Shared.Next())
                .Take(Math.Min(rules.PerDay, rules.Questions.Count)).ToList();
            Character.QuizAsked = 0;
            Save();
        }
        Send(LeisurePackets.QuizLucky(Character.QuizLucky), "(quiz lucky number)");
        Send(LeisurePackets.OpenQuizMatch(LeisurePackets.QuizOpenAnswer), "(quiz answer window)");
        SendQuizQuestion();
    }

    public void AnswerQuiz(int number, int answer, bool showNext)
    {
        if (!QuizRunning || number != Character.QuizAsked + 1) return;
        QuizQuestion question = QuizData.Questions[Character.QuizOrder[Character.QuizAsked]];
        bool right = answer == question.Right;
        Character.QuizAsked++;
        if (right)
        {
            int bonus = Character.QuizLucky >= 0 && Character.QuizLucky < QuizData.LuckyBonusPercent.Count ? QuizData.LuckyBonusPercent[Character.QuizLucky] : 0;
            int gain = QuizData.RewardMoney * (100 + bonus) / 100;
            Character.QuizRight++;
            Character.QuizGain += gain;
            SetMoney((long)Money + gain);
        }
        Send(LeisurePackets.QuizResult(question.Right, answer is >= 1 and <= 4 ? answer : QuizNoAnswer), "(quiz result)");
        if (Character.QuizAsked >= QuizLimit)
        {
            Send(LeisurePackets.QuizOver(Character.QuizGain), "(quiz over)");
            Character.QuizLucky = -1;
            SystemMessage($"The quiz is over: {Character.QuizRight} right answer(s), {Character.QuizGain:N0} silver coins won.");
            Log.Info(Tag, $"{Name} quiz over: {Character.QuizRight}/{Character.QuizAsked}");
        }
        else if (showNext)
        {
            SendQuizQuestion();
        }
        Save();
        SendQuizRanking();
    }

    public void SaveLinkScore(int score)
    {
        if (score <= 0) return;
        if (Character.LinkDay != LeisureToday)
        {
            Character.LinkDay = LeisureToday;
            Character.LinkBestScore = 0;
        }
        if (score > Character.LinkBestScore) Character.LinkBestScore = score;
        int money = LeisureData.MiniGame.LinkMoneyPerPoint * score;
        if (money > 0) SetMoney((long)Money + money);
        Save();
        SendLinkRanking();
        Log.Info(Tag, $"{Name} link game score {score}");
    }

    private void OpenQuiz()
    {
        ResetQuizDay();
        SendQuizRanking();
        if (QuizRunning)
        {
            Send(LeisurePackets.OpenQuizMatch(LeisurePackets.QuizOpenAnswer), "(quiz answer window)");
            SendQuizQuestion();
            return;
        }
        if (Character.QuizAsked >= Math.Min(QuizData.PerDay, Math.Max(1, QuizData.Questions.Count)) || QuizData.Questions.Count == 0)
        {
            Send(LeisurePackets.OpenQuizMatch(LeisurePackets.QuizOpenBlank), "(quiz finished)");
            SystemMessage("You have already answered all of today's questions. Come back tomorrow.", SystemMessageKind.Prompt);
            return;
        }
        Send(LeisurePackets.OpenQuizMatch(LeisurePackets.QuizOpenCards), "(quiz cards)");
    }

    private void SendQuizQuestion()
    {
        if (!QuizRunning) return;
        QuizQuestion question = QuizData.Questions[Character.QuizOrder[Character.QuizAsked]];
        Send(LeisurePackets.QuizText(Character.QuizAsked + 1, question.Question, question.Answers, QuizData.Seconds), "(quiz question)");
    }

    private void ResetQuizDay()
    {
        if (Character.QuizDay == LeisureToday) return;
        Character.QuizDay = LeisureToday;
        Character.QuizAsked = 0;
        Character.QuizRight = 0;
        Character.QuizGain = 0;
        Character.QuizLucky = -1;
        Character.QuizOrder = [];
    }

    private void SendQuizRanking()
        => SendLeisureTable(LeisureTables.QuizRanking, Context.World.Sessions
            .Where(session => session.Character.QuizDay == LeisureToday && session.Character.QuizGain > 0)
            .OrderByDescending(session => session.Character.QuizGain).Take(LeisureRankingRows)
            .Select(session => new[] { session.Name, session.Character.QuizGain.ToString(CultureInfo.InvariantCulture) }), "(quiz ranking)");

    private void SendLinkRanking()
        => SendLeisureTable(LeisureTables.LinkRanking, Context.World.Sessions
            .Where(session => session.Character.LinkDay == LeisureToday && session.Character.LinkBestScore > 0)
            .OrderByDescending(session => session.Character.LinkBestScore).Take(LeisureRankingRows)
            .Select(session => new[] { session.Name, session.Character.LinkBestScore.ToString(CultureInfo.InvariantCulture) }), "(link game ranking)");
}
