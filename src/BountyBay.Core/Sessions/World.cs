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
            _monsters[definition.Id] = state = new MonsterState { Hp = definition.Hp, X = definition.X, Z = definition.Z };
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
            await Task.Delay(TimeSpan.FromSeconds(_context.Settings.World.RespawnSeconds));
            lock (Sync)
            {
                MonsterState state = Monster(definition);
                state.Hp = definition.Hp;
                state.Dead = false;
                state.Fled = false;
                state.Target = null;
                state.X = definition.X;
                state.Z = definition.Z;
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
            if (state.Fled && state.Hp >= definition.Hp) Walk(definition, state, definition.X, definition.Z, fled: false);
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
                if (!definition.Aggressive && !_monsters.ContainsKey(definition.Id)) continue;
                MonsterState state = Monster(definition);
                if (state.Dead) continue;
                if (state.Target is { } held && (!players.Contains(held) || Apart(state, held) > rules.MonsterReach)) state.Target = null;
                if (state.Target == null && definition.Aggressive && !state.Fled)
                {
                    state.Target = players.Where(player => Apart(state, player) <= rules.AggroRadius).MinBy(player => Apart(state, player));
                    if (state.Target != null) state.NextAttackAt = now + rules.MonsterAttackMilliseconds / 2;
                }
                if (state.Target == null) continue;
                if (definition.Flees && !state.Fled && 100 * state.Hp < definition.Hp * rules.FleeHpPercent)
                {
                    (float x, float z) = state.Target.Mover!.Now();
                    float dx = state.X - x;
                    float dz = state.Z - z;
                    float length = MathF.Max(0.1f, MathF.Sqrt(dx * dx + dz * dz));
                    state.Target = null;
                    Walk(definition, state, state.X + dx / length * rules.FleeDistance, state.Z + dz / length * rules.FleeDistance, fled: true);
                    continue;
                }
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

    private void Walk(MonsterDefinition definition, MonsterState state, float x, float z, bool fled)
    {
        byte[] move = ScenePackets.PlaceObject(definition.Id, state.X, definition.Y, state.Z, x, z, _context.Data.Rules.Combat.FleeSpeed);
        foreach (ClientSession session in InScene(definition.Scene)) session.Send(move, fled ? "(monster flees)" : "(monster returns)");
        state.X = x;
        state.Z = z;
        state.Fled = fled;
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
