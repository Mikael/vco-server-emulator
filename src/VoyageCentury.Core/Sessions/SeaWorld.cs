using System.Runtime.CompilerServices;
using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed class SeaNpcState
{
    public SeaNpcState(SeaNpcDefinition definition)
    {
        Definition = definition;
        Hull = definition.Hull;
    }

    public SeaNpcDefinition Definition { get; }

    public int Hull { get; set; }

    public bool Dead { get; set; }

    public bool Gone { get; set; }

    public ClientSession? Target { get; set; }

    public long AggroUntil { get; set; }

    public long NextShotAt { get; set; }

    public int RatioHull => Math.Clamp(100 * Hull / Math.Max(1, Definition.Hull), 0, 100);
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

    public IEnumerable<SeaNpcState> InScene(int scene) => _ships.Values.Where(ship => ship.Definition.Scene == scene);

    public SeaNpcState? Find(int objectId, int scene)
        => _ships.TryGetValue(objectId, out SeaNpcState? ship) && ship.Definition.Scene == scene ? ship : null;

    public bool IsSeaNpc(int objectId) => _ships.ContainsKey(objectId);

    public IEnumerable<ClientSession> Sailors(int scene)
        => _context.World.Sessions.Where(session => session.Scene == scene && session.InWorld && session.AtSea).ToList();

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
        foreach (ClientSession session in Sailors(definition.Scene)) session.Send(sinking, $"({definition.Name} sinks)");

        Later(Data.Rules.SinkSeconds, () =>
        {
            ship.Gone = true;
            foreach (ClientSession session in Sailors(definition.Scene))
                session.Send(ScenePackets.RemoveObject(definition.Id), $"({definition.Name} gone)");
        });
        Later(Math.Max(Data.Rules.SinkSeconds + 2, definition.RespawnSeconds), () =>
        {
            ship.Hull = definition.Hull;
            ship.Dead = false;
            ship.Gone = false;
            ship.Target = null;
            foreach (ClientSession session in Sailors(definition.Scene)) session.SendSeaNpc(ship);
            Log.Debug("Sea", $"{definition.Name} returned");
        });
    }

    private void Tick()
    {
        long now = Now;
        foreach (SeaNpcState ship in _ships.Values)
        {
            if (ship.Dead || ship.Target == null) continue;
            ClientSession target = ship.Target;
            if (now > ship.AggroUntil || !target.InWorld || !target.AtSea || target.Scene != ship.Definition.Scene || target.SeaSunk)
            {
                ship.Target = null;
                continue;
            }
            if (now < ship.NextShotAt) continue;
            ship.NextShotAt = now + (long)(Math.Max(1f, ship.Definition.ReloadSeconds) * 1000);
            target.TakeSeaVolley(ship);
        }
    }
}
