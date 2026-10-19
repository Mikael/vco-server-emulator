using System.Runtime.CompilerServices;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Storms and floating goods on the high sea.
// CLIENT: interface_main.ini [publicmap] draws object kind 124 (0x7c) as "OCEAN_STORM_VISIBLE" (storm warning) and kind 122
// (0x7a) as a floating object; the ocean scenes preload object\4000276 ("Goods") and the particle object 4000292; the
// world map shows storms from `OceanStormInfo <x> <z> <name>` keyed by the object id (CFullMap 0x1008f350, icon 124).
// DOceanNPC (kinds 0x7a/0x7c) adds 4,000,000 to the model id. GUESS: how many storms and goods there are, where, how fast
// storms drift, what they do to a ship and what the goods hold (the client keeps none of it).
public sealed class OceanHazard
{
    public int Id { get; init; }

    public int Ocean { get; init; }

    public bool Storm { get; init; }

    public float X { get; set; }

    public float Z { get; set; }

    public float DestX { get; set; }

    public float DestZ { get; set; }

    public long Until { get; set; }

    public bool Gone { get; set; }
}

public sealed class OceanWorld
{
    public const int FirstStormId = 8_400_000;
    public const int FirstGoodsId = 8_450_000;
    public const byte StormKind = 0x7c;
    public const byte GoodsKind = 0x7a;
    public const int StormModel = 292;
    public const int GoodsModel = 276;
    public const float StormRadius = 30f;
    public const float StormSpeed = 1.5f;
    public const int StormsPerOcean = 4;
    public const int GoodsPerOcean = 12;
    public const int StormLifeSeconds = 600;
    public const int GoodsRespawnSeconds = 120;
    public const float GoodsReach = 12f;
    public const int StormSailPercent = 3;
    public const int StormHullPercent = 1;
    public const string StormName = "Storm";

    private static readonly ConditionalWeakTable<ServerContext, OceanWorld> Worlds = new();
    private static readonly int[] Oceans = [1000001, 1000003];

    private readonly ServerContext _context;
    private readonly Dictionary<int, OceanHazard> _hazards = [];
    private bool _ticking;
    private long _tick;

    private OceanWorld(ServerContext context)
    {
        _context = context;
        int storm = FirstStormId, goods = FirstGoodsId;
        foreach (int ocean in Oceans)
        {
            for (int i = 0; i < StormsPerOcean; i++)
            {
                var hazard = new OceanHazard { Id = storm++, Ocean = ocean, Storm = true };
                Place(hazard);
                _hazards[hazard.Id] = hazard;
            }
            for (int i = 0; i < GoodsPerOcean; i++)
            {
                var hazard = new OceanHazard { Id = goods++, Ocean = ocean };
                Place(hazard);
                _hazards[hazard.Id] = hazard;
            }
        }
    }

    public static OceanWorld Of(ServerContext context) => Worlds.GetValue(context, key => new OceanWorld(key));

    public IEnumerable<OceanHazard> InOcean(int ocean) => _hazards.Values.Where(hazard => hazard.Ocean == ocean && !hazard.Gone);

    public OceanHazard? Find(int id) => _hazards.GetValueOrDefault(id);

    // Somewhere out at sea: next to a random ocean fleet's station (the fleets only sit on open water).
    private void Place(OceanHazard hazard)
    {
        List<SeaNpcState> anchors = SeaWorld.Of(_context).InScene(hazard.Ocean).ToList();
        (float x, float z) = anchors.Count == 0 ? (0f, 0f) : (anchors[Random.Shared.Next(anchors.Count)].Definition.X, anchors[Random.Shared.Next(anchors.Count)].Definition.Z);
        float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
        float distance = 20f + (float)Random.Shared.NextDouble() * 60f;
        hazard.X = x + MathF.Cos(angle) * distance;
        hazard.Z = z + MathF.Sin(angle) * distance;
        hazard.DestX = hazard.X;
        hazard.DestZ = hazard.Z;
        hazard.Until = Environment.TickCount64 + StormLifeSeconds * 1000L;
        hazard.Gone = false;
    }

    public void EnsureTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
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
                        Log.Info("Ocean", $"ocean tick failed: {exception.Message}");
                    }
                }
            }
        });
    }

    private IEnumerable<ClientSession> Sailors(int ocean)
        => _context.World.Sessions.Where(session => session.InWorld && session.AtSea && session.Scene == ocean && session.Room == 0).ToList();

    private void Tick()
    {
        long now = Environment.TickCount64;
        _tick++;
        foreach (OceanHazard storm in _hazards.Values.Where(hazard => hazard.Storm).ToList())
        {
            List<ClientSession> sailors = Sailors(storm.Ocean).ToList();
            if (now > storm.Until)
            {
                foreach (ClientSession sailor in sailors) sailor.Send(ScenePackets.RemoveObject(storm.Id), "(storm passes)");
                Place(storm);
                foreach (ClientSession sailor in sailors) sailor.SendOceanHazard(storm);
                continue;
            }
            float dx = storm.DestX - storm.X, dz = storm.DestZ - storm.Z;
            float left = MathF.Sqrt(dx * dx + dz * dz);
            if (left < 1f)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                storm.DestX = storm.X + MathF.Cos(angle) * 60f;
                storm.DestZ = storm.Z + MathF.Sin(angle) * 60f;
                int heading = ((int)Math.Round(Math.Atan2(storm.DestX - storm.X, storm.DestZ - storm.Z) * 1800.0 / Math.PI) % 3600 + 3600) % 3600;
                foreach (ClientSession sailor in sailors)
                    sailor.Send(ScenePackets.MoveObject(storm.Id, storm.DestX, storm.DestZ, StormSpeed, heading), "(storm drifts)");
            }
            else
            {
                float step = MathF.Min(left, StormSpeed * 2f);
                storm.X += dx / left * step;
                storm.Z += dz / left * step;
            }
            foreach (ClientSession sailor in sailors) sailor.WeatherStorm(storm);
        }
    }

    public void TakeGoods(OceanHazard goods)
    {
        goods.Gone = true;
        foreach (ClientSession sailor in Sailors(goods.Ocean)) sailor.Send(ScenePackets.RemoveObject(goods.Id), "(goods picked up)");
        SeaWorld.Of(_context).Later(GoodsRespawnSeconds, () =>
        {
            Place(goods);
            foreach (ClientSession sailor in Sailors(goods.Ocean)) sailor.SendOceanHazard(goods);
        });
    }
}
