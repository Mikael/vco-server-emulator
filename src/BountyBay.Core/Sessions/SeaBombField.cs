using System.Runtime.CompilerServices;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class SeaBomb
{
    public ClientSession? Owner { get; init; }
    public string OwnerName { get; init; } = "";
    public int Scene { get; init; }
    public float X { get; init; }
    public float Z { get; init; }
    public string Item { get; init; } = "";
    public string Label { get; init; } = "";
    public int Damage { get; init; }
    public long ExpiresAt { get; init; }
}

public sealed class SeaBombField
{
    public const float TriggerRadius = 12f;
    public const int LifetimeSeconds = 180;
    public const double TickSeconds = 1.0;

    private static readonly ConditionalWeakTable<ServerContext, SeaBombField> Fields = new();

    private readonly ServerContext _context;
    private readonly List<SeaBomb> _bombs = [];
    private bool _ticking;

    private SeaBombField(ServerContext context) => _context = context;

    public static SeaBombField Of(ServerContext context) => Fields.GetValue(context, key => new SeaBombField(key));

    public IReadOnlyList<SeaBomb> Bombs => _bombs;

    public void Lay(SeaBomb bomb)
    {
        _bombs.Add(bomb);
        if (_ticking) return;
        _ticking = true;
        SeaWorld.Of(_context).Later(TickSeconds, Tick);
    }

    private void Tick()
    {
        long now = SeaWorld.Now;
        SeaWorld sea = SeaWorld.Of(_context);
        foreach (SeaBomb bomb in _bombs.ToList())
        {
            if (now >= bomb.ExpiresAt)
            {
                _bombs.Remove(bomb);
                continue;
            }
            SeaNpcState? victim = sea.InScene(bomb.Scene)
                .Where(ship => !ship.Dead && !ship.Gone)
                .FirstOrDefault(ship => (ship.X - bomb.X) * (ship.X - bomb.X) + (ship.Z - bomb.Z) * (ship.Z - bomb.Z) <= TriggerRadius * TriggerRadius);
            if (victim == null) continue;
            _bombs.Remove(bomb);
            Explode(sea, bomb, victim);
        }
        if (_bombs.Count == 0)
        {
            _ticking = false;
            return;
        }
        sea.Later(TickSeconds, Tick);
    }

    private static void Explode(SeaWorld sea, SeaBomb bomb, SeaNpcState ship)
    {
        SeaNpcDefinition definition = ship.Definition;
        int damage = Math.Max(1, bomb.Damage - definition.ArmourHull);
        byte[] blast = SeaBombPackets.Explosion(definition.Id, bomb.X, bomb.Z, $"{damage}");
        foreach (ClientSession session in sea.Sailors(bomb.Scene)) session.Send(blast, $"({bomb.Label} explodes under {definition.Name})");
        ship.Hull -= damage;
        Log.Info("Sea", $"{bomb.OwnerName}'s {bomb.Item} hits {definition.Name} for {damage}");
        ClientSession? owner = bomb.Owner is { InWorld: true } bomber && bomber.Scene == bomb.Scene ? bomber : null;
        if (ship.Hull > 0)
        {
            byte[] state = ScenePackets.ObjectProperties(definition.Id, SeaPackets.Known(ship.Bars()));
            foreach (ClientSession session in sea.Sailors(bomb.Scene)) session.Send(state, $"({definition.Name} damage)");
            if (owner != null) sea.Provoke(ship, owner);
            return;
        }
        sea.Sink(ship);
        owner?.SeaBombSank(definition);
    }
}
