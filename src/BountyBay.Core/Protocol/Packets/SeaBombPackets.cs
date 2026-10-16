using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public static class SeaBombPackets
{
    public static byte[] ShowBombArea(float radius)
        => ChatPackets.Script(0, string.Create(CultureInfo.InvariantCulture, $"scShowBombArea {Math.Max(1f, radius):F1}"));

    public static byte[] Explosion(int victimId, float x, float z, string text)
        => ChatPackets.Script(victimId, string.Create(CultureInfo.InvariantCulture,
            $"3DA\ninfo {text}\ncolo ff4000\nweff cannon\ntype 1\nammonum 1\nchance 1.000\nx {x:F2}\ny 0.00\nz {z:F2}\n"));
}
