using System.Runtime.CompilerServices;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class SeaNpcState
{
    public SeaNpcState(SeaNpcDefinition definition)
    {
        Definition = definition;
        Hull = definition.Hull;
        Sail = definition.Sail;
        Sailors = definition.Sailors;
        X = definition.X;
        Z = definition.Z;
        Heading = definition.Heading;
        NextPatrolAt = Environment.TickCount64 + Random.Shared.Next(5000, 30000);
    }

    public float X { get; set; }

    public float Z { get; set; }

    public int Heading { get; set; }

    public float FromX { get; set; }

    public float FromZ { get; set; }

    public float DestX { get; set; }

    public float DestZ { get; set; }

    public float Speed { get; set; }

    public long MoveStart { get; set; }

    public long NextPatrolAt { get; set; }

    public bool Moving => Speed > 0f;

    public void Advance(long now)
    {
        if (Speed <= 0f) return;
        float dx = DestX - FromX;
        float dz = DestZ - FromZ;
        float total = MathF.Sqrt(dx * dx + dz * dz);
        float done = Speed * (now - MoveStart) / 1000f;
        if (total < 0.01f || done >= total)
        {
            X = DestX;
            Z = DestZ;
            Speed = 0f;
            return;
        }
        X = FromX + dx * done / total;
        Z = FromZ + dz * done / total;
    }

    public void ResetPosition()
    {
        X = Definition.X;
        Z = Definition.Z;
        Heading = Definition.Heading;
        Speed = 0f;
    }

    public SeaNpcDefinition Definition { get; }

    // An ocean battle room (0 = the shared scene). Room ships exist only for that battle and never respawn.
    public int Room { get; init; }

    public bool Temporary => Room != 0;

    // An ocean fleet that is fighting in a battle room is hidden on the ocean until the battle ends.
    public bool Engaged { get; set; }

    // The player this ocean pirate already demanded silver from (pay, or refuse and fight).
    public ClientSession? DemandedFrom { get; set; }

    public int Hull { get; set; }

    public int Sail { get; set; }

    public int Sailors { get; set; }

    public bool Dead { get; set; }

    public bool Gone { get; set; }

    public ClientSession? Target { get; set; }

    public long AggroUntil { get; set; }

    public long NextShotAt { get; set; }

    public int RatioHull => Math.Clamp(100 * Hull / Math.Max(1, Definition.Hull), 0, 100);

    public int RatioSail => Math.Clamp(100 * Sail / Math.Max(1, Definition.Sail), 0, 100);

    public int RatioSailors => Math.Clamp(100 * Sailors / Math.Max(1, Definition.Sailors), 0, 100);

    public PropertyBag Bars() => new()
    {
        ["RatioShipHP"] = RatioHull,
        ["RatioHP"] = RatioHull,
        ["RatioSailHP"] = RatioSail,
        ["RatioSailorHP"] = RatioSailors,
        ["S/HP"] = Math.Max(0, Hull),
        ["S/MaxHP"] = Definition.Hull,
        ["S/SailHP"] = Math.Max(0, Sail),
        ["S/MaxSailHP"] = Definition.Sail,
        ["NumSailor"] = Math.Max(0, Sailors),
        ["NumSailorMax"] = Definition.Sailors,
    };
}

public sealed class SeaWorld
{
    private static readonly ConditionalWeakTable<ServerContext, SeaWorld> Worlds = new();

    private readonly ServerContext _context;
    private readonly Dictionary<int, SeaNpcState> _ships = [];
    private bool _ticking;

    private SeaWorld(ServerContext context)
    {
        _context = context;
        Data = SeaNpcs.Load(context.Settings);
        foreach (SeaNpcDefinition definition in Data.Ships) _ships[definition.Id] = new SeaNpcState(definition);
    }

    public SeaData Data { get; }

    public static SeaWorld Of(ServerContext context) => Worlds.GetValue(context, key => new SeaWorld(key));

    public static long Now => Environment.TickCount64;

    public IEnumerable<SeaNpcState> InScene(int scene, int room = 0)
        => _ships.Values.Where(ship => ship.Definition.Scene == scene && ship.Room == room).ToList();

    public SeaNpcState? Find(int objectId, int scene, int room = 0)
        => _ships.TryGetValue(objectId, out SeaNpcState? ship) && ship.Definition.Scene == scene && ship.Room == room ? ship : null;

    public SeaNpcState? Find(int objectId) => _ships.GetValueOrDefault(objectId);

    private int _nextRoomShip = OceanBattleRules.FirstShipId;

    public SeaNpcState AddRoomShip(SeaNpcDefinition definition, int room)
    {
        definition.Id = _nextRoomShip++;
        if (_nextRoomShip >= OceanBattleRules.FirstShipId + 1_000_000) _nextRoomShip = OceanBattleRules.FirstShipId;
        var ship = new SeaNpcState(definition) { Room = room };
        _ships[definition.Id] = ship;
        return ship;
    }

