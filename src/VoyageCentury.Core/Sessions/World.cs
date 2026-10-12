using VoyageCentury.Core.Configuration;
using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;

namespace VoyageCentury.Core.Sessions;

public sealed class MonsterState
{
    public int Hp { get; set; }
    public bool Dead { get; set; }
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
            _monsters[definition.Id] = state = new MonsterState { Hp = definition.Hp };
        return state;
    }

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
                foreach (ClientSession session in InScene(definition.Scene))
                    session.SendMonster(definition, "(monster returns)");
            }
            Log.Debug("World", $"{definition.Name} returned");
        });
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
