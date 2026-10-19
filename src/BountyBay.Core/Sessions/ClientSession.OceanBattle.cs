using System.Numerics;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Ocean battles. The high sea (scenes 1000001-1000004, DOceanRole/DOceanNPC) has no broadsides: the client's ocean classes
// handle no 3Dfd. A fight there moves everyone into a sea battle field (scene Type 1, DSeaRole/DSeaNPC, cannons in real
// time) and back to the ocean afterwards. CLIENT: ini\scenes\1900001.ini is an open-sea field (SCENE\900\001, walk_patch
// +-3000) ringed by buoys at radius ~3100 (biaoji.gm). GUESS: which field the server used, the fleet size, the start
// positions and the escape distance (the client keeps none of it).
public static class OceanBattleRules
{
    public const int FirstShipId = 7_600_000;
    public const int BattleField = 1900001;
    public const float HuntRange = 40f;
    public const float TeamJoinRange = 60f;
    public const float StartGap = 240f;
    public const float EscapeDistance = 480f;
    public const float FieldRadius = 3000f;
    public const int ReturnSeconds = 6;
    public const int SafeSeconds = 20;
    public const int ArrivalGraceSeconds = 10;
    public const int PaidOffSeconds = 90;
    public const int EscortHullPercent = 70;
    public const int EscortGoldPercent = 30;

    // Raiders and pirates open fire on sight (the ocean name label carries FirstAttack); navies and convoys wait.
    public static bool Hunts(SeaNpcState ship)
        => ClientSession.BboOceanScene(ship.Definition.Scene) && !ship.Definition.Friendly && !ship.Temporary
           && System.Text.RegularExpressions.Regex.IsMatch(ship.Definition.Name, "(?i)pirate|raid|corsair|demon|devil|hades|skeleton|jackal");

    // A flagship plus one escort per 10 levels, at most four.
    public static int Escorts(SeaNpcDefinition flagship) => flagship.Boss ? 4 : Math.Clamp(flagship.Level / 10 + 1, 1, 4);
}

public sealed class OceanBattleRoom
{
    private static int _next;

    public OceanBattleRoom(SeaNpcState? foe, int ocean, Vector3 at)
    {
        Id = Interlocked.Increment(ref _next);
        Foe = foe;
        Ocean = ocean;
        At = at;
    }

    public int Id { get; }

    // The NPC fleet of an NPC battle; null for a duel between players (ship-vs-player on the high sea).
    public SeaNpcState? Foe { get; }

    // Duels: the side (0 attackers, 1 defenders) of each fighter.
    public Dictionary<ClientSession, int> Sides { get; } = [];

    public string FoeName => Foe?.Definition.Name ?? "enemy";

    public int Ocean { get; }

    public Vector3 At { get; }

    public List<ClientSession> Fighters { get; } = [];

    public bool Over { get; set; }

    public bool Won { get; set; }
}

public sealed partial class ClientSession
{
    private static readonly Dictionary<int, OceanBattleRoom> OceanRooms = [];

    private OceanBattleRoom? _oceanBattle;
    private int _enteringRoom;

    public int Room { get; private set; }

    public long OceanBattleSafeUntil { get; private set; }

    public bool InOceanBattle => _oceanBattle != null;

    public bool StartOceanBattle(SeaNpcState foe, bool attacked = false)
    {
        if (!BboAtOcean || Mover == null || SeaSunk || _oceanBattle != null) return false;
        if (foe.Dead || foe.Gone || foe.Engaged || foe.Definition.Friendly || foe.Temporary) return false;
        if (attacked && OceanBattleSafeUntil > Environment.TickCount64) return false;
        (float x, float z) = Mover.Now();
        var room = new OceanBattleRoom(foe, Scene, new Vector3(x, 0f, z));
        OceanRooms[room.Id] = room;
        foe.Engaged = true;
        foe.Target = null;
        foreach (ClientSession sailor in Sea.Sailors(Scene)) sailor.Send(ScenePackets.RemoveObject(foe.Definition.Id), "(fleet engaged)");

        SeaNpcDefinition flag = foe.Definition;
        int escorts = OceanBattleRules.Escorts(flag);
        for (int i = 0; i <= escorts; i++)
        {
            float side = i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f) * 45f * ((i + 1) / 2);
            float back = i == 0 ? 0f : 35f * ((i + 1) / 2);
            SeaNpcDefinition ship = RoomShip(flag, i > 0, side, OceanBattleRules.StartGap / 2 + back);
            Sea.AddRoomShip(ship, room.Id);
        }

