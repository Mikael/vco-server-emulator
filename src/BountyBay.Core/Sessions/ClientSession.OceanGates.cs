using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed record BboOceanGate(int Id, int Ocean, string Name, float X, float Z, int TargetOcean, float TargetX, float TargetZ);

public sealed partial class ClientSession
{
    // 1000001 is the Atlantic/Mediterranean, 1000003 the Indian Ocean, Asia and the Pacific. The client marks the crossings
    // on 1000001 as "Afro-Asian Outer Coast" (map2/1000001.ini); the 1000003 ends are GUESS (Cape at the same spot, Magellan
    // and Iceland wrapped round the Pacific). Every position below was checked in the client to be open water.
    private static readonly BboOceanGate[] OceanGates =
    [
        new(8_390_001, 1000001, "Afro-Asian Outer Coast (Cape of Good Hope)", -219.3f, 1042.1f, 1000003, -359.1f, 941.0f),
        new(8_390_002, 1000003, "Atlantic Outer Coast (Cape of Good Hope)", -359.1f, 941.0f, 1000001, -219.3f, 1042.1f),
        new(8_390_003, 1000001, "Afro-Asian Outer Coast (Magellan Strait)", 1265.5f, 1309.9f, 1000003, -4436.7f, 1405.8f),
        new(8_390_004, 1000003, "Atlantic Outer Coast (Magellan Strait)", -4436.7f, 1405.8f, 1000001, 1265.5f, 1309.9f),
        new(8_390_005, 1000001, "Afro-Asian Outer Coast (Iceland)", 1643.4f, -1080.2f, 1000003, -4034.7f, -1133.3f),
        new(8_390_006, 1000003, "Atlantic Outer Coast (Iceland)", -4034.7f, -1133.3f, 1000001, 1643.4f, -1080.2f),
    ];

    private void BboSendOceanGates()
    {
        foreach (BboOceanGate gate in OceanGates.Where(entry => entry.Ocean == Scene && Context.Data.Scenes.Exists(entry.TargetOcean)))
        {
            (float gateX, float gateZ) = GateSpot(gate);
            Send(ScenePackets.CreateObject(gate.Id, ObjectKind.Door, BboSeaPackets.OceanDockModel), $"(ocean gate {gate.Name})");
            Send(ScenePackets.ObjectProperties(gate.Id, SeaPackets.Known(new PropertyBag
            {
                ["Name"] = gate.Name,
                ["X"] = gateX,
                ["Y"] = 0f,
                ["Z"] = gateZ,
                ["Orient"] = 0f,
            })), $"(ocean gate {gate.Name} properties)");
            Send(ScenePackets.PlaceObject(gate.Id, gateX, 0f, gateZ), $"(ocean gate {gate.Name} position)");
            Send(ScenePackets.ShowObject(gate.Id), $"(show ocean gate {gate.Name})");
        }
    }

    private static (float X, float Z) GateSpot(BboOceanGate gate) => (gate.X, gate.Z);

    private bool BboOceanGateClicked(int objectId)
    {
        if (!BboOceanScene(Scene) || Mover == null) return false;
        BboOceanGate? gate = OceanGates.FirstOrDefault(entry => entry.Ocean == Scene && entry.Id == objectId);
        if (gate == null) return false;
        (float gateX, float gateZ) = GateSpot(gate);
        (float arriveX, float arriveZ) = (gate.TargetX, gate.TargetZ);
        var link = new SceneLink
        {
            Id = gate.Id,
            Scene = gate.Ocean,
            Name = gate.Name,
            X = gateX,
            Z = gateZ,
            Radius = BboSeaPackets.OceanDockReach,
            TargetScene = gate.TargetOcean,
            TargetX = arriveX,
            TargetZ = arriveZ,
        };
        (float x, float z) = Mover.Now();
        if (link.DistanceTo(x, z) <= link.Radius)
        {
            Log.Info(Tag, $"{Name} crosses {gate.Name} into ocean {gate.TargetOcean}");
            UseLink(link);
            return true;
        }
        MoveTo(link.X, link.Z);
        WatchArrival(link);
        return true;
    }
}
