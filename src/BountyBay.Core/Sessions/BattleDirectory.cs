using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class BattleFighter
{
    public BattleFighter(ClientSession session, int camp)
    {
        Session = session;
        Camp = camp;
        Name = session.Name;
        Guild = session.Properties.Text("ConsName");
        Rating = session.Character.ArenaRating;
    }

    public ClientSession Session { get; }
    public int Camp { get; set; }
    public string Name { get; }
    public string Guild { get; }
    public int Rating { get; }
    public bool Entered { get; set; }
    public bool Left { get; set; }
    public bool Alive { get; set; } = true;
    public long EnteredAt { get; set; }
    public long ReliveAt { get; set; }
    public long NextResourceAt { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Damage { get; set; }
    public int BossDamage { get; set; }
    public int Flags { get; set; }
    public int Resources { get; set; }
    public int Score { get; set; }
    public int HonorGained { get; set; }
    public int BadgesGained { get; set; }

    public bool Present => Entered && !Left;
}

public sealed class BattleRoom
{
    public BattleRoom(int id, BattleDefinition definition, int level)
    {
        Id = id;
        Definition = definition;
        Level = level;
    }

    public int Id { get; }
    public BattleDefinition Definition { get; }
    public int Level { get; }
    public List<BattleFighter> Fighters { get; } = [];
    public Dictionary<int, int> FlagOwners { get; } = [];
    public HashSet<int> FallenGenerals { get; } = [];
    public long StartAt { get; set; }
    public long EndAt { get; set; }
    public long CloseAt { get; set; }
    public long NextStatusAt { get; set; }
    public bool Over { get; set; }
    public int Winner { get; set; }

    public IEnumerable<BattleFighter> Camp(int camp) => Fighters.Where(fighter => fighter.Camp == camp && fighter.Present);

    public int CampScore(int camp) => Fighters.Where(fighter => fighter.Camp == camp).Sum(fighter => fighter.Score);

    public int CampResources(int camp) => Fighters.Where(fighter => fighter.Camp == camp).Sum(fighter => fighter.Resources);

    public int CampAlive(int camp) => Camp(camp).Count(fighter => fighter.Alive);

    public int CampDamage(int camp) => Fighters.Where(fighter => fighter.Camp == camp).Sum(fighter => fighter.Damage);

    public BattleFighter? Find(ClientSession session) => Fighters.FirstOrDefault(fighter => fighter.Session == session);
}

public sealed class BattleTicket
{
    public BattleTicket(ClientSession session, BattleDefinition definition, int level, int group)
    {
        Session = session;
        Definition = definition;
        Level = level;
        Group = group;
        QueuedAt = Environment.TickCount64;
    }

    public ClientSession Session { get; }
    public BattleDefinition Definition { get; }
    public int Level { get; }
    public int Group { get; }
    public long QueuedAt { get; }
}

public sealed class BattleInvite
{
    public BattleInvite(BattleRoom room, BattleFighter fighter, long deadline)
    {
        Room = room;
        Fighter = fighter;
        Deadline = deadline;
    }

    public BattleRoom Room { get; }
    public BattleFighter Fighter { get; }
    public long Deadline { get; }
}

public static class BattleDirectory
{
    private static readonly List<BattleTicket> Tickets = [];
    private static readonly List<BattleRoom> Rooms = [];
    private static readonly Dictionary<ClientSession, BattleInvite> Invites = [];
    private static int _nextRoom;
    private static int _nextGroup;
    private static bool _ticking;

    public static IReadOnlyList<BattleRoom> ActiveRooms => Rooms;

    public static int NewGroup() => ++_nextGroup;

    public static void EnsureTicking(World world)
    {
        if (_ticking) return;
        _ticking = true;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync())
            {
                lock (world.Sync)
                {
                    try
                    {
                        Tick();
                        CopyDirectory.Tick();
                    }
                    catch (Exception exception)
                    {
                        Log.Error("Battle", exception.ToString());
                    }
                }
            }
        });
    }

    public static BattleTicket? Ticket(ClientSession session) => Tickets.FirstOrDefault(ticket => ticket.Session == session);

    public static BattleRoom? RoomOf(ClientSession session)
        => Rooms.FirstOrDefault(room => room.Fighters.Any(fighter => fighter.Session == session && !fighter.Left));

    public static BattleInvite? InviteOf(ClientSession session) => Invites.GetValueOrDefault(session);

    public static int Queued(BattleDefinition definition, int level)
        => Tickets.Count(ticket => ticket.Definition == definition && ticket.Level == level);

    public static void Enqueue(BattleTicket ticket)
    {
        Tickets.RemoveAll(existing => existing.Session == ticket.Session);
        Tickets.Add(ticket);
        Match(ticket.Definition, ticket.Level);
    }

    public static bool Dequeue(ClientSession session) => Tickets.RemoveAll(ticket => ticket.Session == session) > 0;

    public static void Forget(ClientSession session)
    {
        Dequeue(session);
        if (Invites.Remove(session, out BattleInvite? invite)) invite.Fighter.Left = true;
    }

    public static void Decline(ClientSession session)
    {
        if (!Invites.Remove(session, out BattleInvite? invite)) return;
        invite.Fighter.Left = true;
        session.SystemMessage($"You declined to enter {invite.Room.Definition.Name}.");
    }

    public static void Accept(ClientSession session)
    {
        if (!Invites.Remove(session, out BattleInvite? invite)) return;
        if (invite.Room.Over)
        {
            session.SystemMessage($"{invite.Room.Definition.Name} has already ended.");
            return;
        }
        session.BattleEnterRoom(invite.Room, invite.Fighter);
    }

    private static void Match(BattleDefinition definition, int level)
    {
        List<BattleTicket> waiting = Tickets.Where(ticket => ticket.Definition == definition && ticket.Level == level && ticket.Session.InWorld)
            .OrderBy(ticket => ticket.QueuedAt).ToList();
        int capacity = definition.Mode == "Duel" ? 2 : Math.Max(2, definition.MaxPerCamp * 2);
        if (waiting.Count < Math.Max(1, definition.MinPlayers)) return;

        var room = new BattleRoom(++_nextRoom, definition, level);
        int[] sizes = [0, 0, 0];
        foreach (IGrouping<int, BattleTicket> group in waiting.GroupBy(ticket => ticket.Group))
        {
            List<BattleTicket> members = group.ToList();
            if (room.Fighters.Count + members.Count > capacity) continue;
            int camp = sizes[1] <= sizes[2] ? 1 : 2;
            if (sizes[camp] + members.Count > Math.Max(1, definition.MaxPerCamp)) camp = 3 - camp;
            if (sizes[camp] + members.Count > Math.Max(1, definition.MaxPerCamp)) continue;
            foreach (BattleTicket ticket in members)
            {
                room.Fighters.Add(new BattleFighter(ticket.Session, camp));
                Tickets.Remove(ticket);
            }
            sizes[camp] += members.Count;
        }
        if (room.Fighters.Count == 0) return;

        long now = Environment.TickCount64;
        BattleBook book = BattleBook.For(room.Fighters[0].Session.Context.Settings);
        room.StartAt = now;
        room.EndAt = now + Math.Max(1, definition.Minutes) * 60_000L;
        room.NextStatusAt = now;
        Rooms.Add(room);
        Log.Info("Battle", $"{definition.Name} #{room.Id} level {level} starts with {room.Fighters.Count} player(s)");
        foreach (BattleFighter fighter in room.Fighters)
        {
            fighter.Session.Send(BattlePackets.NotEntered(), "(battle queue left)");
            Invites[fighter.Session] = new BattleInvite(room, fighter, now + Math.Max(1, book.ConfirmSeconds) * 1000L);
            fighter.Session.Send(UiPackets.ShowQuestionBox($"{definition.Name} is ready. Enter the battlefield now?", "AskToBattle_Yes", "AskToBattle_No"),
                "(battle ready)");
        }
    }

    private static void Tick()
    {
        long now = Environment.TickCount64;
        foreach ((ClientSession session, BattleInvite invite) in Invites.ToList())
        {
            if (!session.InWorld)
            {
                Invites.Remove(session);
                invite.Fighter.Left = true;
            }
            else if (now >= invite.Deadline)
            {
                Accept(session);
            }
        }
        foreach (BattleTicket ticket in Tickets.Where(ticket => !ticket.Session.InWorld).ToList()) Tickets.Remove(ticket);

        foreach (BattleRoom room in Rooms.ToList())
        {
            foreach (BattleFighter fighter in room.Fighters.Where(fighter => fighter.Present && fighter.ReliveAt > 0 && now >= fighter.ReliveAt))
            {
                fighter.ReliveAt = 0;
                fighter.Session.BattleRelive(room, fighter);
            }
            if (!room.Over)
            {
                if (now >= room.EndAt || Decided(room)) Finish(room);
                else if (now >= room.NextStatusAt) Status(room);
            }
            else if (now >= room.CloseAt)
            {
                foreach (BattleFighter fighter in room.Fighters.Where(fighter => fighter.Present).ToList()) fighter.Session.BattleExit("(battle closed)");
                Rooms.Remove(room);
            }
            if (!room.Over && room.Fighters.All(fighter => fighter.Left) && Invites.Values.All(invite => invite.Room != room))
            {
                Log.Info("Battle", $"{room.Definition.Name} #{room.Id} is empty and closes");
                Rooms.Remove(room);
            }
        }
    }

    public static void Status(BattleRoom room)
    {
        room.NextStatusAt = Environment.TickCount64 + 10_000;
        if (room.Over) return;
        long left = Math.Max(0, room.EndAt - Environment.TickCount64) / 1000;
        string score = room.Definition.Mode switch
        {
            "Resources" => $"Red {room.CampResources(1)} : Blue {room.CampResources(2)} resources",
            "General" or "Survivors" or "Duel" => $"Red {room.CampAlive(1)} : Blue {room.CampAlive(2)} standing",
            _ => $"Red {room.CampScore(1)} : Blue {room.CampScore(2)}",
        };
        byte[] text = BattlePackets.BattleText($"{room.Definition.Name}  {score}  {left / 60:00}:{left % 60:00}");
        foreach (BattleFighter fighter in room.Fighters.Where(fighter => fighter.Present)) fighter.Session.Send(text, "(battle status)");
    }

    private static bool Decided(BattleRoom room)
    {
        BattleDefinition definition = room.Definition;
        if (!room.Fighters.Any(fighter => fighter.Entered)) return false;
        if (definition.Mode == "Resources" && definition.ResourceTarget > 0
            && (room.CampResources(1) >= definition.ResourceTarget || room.CampResources(2) >= definition.ResourceTarget)) return true;
        if (definition.Mode == "General" && room.FallenGenerals.Count > 0) return true;
        if (definition.Mode is "Duel" or "Survivors" or "General")
        {
            bool started = Environment.TickCount64 - room.StartAt > 30_000;
            return started && (room.CampAlive(1) == 0 || room.CampAlive(2) == 0);
        }
        return false;
    }

    private static int DecideWinner(BattleRoom room)
    {
        BattleDefinition definition = room.Definition;
        int Compare(int red, int blue) => red > blue ? 1 : blue > red ? 2 : 0;
        switch (definition.Mode)
        {
            case "Resources":
                return Compare(room.CampResources(1), room.CampResources(2));
            case "General":
                if (room.FallenGenerals.Count == 1) return room.FallenGenerals.Contains(1) ? 2 : 1;
                int general = Compare(room.CampAlive(1), room.CampAlive(2));
                return general != 0 ? general : Compare(room.CampScore(1), room.CampScore(2));
            case "Duel":
            case "Survivors":
                int alive = Compare(room.CampAlive(1), room.CampAlive(2));
                if (alive != 0) return alive;
                int damage = Compare(room.CampDamage(1), room.CampDamage(2));
                if (damage != 0 || definition.Mode == "Survivors") return damage;
                BattleFighter? first = room.Fighters.Where(fighter => fighter.Entered).MinBy(fighter => fighter.EnteredAt);
                return first?.Camp ?? 0;
            default:
                return Compare(room.CampScore(1), room.CampScore(2));
        }
    }

    public static void Finish(BattleRoom room)
    {
        if (room.Over) return;
        room.Over = true;
        room.Winner = DecideWinner(room);
        BattleBook book = BattleBook.For((room.Fighters.FirstOrDefault()?.Session ?? throw new InvalidOperationException("empty battle")).Context.Settings);
        room.CloseAt = Environment.TickCount64 + Math.Max(5, book.ReturnSeconds) * 1000L;
        Log.Info("Battle", $"{room.Definition.Name} #{room.Id} is over, winner camp {room.Winner}");

        foreach (BattleFighter fighter in room.Fighters.Where(fighter => fighter.Entered)) fighter.Session.BattleReward(room, fighter);

        TableDefinition? table = BattleTables.Report(room.Definition.Report);
        List<object[]> rows = room.Fighters.Where(fighter => fighter.Entered).Select(fighter => ReportRow(room, fighter)).ToList();
        foreach (BattleFighter fighter in room.Fighters.Where(fighter => fighter.Present))
        {
            ClientSession session = fighter.Session;
            if (table != null)
            {
                session.Send(TablePackets.Clear(table), $"(clear {table.Name})");
                session.Send(TablePackets.Rows(table, rows), $"({table.Name})");
                session.Send(BattlePackets.Winner(room.Definition.Report, room.Winner), "(battle winner)");
                session.Send(BattlePackets.ShowReport(room.Definition.Report), "(battle report)");
            }
            string result = room.Winner == 0 ? "The battle ended in a draw." : room.Winner == fighter.Camp ? "Your side won the battle!" : "Your side lost the battle.";
            session.Send(BattlePackets.BattleText(result), "(battle result)");
            session.SystemMessage($"{room.Definition.Name}: {result} You will leave the battlefield in {book.ReturnSeconds} seconds.");
        }
    }

    private static object[] ReportRow(BattleRoom room, BattleFighter fighter)
    {
        string[] values = room.Definition.Report switch
        {
            "HonorSea" =>
            [
                $"{fighter.Camp}", fighter.Name, fighter.Guild, $"{fighter.Damage}", "0", "0", $"{fighter.Resources}", $"{fighter.Kills}",
                $"{fighter.Deaths}", $"{fighter.HonorGained}", $"{fighter.BadgesGained}", $"{fighter.Score}",
            ],
            "TeamWar" =>
            [
                $"{fighter.Camp}", fighter.Name, fighter.Guild, $"{fighter.Damage}", "0", "0", "0", $"{fighter.Flags}", $"{fighter.Resources}", "0",
                $"{fighter.Kills}", $"{fighter.Deaths}", $"{fighter.BossDamage}", "0", $"{fighter.HonorGained}", $"{fighter.BadgesGained}", $"{fighter.Score}",
            ],
            "PaleWar" =>
            [
                $"{fighter.Camp}", fighter.Name, fighter.Guild, $"{fighter.Damage}", $"{fighter.BossDamage}", $"{fighter.Kills}", $"{fighter.Deaths}",
                fighter.Alive ? "1" : "0", $"{fighter.HonorGained}", $"{fighter.BadgesGained}", $"{fighter.Score}",
            ],
            _ =>
            [
                $"{fighter.Camp}", fighter.Name, fighter.Guild, $"{fighter.Damage}", $"{fighter.Flags}", $"{fighter.Resources}", $"{fighter.Kills}",
                $"{fighter.Deaths}", $"{fighter.HonorGained}", $"{fighter.BadgesGained}", $"{fighter.Score}",
            ],
        };
        return [.. values.Select(value => (object)value)];
    }
}
