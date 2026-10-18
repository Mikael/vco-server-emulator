using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public static class BboSeaPackets
{
    public const int YardToolsView = 17;
    public const int YardShipView = 22;
    public const int BuyBackView = 48;

    public const int FirstOcean = 1000001;
    public const int LastOcean = 1000004;
    public const int FirstOceanDockId = 8_300_000;
    public const int OceanDockModel = 4000284;
    public const float OceanDockReach = 40f;

    public const byte KindOceanNpc = 0x6a;
    public const byte KindOceanNpcB = 0x78;
    public const byte KindOceanNpcC = 0x79;
    public const byte KindOceanNpcD = 0x7a;
    public const byte KindRockNpc = 0x7b;
    public const byte KindOceanNpcE = 0x7c;
    public const byte KindSeaRingA = 0x7d;
    public const byte KindSeaRingB = 0x7e;
    public const byte KindSeaRingC = 0x81;

    public static byte[] BuyInfo(int view, int index, int price, int most, int extra = 0)
        => ChatPackets.Script(0, $"BuyInfo {view} {index} {price} {most} {extra}");

    public static byte[] CreateSeaRing(int objectId, byte kind, int model)
        => ScenePackets.CreateObject(objectId, (ObjectKind)kind, model);

    public static byte[] GoTo(int objectId, float x, float z)
        => ChatPackets.Script(objectId, string.Create(CultureInfo.InvariantCulture, $"3DGoTo {x:F2} {z:F2}"));

    public static byte[] AuraOn(int objectId, float first, float second)
        => ChatPackets.Script(objectId, string.Create(CultureInfo.InvariantCulture, $"3DK a {first:F2} {second:F2}"));

    public static byte[] AuraOff(int objectId) => ChatPackets.Script(objectId, "3DK d");

    public static byte[] GroundArea(int objectId, int power, float x, float y, float z, float radius)
        => ChatPackets.Script(objectId, string.Create(CultureInfo.InvariantCulture, $"3Dla {power} {x:F2} {y:F2} {z:F2} {radius:F2}"));

    public static byte[] NpcEffect(int objectId, float x, float y, float z, float angle)
        => ChatPackets.Script(objectId, string.Create(CultureInfo.InvariantCulture, $"3DSS\n{x:F2}\n{y:F2}\n{z:F2}\n{angle:F3}"));

    public static byte[] LandEffect(int objectId, string key, string effect, float x, float y, float z)
        => ChatPackets.Script(objectId, string.Create(CultureInfo.InvariantCulture, $"3DSN\n{key}\n{effect}\n{x:F2}\n{y:F2}\n{z:F2}"));

    public static PropertyBag FiringArc(int cannonsLeft, int cannonsRight, float range, float angle) => SeaPackets.Known(new PropertyBag
    {
        ["AttackDistL"] = cannonsLeft > 0 ? (int)range : 0,
        ["AttackAngleL"] = cannonsLeft > 0 ? (int)angle : 0,
        ["AttackDistR"] = cannonsRight > 0 ? (int)range : 0,
        ["AttackAngleR"] = cannonsRight > 0 ? (int)angle : 0,
    });
}
