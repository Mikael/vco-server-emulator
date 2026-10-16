using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed record SailorBarBet(string Player, int Boat, long Money);

public static class SailorBarRace
{
    public const int Boats = 8;
    public const int FinishLine = 300;

    private static readonly HashSet<ClientSession> Watchers = [];
    private static readonly List<SailorBarBet> Bets = [];
    private static readonly List<string[]> SingleWinners = [];
    private static readonly Dictionary<string, long> TotalWinnings = [];
    private static readonly float[] Speeds = new float[Boats];
    private static readonly int[] Wins = new int[Boats];
    private static ServerContext? _context;
    private static Timer? _timer;
    private static int _phase = LeisurePackets.BoatBetting;
    private static DateTime _phaseEnds = DateTime.MinValue;
    private static int _winner;

    public static void Watch(ClientSession session)
    {
        EnsureRunning(session.Context);
        Watchers.Add(session);
        SendState(session);
    }

    public static void Leave(ClientSession session) => Watchers.Remove(session);

    public static void Bet(ClientSession session, int boat)
    {
        EnsureRunning(session.Context);
        BoatRaceRules rules = LeisureRules.For(session.Context.Settings).BoatRace;
        if (_phase != LeisurePackets.BoatBetting)
        {
            session.SystemMessage("Betting is closed while the boats are racing.", SystemMessageKind.Prompt);
            return;
        }
        if (boat is < 0 or >= Boats) return;
        if (Bets.Count(bet => bet.Player == session.Name) >= rules.MaxBetsPerRace)
        {
            session.SystemMessage($"You can place at most {rules.MaxBetsPerRace} bets per race.", SystemMessageKind.Prompt);
            return;
        }
        if (session.Money < rules.BetMoney)
        {
            session.SystemMessage($"A bet costs {rules.BetMoney:N0} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        session.SetMoney((long)session.Money - rules.BetMoney);
        session.Save();
        Bets.Add(new SailorBarBet(session.Name, boat, rules.BetMoney));
        session.SystemMessage($"You bet {rules.BetMoney:N0} silver coins on boat {boat + 1}.");
        Log.Info(session.Tag, $"{session.Name} bets {rules.BetMoney} on boat {boat + 1}");
        foreach (ClientSession watcher in Watchers.ToList()) SendBets(watcher);
    }

    private static void EnsureRunning(ServerContext context)
    {
        if (_timer != null) return;
        _context = context;
        StartBetting(LeisureRules.For(context.Settings).BoatRace);
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Log.Info("SailorBar", "dragon boat race started");
    }

    private static void Tick()
    {
        if (_context == null) return;
        try
        {
            lock (_context.World.Sync) Update(_context);
        }
        catch (Exception exception)
        {
            Log.Error("SailorBar", $"race tick failed: {exception.Message}");
        }
    }

    private static void Update(ServerContext context)
    {
        Watchers.RemoveWhere(session => !context.World.Sessions.Contains(session));
        if (DateTime.UtcNow < _phaseEnds) return;
        BoatRaceRules rules = LeisureRules.For(context.Settings).BoatRace;
        if (_phase == LeisurePackets.BoatBetting) StartRace(rules);
        else
        {
            Finish(context, rules);
            StartBetting(rules);
        }
    }

    private static void StartBetting(BoatRaceRules rules)
    {
        _phase = LeisurePackets.BoatBetting;
        _phaseEnds = DateTime.UtcNow.AddSeconds(Math.Max(10, rules.BetSeconds));
        Bets.Clear();
        foreach (ClientSession watcher in Watchers.ToList()) SendState(watcher);
    }

    private static void StartRace(BoatRaceRules rules)
    {
        int seconds = Math.Max(10, rules.RaceSeconds);
        _phase = LeisurePackets.BoatRacing;
        _phaseEnds = DateTime.UtcNow.AddSeconds(seconds);
        _winner = Random.Shared.Next(Boats);
        float fastest = FinishLine / (float)Math.Max(5, seconds - 2);
        for (int boat = 0; boat < Boats; boat++)
            Speeds[boat] = boat == _winner ? fastest : fastest * (0.82f + 0.15f * (float)Random.Shared.NextDouble());
        foreach (ClientSession watcher in Watchers.ToList())
        {
            SendState(watcher);
            watcher.Send(LeisurePackets.ShipMatchStart(), "(dragon boat race start)");
        }
    }

    private static void Finish(ServerContext context, BoatRaceRules rules)
    {
        Wins[_winner]++;
        foreach (ClientSession watcher in Watchers.ToList()) watcher.Send(LeisurePackets.ShipMatchArrive(_winner), "(dragon boat winner)");
        SingleWinners.Clear();
        foreach (IGrouping<string, SailorBarBet> group in Bets.Where(bet => bet.Boat == _winner).GroupBy(bet => bet.Player))
        {
            long prize = group.Sum(bet => bet.Money) * Math.Max(1, rules.PayoutFactor);
            TotalWinnings[group.Key] = TotalWinnings.GetValueOrDefault(group.Key) + prize;
            SingleWinners.Add([group.Key, prize.ToString(CultureInfo.InvariantCulture)]);
            ClientSession? player = context.World.Sessions.FirstOrDefault(session => session.Name == group.Key);
            if (player != null)
            {
                player.SetMoney((long)player.Money + prize);
                player.Save();
                player.SystemMessage($"Boat {_winner + 1} won the race: you receive {prize:N0} silver coins.", SystemMessageKind.Upgrade);
            }
            else
            {
                Log.Warn("SailorBar", $"{group.Key} won {prize} while offline; the prize is lost");
            }
        }
        Log.Info("SailorBar", $"boat {_winner + 1} wins; {SingleWinners.Count} winner(s)");
    }

    private static void SendState(ClientSession session)
    {
        int seconds = (int)Math.Max(0, Math.Ceiling((_phaseEnds - DateTime.UtcNow).TotalSeconds));
        session.SendLeisureTable(LeisureTables.BoatRunners, Enumerable.Range(0, Boats).Select(boat => new[]
        {
            boat.ToString(CultureInfo.InvariantCulture), (boat % 3 + 1).ToString(CultureInfo.InvariantCulture), BoatName(session, boat),
            Wins[boat].ToString(CultureInfo.InvariantCulture), "-", "-",
            Speeds[boat].ToString("0.00", CultureInfo.InvariantCulture),
        }), "(dragon boats)");
        SendBets(session);
        session.SendLeisureTable(LeisureTables.BoatSingleWinners, SingleWinners, "(dragon boat winners)");
        session.SendLeisureTable(LeisureTables.BoatTotalWinners, TotalWinnings.OrderByDescending(pair => pair.Value).Take(10)
            .Select(pair => new[] { pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture) }), "(dragon boat total winners)");
        if (_phase == LeisurePackets.BoatRacing)
        {
            double elapsed = Math.Max(0, LeisureRules.For(session.Context.Settings).BoatRace.RaceSeconds - seconds);
            session.SendLeisureTable(LeisureTables.BoatRace, Enumerable.Range(0, Boats).Select(boat => new[]
            {
                boat.ToString(CultureInfo.InvariantCulture), "0",
                ((int)Math.Min(FinishLine - 1, Speeds[boat] * elapsed)).ToString(CultureInfo.InvariantCulture),
                Speeds[boat].ToString("0.00", CultureInfo.InvariantCulture),
            }), "(dragon boat positions)");
        }
        session.Send(LeisurePackets.ShipMatchInfo(_phase, seconds), "(dragon boat race state)");
    }

    private static void SendBets(ClientSession session)
    {
        session.SendLeisureTable(LeisureTables.BoatOwnBets, Enumerable.Range(0, Boats).Select(boat => new[]
        {
            boat.ToString(CultureInfo.InvariantCulture),
            Bets.Where(bet => bet.Boat == boat && bet.Player == session.Name).Sum(bet => bet.Money).ToString(CultureInfo.InvariantCulture),
        }), "(own dragon boat bets)");
        session.SendLeisureTable(LeisureTables.BoatTotalBets, Enumerable.Range(0, Boats).Select(boat => new[]
        {
            boat.ToString(CultureInfo.InvariantCulture),
            Bets.Where(bet => bet.Boat == boat).Sum(bet => bet.Money).ToString(CultureInfo.InvariantCulture),
        }), "(dragon boat bets)");
    }

    private static string BoatName(ClientSession session, int boat)
    {
        List<string> names = LeisureRules.For(session.Context.Settings).BoatRace.Boats;
        return boat < names.Count ? names[boat] : $"Boat {boat + 1}";
    }
}