    public void RemoveRoom(int room)
    {
        foreach (int id in _ships.Values.Where(ship => ship.Room == room).Select(ship => ship.Definition.Id).ToList()) _ships.Remove(id);
    }

    public CannonDefinition CannonOf(SeaNpcDefinition definition) => Data.FindCannon(definition.Cannon) ?? Data.DefaultCannon;

    public bool IsSeaNpc(int objectId) => _ships.ContainsKey(objectId);

    public IEnumerable<ClientSession> Sailors(int scene, int room = 0)
        => _context.World.Sessions.Where(session => session.Scene == scene && session.Room == room && session.InWorld && session.AtSea).ToList();

    public IEnumerable<ClientSession> Sailors(SeaNpcState ship) => Sailors(ship.Definition.Scene, ship.Room);

    public void Later(double seconds, Action action)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.05, seconds)));
            lock (_context.World.Sync)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Log.Info("Sea", $"delayed sea action failed: {exception.Message}");
                }
            }
        });
    }

    public void EnsureTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync())
            {
                lock (_context.World.Sync)
                {
                    try
                    {
                        Tick();
                    }
                    catch (Exception exception)
                    {
                        Log.Info("Sea", $"sea tick failed: {exception.Message}");
                    }
                }
            }
        });
    }

    public void Provoke(SeaNpcState ship, ClientSession attacker)
    {
        if (ship.Definition.Friendly) return;
        long now = Now;
        if (ship.Target == null || ship.AggroUntil < now) ship.NextShotAt = now + 1500;
        ship.Target = attacker;
        ship.AggroUntil = now + Data.Rules.AggroSeconds * 1000L;
    }

    public void Forget(ClientSession session)
    {
        foreach (SeaNpcState ship in _ships.Values)
        {
            if (ship.Target == session) ship.Target = null;
        }
    }

    public void Sink(SeaNpcState ship)
    {
        if (ship.Dead) return;
        ship.Dead = true;
        ship.Hull = 0;
        ship.Target = null;
        SeaNpcDefinition definition = ship.Definition;
        byte[] sinking = ScenePackets.ObjectProperties(definition.Id, SeaPackets.Known(new PropertyBag
        {
            ["Dead"] = 1,
            ["RatioShipHP"] = 0,
            ["RatioHP"] = 0,
        }));
        foreach (ClientSession session in Sailors(ship)) session.Send(sinking, $"({definition.Name} sinks)");

        Later(Data.Rules.SinkSeconds, () =>
        {
            ship.Gone = true;
            foreach (ClientSession session in Sailors(ship))
                session.Send(ScenePackets.RemoveObject(definition.Id), $"({definition.Name} gone)");
        });
        if (ship.Temporary) return;
        Later(Math.Max(Data.Rules.SinkSeconds + 2, definition.RespawnSeconds), () =>
        {
            ship.Hull = definition.Hull;
            ship.Sail = definition.Sail;
            ship.Sailors = definition.Sailors;
            ship.Dead = false;
            ship.Gone = false;
            ship.Target = null;
            ship.Engaged = false;
            ship.ResetPosition();
            foreach (ClientSession session in Sailors(definition.Scene)) session.SendSeaNpc(ship);
            Log.Debug("Sea", $"{definition.Name} returned");
        });
    }

    private void Tick()
    {
        long now = Now;
        ShipRules rules = Data.Rules;
        HashSet<(int, int)> occupied = _context.World.Sessions.Where(session => session.InWorld && session.AtSea).Select(session => (session.Scene, session.Room)).ToHashSet();
        foreach (SeaNpcState ship in _ships.Values.ToList())
        {
            if (ship.Dead || ship.Engaged || !occupied.Contains((ship.Definition.Scene, ship.Room))) continue;
            ship.Advance(now);
            ClientSession? target = ship.Target;
            if (target == null && OceanBattleRules.Hunts(ship)) target = ship.Target = HuntOnOcean(ship, now);
            if (target != null && (now > ship.AggroUntil || !target.InWorld || !target.AtSea || target.Scene != ship.Definition.Scene || target.Room != ship.Room || target.SeaSunk))
            {
                ship.Target = null;
                target = null;
                if (ship.Definition.Roams) ship.NextPatrolAt = now;
                else Steer(ship, ship.Definition.X, ship.Definition.Z, rules.NpcPatrolSpeed, "(ship returns to station)");
            }
            if (target == null)
            {
                if (!ship.Moving && now >= ship.NextPatrolAt) Patrol(ship, rules, now);
                continue;
            }
            (float tx, float tz) = target.Mover!.Now();
            float dx = tx - ship.X;
            float dz = tz - ship.Z;
            float apart = MathF.Sqrt(dx * dx + dz * dz);
            float range = CannonOf(ship.Definition).Range;
            float home = MathF.Sqrt((tx - ship.Definition.X) * (tx - ship.Definition.X) + (tz - ship.Definition.Z) * (tz - ship.Definition.Z));
            if (home > (ship.Definition.Roams ? MathF.Max(rules.NpcLeashDistance, ship.Definition.RoamRadius * 1.5f) : rules.NpcLeashDistance))
            {
                ship.Target = null;
                if (ship.Definition.Roams) Patrol(ship, rules, now);
                else Steer(ship, ship.Definition.X, ship.Definition.Z, rules.NpcPatrolSpeed, "(ship breaks off)");
                continue;
            }
            if (apart > range * 0.9f)
            {
                float stop = apart - range * 0.6f;
                float aimX = ship.X + dx * stop / apart;
                float aimZ = ship.Z + dz * stop / apart;
                bool sameAim = ship.Moving && MathF.Abs(ship.DestX - aimX) + MathF.Abs(ship.DestZ - aimZ) < range * 0.2f;
                if (!sameAim) Steer(ship, aimX, aimZ, rules.NpcChaseSpeed, "(ship closes in)");
                continue;
            }
            // The high sea has no broadsides (DOceanRole/DOceanNPC have no 3Dfd): a fleet that reaches you there starts a battle.
            if (BountyBay.Core.Sessions.ClientSession.BboOceanScene(ship.Definition.Scene))
            {
                if (target.RobberyPending) continue;
                if (ship.DemandedFrom != target)
                {
                    ship.DemandedFrom = target;
                    if (target.PirateDemand(ship)) continue;
                }
                target.StartOceanBattle(ship, attacked: true);
                continue;
            }
            if (now < ship.NextShotAt) continue;
            ship.NextShotAt = now + (long)(Math.Max(1f, CannonOf(ship.Definition).ReloadSeconds) * 1000);
            target.TakeSeaVolley(ship);
        }
    }

    private void Patrol(SeaNpcState ship, ShipRules rules, long now)
    {
        ship.NextPatrolAt = now + Random.Shared.Next(rules.NpcPatrolMinSeconds * 1000, rules.NpcPatrolMaxSeconds * 1000 + 1);
        float radius = ship.Definition.Roams ? ship.Definition.RoamRadius : rules.NpcPatrolRadius;
        if (radius <= 0f) return;
        if (ship.Definition.Roams && ScenePaths.RandomNear(_context.Settings, ship.Definition.Scene, ship.Definition.X, ship.Definition.Z, radius * 0.3f, radius) is { } waypoint)
        {
            Steer(ship, waypoint.X, waypoint.Z, rules.NpcPatrolSpeed, "(ship roams)");
            return;
        }
        float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
        float distance = ship.Definition.Roams ? radius * (0.4f + 0.6f * (float)Random.Shared.NextDouble()) : (float)Random.Shared.NextDouble() * radius;
        Steer(ship, ship.Definition.X + MathF.Cos(angle) * distance, ship.Definition.Z + MathF.Sin(angle) * distance, rules.NpcPatrolSpeed, "(ship patrols)");
    }

    private void Steer(SeaNpcState ship, float x, float z, float speed, string note)
    {
        float dx = x - ship.X;
        float dz = z - ship.Z;
        if (dx * dx + dz * dz < 1f) return;
        int heading = ((int)Math.Round(Math.Atan2(dx, dz) * 1800.0 / Math.PI) % 3600 + 3600) % 3600;
        ship.FromX = ship.X;
        ship.FromZ = ship.Z;
        ship.DestX = x;
        ship.DestZ = z;
        ship.Speed = speed;
        ship.MoveStart = Environment.TickCount64;
        ship.Heading = heading;
        byte[] move = ScenePackets.MoveObject(ship.Definition.Id, x, z, speed, heading);
        foreach (ClientSession session in Sailors(ship)) session.Send(move, note);
    }

    // FirstAttack fleets on the ocean go after the nearest ship that comes within OceanBattleRules.HuntRange.
    private ClientSession? HuntOnOcean(SeaNpcState ship, long now)
    {
        ClientSession? prey = Sailors(ship)
            .Where(session => !session.SeaSunk && session.Mover != null && session.OceanBattleSafeUntil <= now)
            .Select(session => (session, distance: Distance(session, ship)))
            .Where(pair => pair.distance <= OceanBattleRules.HuntRange)
            .OrderBy(pair => pair.distance)
            .Select(pair => pair.session)
            .FirstOrDefault();
        if (prey != null) ship.AggroUntil = now + Data.Rules.AggroSeconds * 1000L;
        return prey;
    }

    private static float Distance(ClientSession session, SeaNpcState ship)
    {
        (float x, float z) = session.Mover!.Now();
        return MathF.Sqrt((x - ship.X) * (x - ship.X) + (z - ship.Z) * (z - ship.Z));
    }
}
