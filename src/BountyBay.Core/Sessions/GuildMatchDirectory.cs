using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class GuildMatchDirectory
{
    public const int Lost = 3;
    public const int Profit = 1;
    public const int Returned = 2;

    private static readonly int[][] Feeders = [[], [2, 3], [4, 5], [6, 7]];
    private static readonly int[][] Seeds = [[0, 7], [3, 4], [1, 6], [2, 5]];

    public static GuildMatchRules Rules(ServerContext context) => GuildBuildDirectory.Rules(context).Match;

    public static GuildMatchFile Refresh(ServerContext context)
    {
        GuildMatchFile file = GuildMatchStore.Load(context.Settings);
        DateTime now = DateTime.UtcNow;
        string season = GuildBuildDirectory.SeasonKey(now);
        bool changed = false;
        if (file.Season != season)
        {
            foreach (GuildMatchBet bet in file.Bets.Where(bet => bet.Result == 0))
            {
                bet.Result = Returned;
                bet.Reward = bet.Amount;
            }
            Seed(context, file, season);
            changed = true;
        }
        DateTime start = GuildBuildDirectory.SeasonStart(now);
        foreach (int slot in new[] { 4, 5, 6, 7, 2, 3, 1 }) changed |= Advance(context, file, Slot(file, slot), start, now);
        if (changed) GuildMatchStore.Save();
        return file;
    }

    public static GuildMatchSlot Slot(GuildMatchFile file, int slot)
    {
        GuildMatchSlot? found = file.Slots.FirstOrDefault(entry => entry.Slot == slot);
        if (found != null) return found;
        found = new GuildMatchSlot { Slot = slot };
        file.Slots.Add(found);
        return found;
    }

    public static int Round(int slot) => slot == 1 ? 1 : slot <= 3 ? 2 : 4;

    public static GuildMatchSide Side(ServerContext context, string name)
    {
        Guild? guild = GuildDirectory.Find(context, name);
        if (guild == null) return new GuildMatchSide(name, 0, 0, "-");
        string leader = guild.Members.FirstOrDefault(member => member.Duty == GuildDuty.Leader)?.Name ?? "-";
        return new GuildMatchSide(guild.Name, guild.Flag, GuildBuildDirectory.State(context, guild).Activity, leader);
    }

    private static void Seed(ServerContext context, GuildMatchFile file, string season)
    {
        file.Season = season;
        file.Champion = "";
        file.Slots.Clear();
        List<Guild> guilds = GuildBuildDirectory.All(context)
            .OrderByDescending(guild => guild.Level)
            .ThenByDescending(guild => GuildBuildDirectory.State(context, guild).Activity)
            .ThenByDescending(guild => guild.Members.Count)
            .Take(Math.Clamp(Rules(context).Guilds, 0, 8))
            .ToList();
        for (int slot = 1; slot <= GuildMatchPackets.Slots; slot++) Slot(file, slot);
        for (int i = 0; i < Seeds.Length; i++)
        {
            GuildMatchSlot quarter = Slot(file, 4 + i);
            quarter.Left = Seeds[i][0] < guilds.Count ? guilds[Seeds[i][0]].Name : "";
            quarter.Right = Seeds[i][1] < guilds.Count ? guilds[Seeds[i][1]].Name : "";
        }
        Log.Info("GuildMatch", $"Season {season}: {guilds.Count} guilds seeded");
    }

    private static bool Advance(ServerContext context, GuildMatchFile file, GuildMatchSlot match, DateTime start, DateTime now)
    {
        if (match.State >= GuildMatchPackets.LeftWon) return false;
        if (match.Slot <= 3)
        {
            GuildMatchSlot first = Slot(file, Feeders[match.Slot][0]);
            GuildMatchSlot second = Slot(file, Feeders[match.Slot][1]);
            if (!Done(first) || !Done(second)) return false;
            match.Left = first.Winner;
            match.Right = second.Winner;
        }
        if (match.Left.Length == 0 && match.Right.Length == 0) return false;
        if (match.Left.Length == 0 || match.Right.Length == 0)
        {
            match.Winner = match.Left.Length > 0 ? match.Left : match.Right;
            match.State = match.Left.Length > 0 ? GuildMatchPackets.LeftWon : GuildMatchPackets.RightWon;
            match.Score = "bye";
            if (match.Slot == 1) file.Champion = match.Winner;
            return true;
        }
        GuildMatchRules rules = Rules(context);
        int hour = match.Slot == 1 ? rules.FinalHour : match.Slot <= 3 ? rules.SemiHour : rules.QuarterHour;
        if (now < start.AddHours(hour))
        {
            if (match.State == GuildMatchPackets.Betting) return false;
            match.State = GuildMatchPackets.Betting;
            return true;
        }
        Resolve(context, file, match);
        return true;
    }

    private static bool Done(GuildMatchSlot slot) => slot.State >= GuildMatchPackets.LeftWon || (slot.Left.Length == 0 && slot.Right.Length == 0);

    private static void Resolve(ServerContext context, GuildMatchFile file, GuildMatchSlot match)
    {
        double left = Strength(context, match.Left);
        double right = Strength(context, match.Right);
        int leftWins = 0;
        int rightWins = 0;
        match.Rounds.Clear();
        while (leftWins < 2 && rightWins < 2)
        {
            bool leftTakes = Random.Shared.NextDouble() < left / (left + right);
            match.Rounds.Add(leftTakes ? 1 : 2);
            if (leftTakes) leftWins++;
            else rightWins++;
        }
        bool leftWon = leftWins > rightWins;
        match.Winner = leftWon ? match.Left : match.Right;
        match.State = leftWon ? GuildMatchPackets.LeftWon : GuildMatchPackets.RightWon;
        match.Score = $"{leftWins}:{rightWins}";
        if (match.Slot == 1) file.Champion = match.Winner;
        Settle(Rules(context), file, match, leftWon ? 1 : 2);
        GuildActivityRules activity = GuildBuildDirectory.Rules(context).Activity;
        foreach (string name in new[] { match.Left, match.Right })
        {
            if (GuildDirectory.Find(context, name) is not { } guild) continue;
            bool won = name == match.Winner;
            GuildBuildDirectory.AddActivity(context, guild, won ? activity.MatchWin : activity.MatchPlayed, "guild tournament");
            GuildBuildDirectory.Note(context, guild, won
                ? $"Won the tournament match against {(name == match.Left ? match.Right : match.Left)} {match.Score}."
                : $"Lost the tournament match against {match.Winner} {match.Score}.");
        }
        Log.Info("GuildMatch", $"Slot {match.Slot}: {match.Left} vs {match.Right} -> {match.Winner} {match.Score}");
    }

    private static double Strength(ServerContext context, string name)
    {
        if (GuildDirectory.Find(context, name) is not { } guild) return 10;
        return 10 + guild.Members.Count * 10 + guild.Level * 50 + GuildBuildDirectory.State(context, guild).Activity / 100.0;
    }

    private static void Settle(GuildMatchRules rules, GuildMatchFile file, GuildMatchSlot match, int winner)
    {
        List<GuildMatchBet> bets = file.Bets.Where(bet => bet.Season == file.Season && bet.Slot == match.Slot && bet.Result == 0).ToList();
        long total = bets.Sum(bet => bet.Amount);
        long winning = bets.Where(bet => bet.Side == winner).Sum(bet => bet.Amount);
        foreach (GuildMatchBet bet in bets)
        {
            if (bet.Side != winner)
            {
                bet.Result = Lost;
                continue;
            }
            double odds = Math.Max(rules.MinimumOdds, winning > 0 ? (double)total / winning : 1.0);
            bet.Result = Profit;
            bet.Reward = (long)Math.Round(bet.Amount * odds);
        }
    }
}
