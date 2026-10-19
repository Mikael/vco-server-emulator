using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// The world map on the high sea: explored-area fog, team mates, storms and floating goods.
// CLIENT (wn_3dwnd):
// - Fog: the map keeps a 64x64 grid per ocean in table "VisitedOcean" (rows ocean*64 + row, column 0 = bits of grid columns
//   0-31, column 1 = columns 32-63; the client wants at least 128 rows, 0x101ae230). When the own ship enters a cell whose
//   bit is clear it sends a bare "SetVisitedSea" (0x101ae710); the server sets the bit. Cell = trunc(map(x) / 2048 * 64)
//   with map(x) = (1 - (x - centerx) / width) * 2048 and map(z) = (z - centerz) / height * 2048 (0x100450e0 / 0x10045120,
//   0x101b48b4), centerx/centerz/width/height/mapindex from ini\window\map\<ocean>.ini.
// - Map keywords (UILogic full map dialog registers them, forwards to CFullMap 0x1008f350): `Pos <x> <y> <z> <dead> <name>`
//   (team mate icon 2000, 2002 when dead), `OceanStormInfo <x> <z> <name>` keyed by the object id (icon 124).
public sealed partial class ClientSession
{
    public static readonly TableDefinition VisitedOceanTable = new(100, "VisitedOcean", [new("Low", PropertyType.U32), new("High", PropertyType.U32)]);

    private const int OceanGrid = 64;
    private const int OceanMaps = 2;

    private static readonly Dictionary<int, (int Index, float X, float Z, float Width, float Height)> OceanMapFrames = new()
    {
        [1000001] = (0, -1316.694580f, -1367.081604f, 3425.852051f, 3425.852051f),
        [1000003] = (1, -4542.333008f, -1734.142212f, 4198.679688f, 4198.679688f),
    };

    private long _stormNoticeAt;

    private OceanWorld Ocean => OceanWorld.Of(Context);

    private List<uint> VisitedOcean
    {
        get
        {
            List<uint> cells = Character.VisitedOcean;
            while (cells.Count < OceanMaps * OceanGrid * 2) cells.Add(0);
            return cells;
        }
    }

    public void SendVisitedOcean()
    {
        List<uint> cells = VisitedOcean;
        var rows = new List<object[]>();
        for (int row = 0; row < OceanMaps * OceanGrid; row++) rows.Add([cells[row * 2], cells[row * 2 + 1]]);
        Send(TablePackets.Rows(VisitedOceanTable, rows), "(explored sea)");
    }

    public void SetVisitedSea()
    {
        if (!BboAtOcean || Room != 0 || Mover == null || !OceanMapFrames.TryGetValue(Scene, out var frame)) return;
        (float x, float z) = Mover.Now();
        int column = (int)((1f - (x - frame.X) / frame.Width) * OceanGrid);
        int row = (int)((z - frame.Z) / frame.Height * OceanGrid);
        if (column < 0 || column >= OceanGrid || row < 0 || row >= OceanGrid) return;
        int tableRow = frame.Index * OceanGrid + row;
        int word = column >= 32 ? 1 : 0;
        List<uint> cells = VisitedOcean;
        uint value = cells[tableRow * 2 + word] | (1u << (column & 31));
        if (value == cells[tableRow * 2 + word]) return;
        cells[tableRow * 2 + word] = value;
        Send(TablePackets.UpdateCell(VisitedOceanTable, tableRow, word, value), "(sea explored)");
    }

    // AskTeamPos (the map opens): the other team mates in this scene.
    public void SendTeamPositions()
    {
        if (Team == null) return;
        foreach (ClientSession mate in Team.Members.Where(mate => mate != this && mate.InWorld && mate.Scene == Scene && mate.Mover != null))
        {
            (float x, float z) = mate.Mover!.Now();
            int dead = mate.Hp <= 0 || mate.SeaSunk ? 1 : 0;
            Send(UiPackets.Custom(FormattableString.Invariant($"Pos {x:F1} {mate.Height:F1} {z:F1} {dead} {mate.Name}")), "(team mate on the map)");
        }
    }

    // OpenMapOnOcean: the storms of this ocean, plus the team mates.
    public void SendOceanMapMarks()
    {
        if (!BboAtOcean || Room != 0) return;
        foreach (OceanHazard storm in Ocean.InOcean(Scene).Where(hazard => hazard.Storm))
            Send(UiPackets.Custom(FormattableString.Invariant($"OceanStormInfo {storm.X:F1} {storm.Z:F1} {OceanWorld.StormName}"), storm.Id), "(storm on the map)");
        SendTeamPositions();
    }

