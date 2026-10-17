using BountyBay.Core.Configuration;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class MonsterState
{
    public int Hp { get; set; }
    public bool Dead { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public bool Fled { get; set; }
    public ClientSession? Target { get; set; }
    public long NextAttackAt { get; set; }
    public Dictionary<string, (int Percent, long Until)> Weakened { get; } = [];
    public float HomeX { get; set; }
    public float HomeZ { get; set; }
    public float FromX { get; set; }
    public float FromZ { get; set; }
    public float DestX { get; set; }
    public float DestZ { get; set; }
    public float Speed { get; set; }
    public long MoveStart { get; set; }
    public long NextRoamAt { get; set; }
    public bool Returning { get; set; }
    public int Heading { get; set; } = -1;

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

    public int WeakenedBy(string what)
        => Weakened.TryGetValue(what, out (int Percent, long Until) entry) && entry.Until > Environment.TickCount64 ? entry.Percent : 0;
}

public sealed class ServerContext
{
    public ServerContext(EmulatorSettings settings, GameData data, AccountRepository accounts)
    {
        Settings = settings;
        Data = data;
        Accounts = accounts;
        World = new World(this);
    }

    public EmulatorSettings Settings { get; }

    public GameData Data { get; }

    public AccountRepository Accounts { get; }

    public World World { get; }
}

public sealed class World
{
    private readonly ServerContext _context;
    private readonly List<ClientSession> _sessions = [];
    private readonly Dictionary<int, MonsterState> _monsters = [];
    private int _nextObjectId = ProtocolConstants.FirstPlayerObjectId;
    private long _tick;
    private int _hour = -1;

    public World(ServerContext context) => _context = context;

    public object Sync { get; } = new();

    public IReadOnlyList<ClientSession> Sessions => _sessions;

    public int NextObjectId() => Interlocked.Increment(ref _nextObjectId);

    public void Add(ClientSession session)
    {
        if (!_sessions.Contains(session)) _sessions.Add(session);
    }

    public void Remove(ClientSession session) => _sessions.Remove(session);

    public IEnumerable<ClientSession> InScene(int scene) => _sessions.Where(session => session.Scene == scene && session.InWorld).ToList();

    public MonsterState Monster(MonsterDefinition definition)
    {
        if (!_monsters.TryGetValue(definition.Id, out MonsterState? state))
            _monsters[definition.Id] = state = new MonsterState
            {
                Hp = definition.Hp, X = definition.X, Z = definition.Z, HomeX = definition.X, HomeZ = definition.Z,
                NextRoamAt = Environment.TickCount64 + Random.Shared.Next(2000, 15000),
            };
        return state;
    }

    public void Provoke(MonsterDefinition definition, ClientSession attacker)
    {
        MonsterState state = Monster(definition);
        if (state.Dead || state.Fled) return;
        long now = Environment.TickCount64;
        if (state.Target == null) state.NextAttackAt = now + _context.Data.Rules.Combat.MonsterAttackMilliseconds / 2;
        state.Target ??= attacker;
    }

    public GameTime Time => GameTime.Now(_context.Data.Rules.Clock);

    public void ScheduleRespawn(MonsterDefinition definition)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(definition.RespawnSeconds > 0 ? definition.RespawnSeconds : _context.Settings.World.RespawnSeconds));
            lock (Sync)
            {
                MonsterState state = Monster(definition);
                state.Hp = definition.Hp;
                state.Dead = false;
                state.Fled = false;
                state.Target = null;
                state.X = definition.X;
                state.Z = definition.Z;
                state.Speed = 0f;
                state.Returning = false;
                state.NextRoamAt = Environment.TickCount64 + Random.Shared.Next(2000, 15000);
                state.Weakened.Clear();
                foreach (ClientSession session in InScene(definition.Scene))
                    session.SendMonster(definition, "(monster returns)");
            }
            Log.Debug("World", $"{definition.Name} returned");
        });
    }

    public ClientSession? FindPlayer(string name)
        => _sessions.FirstOrDefault(session => session.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void StartTicking(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    lock (Sync) Tick();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    private void Tick()
    {
        _tick++;
        foreach (ClientSession session in _sessions.ToList())
        {
            try
            {
                session.TickSecond(_tick);
            }
            catch (Exception exception)
            {
                Log.Error(session.Tag, exception);
            }
        }
        TickClock();
        TickMonsters();
        GuildWarDirectory.Tick(_context);
        if (_tick % Math.Max(1, _context.Data.Rules.Character.RegenerationSeconds) != 0) return;
        foreach ((int id, MonsterState state) in _monsters)
        {
            MonsterDefinition definition = _context.Data.Monsters[id];
            if (state.Target is { } target && (!target.InWorld || target.Scene != definition.Scene)) state.Target = null;
            if (state.Dead || state.Target != null || state.Hp >= definition.Hp) continue;
            state.Hp = Math.Min(definition.Hp, state.Hp + Math.Max(1, definition.Hp / 10));
            byte[] update = ScenePackets.ObjectProperties(id, new PropertyBag
            {
                ["HP"] = state.Hp,
                ["RatioHP"] = 100 * state.Hp / Math.Max(1, definition.Hp),
            });
            foreach (ClientSession session in InScene(definition.Scene)) session.Send(update, "(monster recovers)");
            if (state.Fled && state.Hp >= definition.Hp)
            {
                state.Fled = false;
                Walk(definition, state, state.HomeX, state.HomeZ, _context.Data.Rules.Combat.MonsterWalkSpeed, "(monster returns)");
            }
        }
    }

    private void TickClock()
    {
        GameTime time = Time;
        if (time.Hour == _hour) return;
        bool first = _hour < 0;
        _hour = time.Hour;
        if (first) return;
        foreach (ClientSession session in _sessions.ToList()) session.SendSceneState(time);
    }

    private void TickMonsters()
    {
        CombatRuleSet rules = _context.Data.Rules.Combat;
        long now = Environment.TickCount64;
        foreach (IGrouping<int, ClientSession> scene in _sessions.Where(session => session.InWorld && !session.AtSea).GroupBy(session => session.Scene).ToList())
        {
            List<ClientSession> players = scene.Where(session => session.Hp > 0).ToList();
            foreach (MonsterDefinition definition in _context.Data.Monsters.Values.Where(monster => monster.Scene == scene.Key))
            {
                MonsterState state = Monster(definition);
                if (state.Dead) continue;
                state.Advance(now);
                if (state.Target is { } held && (!players.Contains(held) || FromHome(state, held) > rules.MonsterLeashRadius))
                {
                    state.Target = null;
                    ReturnHome(definition, state, rules);
                    continue;
                }
                if (state.Target == null && definition.Aggressive && !state.Fled && !state.Returning)
                {
                    state.Target = players.Where(player => Apart(state, player) <= rules.AggroRadius).MinBy(player => Apart(state, player));
                    if (state.Target != null) state.NextAttackAt = now + rules.MonsterAttackMilliseconds / 2;
                }
                if (state.Target == null)
                {
                    if (state.Returning && !state.Moving) state.Returning = false;
                    if (!state.Moving && !state.Fled && !state.Returning && now >= state.NextRoamAt) Roam(definition, state, rules, now);
                    continue;
                }
                if (definition.Flees && !state.Fled && 100 * state.Hp < definition.Hp * rules.FleeHpPercent)
                {
                    (float x, float z) = state.Target.Mover!.Now();
                    float dx = state.X - x;
                    float dz = state.Z - z;
                    float length = MathF.Max(0.1f, MathF.Sqrt(dx * dx + dz * dz));
                    state.Target = null;
                    Walk(definition, state, state.X + dx / length * rules.FleeDistance, state.Z + dz / length * rules.FleeDistance, rules.FleeSpeed, "(monster flees)");
                    state.Fled = true;
                    continue;
                }
                float apart = Apart(state, state.Target);
                if (apart > rules.MonsterAttackRange)
                {
                    (float tx, float tz) = state.Target.Mover!.Now();
                    float step = apart - rules.MonsterAttackRange * 0.8f;
                    float aimX = state.X + (tx - state.X) * step / apart;
                    float aimZ = state.Z + (tz - state.Z) * step / apart;
                    bool sameAim = state.Moving && MathF.Abs(state.DestX - aimX) + MathF.Abs(state.DestZ - aimZ) < rules.MonsterAttackRange;
                    if (!sameAim) Walk(definition, state, aimX, aimZ, rules.MonsterChaseSpeed, "(monster chases)");
                    continue;
                }
                int facing = Facing(state, state.Target);
                if (state.Moving || TurnAngle(state.Heading, facing) > 150) Stop(definition, state, facing);
                if (now < state.NextAttackAt) continue;
                state.NextAttackAt = now + Math.Max(500, rules.MonsterAttackMilliseconds);
                try
                {
                    state.Target.TakeMonsterHit(definition, state);
                }
                catch (Exception exception)
                {
                    Log.Error(state.Target?.Tag ?? "World", exception);
                }
            }
        }
    }

    private void Roam(MonsterDefinition definition, MonsterState state, CombatRuleSet rules, long now)
    {
        state.NextRoamAt = now + Random.Shared.Next(rules.MonsterRoamMinSeconds * 1000, rules.MonsterRoamMaxSeconds * 1000 + 1);
        float radius = definition.RoamRadius > 0f ? definition.RoamRadius : rules.MonsterRoamRadius;
        if (radius <= 0f) return;
        float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
        float distance = (float)Random.Shared.NextDouble() * radius;
        Walk(definition, state, state.HomeX + MathF.Cos(angle) * distance, state.HomeZ + MathF.Sin(angle) * distance,
            rules.MonsterWalkSpeed, "(monster roams)");
    }

    private void ReturnHome(MonsterDefinition definition, MonsterState state, CombatRuleSet rules)
    {
        state.Returning = true;
        state.Fled = false;
        Walk(definition, state, state.HomeX, state.HomeZ, rules.MonsterChaseSpeed, "(monster returns)");
    }

    private void Stop(MonsterDefinition definition, MonsterState state, int facing = 0)
    {
        state.Speed = 0f;
        state.Heading = facing;
        byte[] place = ScenePackets.PlaceObject(definition.Id, state.X, definition.Y, state.Z, orient: facing);
        foreach (ClientSession session in InScene(definition.Scene)) session.Send(place, "(monster stops)");
    }

    private void Walk(MonsterDefinition definition, MonsterState state, float x, float z, float speed, string note)
    {
        state.FromX = state.X;
        state.FromZ = state.Z;
        state.DestX = x;
        state.DestZ = z;
        state.Speed = speed;
        state.MoveStart = Environment.TickCount64;
        state.Heading = -1;
        byte[] move = ScenePackets.MoveObject(definition.Id, x, z, speed, 0);
        foreach (ClientSession session in InScene(definition.Scene)) session.Send(move, note);
    }

    private static int Facing(MonsterState state, ClientSession session)
    {
        if (session.Mover == null) return Math.Max(0, state.Heading);
        (float x, float z) = session.Mover.Now();
        float dx = x - state.X;
        float dz = z - state.Z;
        if (dx * dx + dz * dz < 0.0001f) return Math.Max(0, state.Heading);
        double tenths = Math.Atan2(dx, dz) * 1800.0 / Math.PI;
        return (int)Math.Round(tenths < 0 ? tenths + 3600.0 : tenths) % 3600;
    }

    private static int TurnAngle(int from, int to)
    {
        if (from < 0) return 1800;
        int difference = Math.Abs(from - to) % 3600;
        return difference > 1800 ? 3600 - difference : difference;
    }

    private static float FromHome(MonsterState state, ClientSession session)
    {
        if (session.Mover == null) return float.MaxValue;
        (float x, float z) = session.Mover.Now();
        return MathF.Sqrt((state.HomeX - x) * (state.HomeX - x) + (state.HomeZ - z) * (state.HomeZ - z));
    }

    private static float Apart(MonsterState state, ClientSession session)
    {
        if (session.Mover == null) return float.MaxValue;
        (float x, float z) = session.Mover.Now();
        return MathF.Sqrt((state.X - x) * (state.X - x) + (state.Z - z) * (state.Z - z));
    }

    public void Broadcast(byte[] body, string note)
    {
        lock (Sync)
        {
            foreach (ClientSession session in _sessions.ToList())
                session.Send(body, note);
        }
    }
}