        List<ClientSession> fighters = WithNearbyMates();
        for (int i = 0; i < fighters.Count; i++)
        {
            float side = i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f) * 40f * ((i + 1) / 2);
            fighters[i].EnterOceanBattle(room, new Vector3(side, 0f, -OceanBattleRules.StartGap / 2), attacked);
        }
        foreach (SeaNpcState ship in Sea.InScene(OceanBattleRules.BattleField, room.Id))
            Sea.Provoke(ship, fighters[Random.Shared.Next(fighters.Count)]);
        Log.Info(Tag, $"{Name} {(attacked ? "is caught by" : "engages")} {flag.Name} on ocean {room.Ocean} ({escorts + 1} ships, {fighters.Count} player ship(s), room {room.Id})");
        return true;
    }

    // This ship and the team mates sailing close by on the same ocean.
    private List<ClientSession> WithNearbyMates()
    {
        var fighters = new List<ClientSession> { this };
        if (Team == null || Mover == null) return fighters;
        (float x, float z) = Mover.Now();
        foreach (ClientSession mate in Team.Members.Where(mate => mate != this && mate.BboAtOcean && mate.Scene == Scene && mate.Room == 0
                     && !mate.SeaSunk && mate._oceanBattle == null && mate.Mover != null))
        {
            (float mx, float mz) = mate.Mover!.Now();
            if (MathF.Sqrt((mx - x) * (mx - x) + (mz - z) * (mz - z)) <= OceanBattleRules.TeamJoinRange) fighters.Add(mate);
        }
        return fighters;
    }

    // A forced attack on another player's ship on the high sea: both sides (and their team mates close by) fight it out on a
    // battle field. The side left afloat wins; sailing away from every enemy ship escapes.
    public bool StartOceanDuel(ClientSession victim)
    {
        if (!BboAtOcean || Room != 0 || Mover == null || SeaSunk || _oceanBattle != null) return false;
        if (!victim.BboAtOcean || victim.Room != 0 || victim.Scene != Scene || victim.SeaSunk || victim._oceanBattle != null || victim.Mover == null) return false;
        (float x, float z) = Mover.Now();
        var room = new OceanBattleRoom(null, Scene, new Vector3(x, 0f, z));
        OceanRooms[room.Id] = room;
        List<ClientSession> attackers = WithNearbyMates();
        List<ClientSession> defenders = victim.WithNearbyMates().Where(mate => !attackers.Contains(mate)).ToList();
        foreach (ClientSession fighter in attackers) room.Sides[fighter] = 0;
        foreach (ClientSession fighter in defenders) room.Sides[fighter] = 1;
        for (int i = 0; i < attackers.Count; i++)
            attackers[i].EnterOceanBattle(room, new Vector3((i % 2 == 1 ? 1f : -1f) * 40f * ((i + 1) / 2), 0f, -OceanBattleRules.StartGap / 2), false);
        for (int i = 0; i < defenders.Count; i++)
            defenders[i].EnterOceanBattle(room, new Vector3((i % 2 == 1 ? 1f : -1f) * 40f * ((i + 1) / 2), 0f, OceanBattleRules.StartGap / 2), true);
        Log.Info(Tag, $"{Name} attacks {victim.Name} on ocean {room.Ocean}: {attackers.Count} against {defenders.Count} ship(s), room {room.Id}");
        return true;
    }

    private string OpponentsOf(OceanBattleRoom room)
    {
        if (room.Foe != null) return "the " + room.Foe.Definition.Name;
        int side = room.Sides.GetValueOrDefault(this);
        return string.Join(", ", room.Sides.Where(pair => pair.Value != side).Select(pair => pair.Key.Name));
    }

    private static SeaNpcDefinition RoomShip(SeaNpcDefinition flag, bool escort, float x, float z) => new()
    {
        Scene = OceanBattleRules.BattleField,
        Model = flag.Model,
        Name = flag.Name,
        Level = flag.Level,
        Hull = escort ? Math.Max(1, flag.Hull * OceanBattleRules.EscortHullPercent / 100) : flag.Hull,
        Sail = escort ? Math.Max(1, flag.Sail * OceanBattleRules.EscortHullPercent / 100) : flag.Sail,
        Sailors = escort ? Math.Max(1, flag.Sailors * OceanBattleRules.EscortHullPercent / 100) : flag.Sailors,
        Cannons = flag.Cannons,
        Cannon = flag.Cannon,
        Ammunition = flag.Ammunition,
        ArmourHull = flag.ArmourHull,
        ArmourSail = flag.ArmourSail,
        HitPercent = flag.HitPercent,
        Gold = escort ? flag.Gold * OceanBattleRules.EscortGoldPercent / 100 : flag.Gold,
        Reputation = escort ? flag.Reputation * OceanBattleRules.EscortGoldPercent / 100 : flag.Reputation,
        SailCloth = flag.SailCloth,
        X = x,
        Z = z,
        Heading = 1800,
        Roams = false,
        Boss = flag.Boss && !escort,
    };

    private void EnterOceanBattle(OceanBattleRoom room, Vector3 at, bool attacked)
    {
        _enteringRoom = room.Id;
        Send(ScenePackets.LeaveScene(), "(leave the ocean for battle)");
        EnterScene(OceanBattleRules.BattleField, at);
        _oceanBattle = room;
        room.Fighters.Add(this);
        SystemMessage(attacked
            ? $"{OpponentsOf(room)} attacked you! Sink every enemy ship to win, or sail away from them to escape."
            : $"You engaged {OpponentsOf(room)}! Sink every enemy ship to win, or sail away from them to escape.", SystemMessageKind.System);
        WatchOceanBattle(room);
    }

    // Called from EnterScene after the old scene was left: the new scene's room.
    private void TakeEnteringRoom()
    {
        Room = _enteringRoom;
        _enteringRoom = 0;
    }

    private void WatchOceanBattle(OceanBattleRoom room)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(1000);
                lock (Context.World.Sync)
                {
                    if (_oceanBattle != room || !InWorld || room.Over || Mover == null) return;
                    if (SeaSunk) continue;
                    (float x, float z) = Mover.Now();
                    List<(float X, float Z)> enemies = room.Foe != null
                        ? Sea.InScene(OceanBattleRules.BattleField, room.Id).Where(ship => !ship.Dead).Select(ship => (ship.X, ship.Z)).ToList()
                        : room.Sides.Where(pair => pair.Value != room.Sides.GetValueOrDefault(this) && pair.Key.Room == room.Id && !pair.Key.SeaSunk
                                && pair.Key.Mover != null).Select(pair => pair.Key.Mover!.Now()).ToList();
                    if (enemies.Count == 0) return;
                    float nearest = enemies.Min(ship => MathF.Sqrt((ship.X - x) * (ship.X - x) + (ship.Z - z) * (ship.Z - z)));
                    if (nearest >= OceanBattleRules.EscapeDistance || MathF.Sqrt(x * x + z * z) >= OceanBattleRules.FieldRadius)
                    {
                        SystemMessage($"You escaped from {OpponentsOf(room)}.", SystemMessageKind.System);
                        Log.Info(Tag, $"{Name} escapes from {OpponentsOf(room)} (room {room.Id})");
                        ReturnToOcean();
                        return;
                    }
                }
            }
        });
    }

    private void OceanBattleShipSunk(SeaNpcState ship)
    {
        if (_oceanBattle is not { } room || room.Foe == null || ship.Room != room.Id || room.Over) return;
        if (Sea.InScene(OceanBattleRules.BattleField, room.Id).Any(other => !other.Dead)) return;
        room.Over = true;
        room.Won = true;
        foreach (ClientSession fighter in room.Fighters.Where(fighter => fighter.InWorld && fighter.Room == room.Id).ToList())
        {
            fighter.SystemMessage($"Victory! The {room.FoeName} fleet is sunk. You return to the open sea in {OceanBattleRules.ReturnSeconds} seconds.",
                SystemMessageKind.System);
            fighter.Send(UiPackets.PlaySound("SE3009"), "(victory)");
        }
        Log.Info(Tag, $"{string.Join(", ", room.Fighters.Select(fighter => fighter.Name))} sank the {room.FoeName} fleet (room {room.Id})");
        Sea.Later(OceanBattleRules.ReturnSeconds, () =>
        {
            foreach (ClientSession fighter in room.Fighters.Where(fighter => fighter.InWorld && fighter.Room == room.Id).ToList()) fighter.ReturnToOcean();
        });
    }

    private void ReturnToOcean()
    {
        if (_oceanBattle is not { } room) return;
        Vector3 at = room.At;
        int ocean = room.Ocean;
        Send(ScenePackets.LeaveScene(), "(back to the ocean)");
        EnterScene(ocean, at);
        OceanBattleSafeUntil = Environment.TickCount64 + OceanBattleRules.SafeSeconds * 1000L;
    }

    // LeaveScene: the fighter leaves the battle room (back to the ocean, sunk and towed home, or logged off).
    private void LeaveOceanBattleRoom()
    {
        if (_oceanBattle is not { } room) return;
        _oceanBattle = null;
        room.Fighters.Remove(this);
        Room = 0;
        if (room.Foe == null) DuelSideLeft(room);
        if (room.Fighters.Count > 0) return;
        OceanRooms.Remove(room.Id);
        Sea.RemoveRoom(room.Id);
        if (room.Foe is not { } foe) return;
        if (room.Won)
        {
            foe.Engaged = false;
            foe.Gone = true;
            Sea.Sink(foe);
            return;
        }
        foe.Engaged = false;
        foe.Target = null;
        foe.DemandedFrom = null;
        foreach (ClientSession sailor in Sea.Sailors(room.Ocean)) sailor.SendSeaNpc(foe);
    }

    // A duel ends when one side has no ship left in the room: the other side wins and sails back to the ocean.
    private void DuelSideLeft(OceanBattleRoom room)
    {
        if (room.Over) return;
        int side = room.Sides.GetValueOrDefault(this);
        if (room.Fighters.Any(fighter => room.Sides.GetValueOrDefault(fighter) == side)) return;
        room.Over = true;
        List<ClientSession> winners = room.Fighters.Where(fighter => fighter.InWorld && fighter.Room == room.Id).ToList();
        foreach (ClientSession winner in winners)
        {
            winner.SystemMessage($"Victory! No enemy ship is left. You return to the open sea in {OceanBattleRules.ReturnSeconds} seconds.", SystemMessageKind.System);
            winner.Send(UiPackets.PlaySound("SE3009"), "(victory)");
            winner.GainReputation(20);
        }
        Log.Info(Tag, $"duel in room {room.Id} won by {string.Join(", ", winners.Select(winner => winner.Name))}");
        Sea.Later(OceanBattleRules.ReturnSeconds, () =>
        {
            foreach (ClientSession winner in room.Fighters.Where(fighter => fighter.InWorld && fighter.Room == room.Id).ToList()) winner.ReturnToOcean();
        });
    }

    // Close: a fighter who logs off in a battle comes back on the ocean where the battle started.
    private void OceanBattleLeaveWorld(OceanBattleRoom room)
    {
        Character.Scene = room.Ocean;
        Character.Position = [MathF.Round(room.At.X, 2), 0f, MathF.Round(room.At.Z, 2)];
    }
}