    private void SendOceanHazards()
    {
        if (!BboAtOcean || Room != 0) return;
        Ocean.EnsureTicking();
        foreach (OceanHazard hazard in Ocean.InOcean(Scene)) SendOceanHazard(hazard);
    }

    public void SendOceanHazard(OceanHazard hazard)
    {
        string note = hazard.Storm ? "(storm)" : "(floating goods)";
        Send(ScenePackets.CreateObject(hazard.Id, (ObjectKind)(hazard.Storm ? OceanWorld.StormKind : OceanWorld.GoodsKind),
            hazard.Storm ? OceanWorld.StormModel : OceanWorld.GoodsModel), note);
        Send(ScenePackets.ObjectProperties(hazard.Id, SeaPackets.Known(new PropertyBag
        {
            ["Name"] = hazard.Storm ? OceanWorld.StormName : "Floating Goods",
            ["Model"] = hazard.Storm ? OceanWorld.StormModel : OceanWorld.GoodsModel,
            ["CollideRadius"] = hazard.Storm ? OceanWorld.StormRadius : 1.5f,
        })), note);
        bool drifting = hazard.Storm && MathF.Abs(hazard.DestX - hazard.X) + MathF.Abs(hazard.DestZ - hazard.Z) > 1f;
        Send(ScenePackets.PlaceObject(hazard.Id, hazard.X, 0f, hazard.Z, drifting ? hazard.DestX : null, drifting ? hazard.DestZ : null,
            drifting ? OceanWorld.StormSpeed : 0f), note);
        Send(ScenePackets.ShowObject(hazard.Id), note);
    }

    // Each storm tick: a ship inside the storm loses sail and hull (never below 1 hull).
    public void WeatherStorm(OceanHazard storm)
    {
        if (Mover == null || SeaSunk || _oceanBattle != null) return;
        (float x, float z) = Mover.Now();
        if ((x - storm.X) * (x - storm.X) + (z - storm.Z) * (z - storm.Z) > OceanWorld.StormRadius * OceanWorld.StormRadius) return;
        SetShipSail(Math.Max(0, ShipSail - Math.Max(1, FleetSailMax * OceanWorld.StormSailPercent / 100)));
        SetShipHull(Math.Max(1, ShipHull - Math.Max(1, ShipHullMax * OceanWorld.StormHullPercent / 100)));
        long now = Environment.TickCount64;
        if (now - _stormNoticeAt < 8000) return;
        _stormNoticeAt = now;
        SystemMessage("Your fleet is caught in a storm! The sails are tearing and the hull takes a beating. Sail out of it!", SystemMessageKind.Prompt);
    }

    public bool OnOceanHazardClicked(int objectId)
    {
        if (!BboAtOcean || Mover == null || Ocean.Find(objectId) is not { } hazard || hazard.Ocean != Scene || hazard.Gone) return false;
        if (hazard.Storm)
        {
            SystemMessage("A storm. Ships caught in it lose sail and hull; better sail around it.", SystemMessageKind.Prompt);
            return true;
        }
        (float x, float z) = Mover.Now();
        if ((x - hazard.X) * (x - hazard.X) + (z - hazard.Z) * (z - hazard.Z) > OceanWorld.GoodsReach * OceanWorld.GoodsReach)
        {
            Send(BboSeaPackets.GoTo(ProtocolConstants.SelfObjectId, hazard.X, hazard.Z), "(sail to the goods)");
            SystemMessage("Sail closer to fish the goods out of the water.", SystemMessageKind.Prompt);
            return true;
        }
        Ocean.TakeGoods(hazard);
        int roll = Random.Shared.Next(100);
        if (roll < 60)
        {
            int silver = 100 + Random.Shared.Next(400);
            SetMoney(Money + silver);
            SystemMessage($"You fished a drifting chest out of the sea: {silver} silver coins.", SystemMessageKind.Gain);
        }
        else
        {
            AmmunitionDefinition ammunition = Sea.Data.Ammunition.Count > 0 ? Sea.Data.Ammunition[Random.Shared.Next(Math.Min(3, Sea.Data.Ammunition.Count))] : Sea.Data.DefaultAmmunition;
            int balls = 20 + Random.Shared.Next(31);
            AddAmmunition(ammunition.Name, balls);
            SystemMessage($"You fished drifting crates out of the sea: {balls} rounds of {ItemLabel(ammunition.Name)}.", SystemMessageKind.Gain);
        }
        Save();
        Log.Info(Tag, $"{Name} picks up floating goods {hazard.Id} on ocean {Scene}");
        return true;
    }
}
