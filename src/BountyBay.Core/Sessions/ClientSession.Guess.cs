using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int GuessGroupSize = 4;
    private const int GuessPerGroup = 2;

    private GuessRules GuessData => LeisureData.Guess;

    public void OpenFootballGuess()
    {
        GuessRules rules = GuessData;
        Send(LeisurePackets.FootballGuess(0, Character.FootballChampion >= 0 ? Character.FootballChampion.ToString() : ""), "(world cup champion guess)");
        if (rules.FootballRound >= 1)
            Send(LeisurePackets.FootballGuess(rules.FootballRound, Character.FootballRounds.GetValueOrDefault(rules.FootballRound, "")), "(world cup round guess)");
    }

    public void GuessFootball(int round, string picks)
    {
        GuessRules rules = GuessData;
        if (round == 0)
        {
            if (Character.FootballChampion >= 0 || !int.TryParse(picks, out int team) || team < 0 || team >= rules.Teams) return;
            Character.FootballChampion = team;
        }
        else
        {
            if (round != rules.FootballRound || round > rules.RoundPicks.Count || Character.FootballRounds.ContainsKey(round))
            {
                SystemMessage("Guesses for this round are closed.", SystemMessageKind.Prompt);
                return;
            }
            List<int> teams = [];
            foreach (string part in picks.Split('-', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out int team) || team < 0 || team >= rules.Teams || teams.Contains(team)) return;
                teams.Add(team);
            }
            if (teams.Count != rules.RoundPicks[round - 1] || teams.GroupBy(team => team / GuessGroupSize).Any(group => group.Count() > GuessPerGroup)) return;
            Character.FootballRounds[round] = string.Join('-', teams.Order());
        }
        Save();
        OpenFootballGuess();
        SystemMessage("Your guess has been recorded.");
        Log.Info(Tag, $"{Name} football guess round {round}: {picks}");
    }

    public void OpenGuessOlympic()
    {
        if (!GuessData.OlympicOpen)
        {
            SystemMessage("The Olympic guessing game is closed.", SystemMessageKind.Prompt);
            return;
        }
        Send(LeisurePackets.ShowGuessOlympic(), "(olympic guess window)");
    }

    public void GuessOlympic(string text)
    {
        string[] parts = text.Split('-');
        if (parts.Length < 3 || parts.Take(3).Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit))) return;
        if (Character.OlympicGuess.Length > 0)
        {
            SystemMessage("You have already handed in your Olympic guess.", SystemMessageKind.Prompt);
            return;
        }
        Character.OlympicGuess = text;
        Save();
        SystemMessage("Your Olympic medal guess has been recorded.");
        Log.Info(Tag, $"{Name} olympic guess {text}");
    }

    public void StartTowerGame() => Send(LeisurePackets.BeginTowerGame(), "(tower link game)");

    public void FinishTowerGame(bool won)
    {
        MiniGameRules rules = LeisureData.MiniGame;
        if (Character.TowerGameDay != LeisureToday)
        {
            Character.TowerGameDay = LeisureToday;
            Character.TowerGamesWon = 0;
        }
        if (!won)
        {
            SystemMessage("The game was not cleared.", SystemMessageKind.Prompt);
            return;
        }
        if (Character.TowerGamesWon >= rules.TowerWinsPerDay)
        {
            SystemMessage("You have already won enough mini games today.", SystemMessageKind.Prompt);
            return;
        }
        Character.TowerGamesWon++;
        if (rules.TowerWinMoney > 0) SetMoney((long)Money + rules.TowerWinMoney);
        Save();
        SystemMessage($"You won the mini game and receive {rules.TowerWinMoney:N0} silver coins.");
    }
}
