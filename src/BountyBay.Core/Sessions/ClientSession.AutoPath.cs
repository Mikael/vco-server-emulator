using BountyBay.Core.Logging;
using BountyBay.Core.Network;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int MaxPathPoints = 64;
    private const float PathPointReached = 0.5f;

    private int _pathSerial;

    public void FollowPath(PacketReader reader, int offset)
    {
        if (Mover == null || StallOpen) return;
        reader.Seek(offset + 1);
        int count = reader.ReadByte();
        var points = new List<(float X, float Z)>();
        for (int i = 0; i < count && i < MaxPathPoints && reader.Remaining >= 8; i++)
        {
            float x = reader.ReadSingle();
            float z = reader.ReadSingle();
            if (!float.IsFinite(x) || !float.IsFinite(z)) return;
            points.Add((x, z));
        }
        (float nowX, float nowZ) = Mover.Now();
        while (points.Count > 1 && Distance(points[0], (nowX, nowZ)) <= PathPointReached) points.RemoveAt(0);
        if (points.Count == 0) return;
        Log.Debug(Tag, $"{Name} follows a path of {points.Count} point(s) to ({points[^1].X:F1}, {points[^1].Z:F1})");

        int serial = ++_pathSerial;
        int scene = Scene;
        WalkPathPoint(points[0]);
        if (points.Count == 1) return;
        _ = Task.Run(async () =>
        {
            for (int next = 1; next < points.Count; next++)
            {
                (float X, float Z) target = points[next - 1];
                for (int check = 0; check < MaxArrivalChecks; check++)
                {
                    await Task.Delay(TimeToDestination());
                    lock (Context.World.Sync)
                    {
                        if (serial != _pathSerial || Mover == null || Scene != scene) return;
                        if (Distance((Mover.DestinationX, Mover.DestinationZ), target) > PathPointReached) return;
                        if (Distance(Mover.Now(), target) <= ArrivalSlack) break;
                    }
                }
                lock (Context.World.Sync)
                {
                    if (serial != _pathSerial || Mover == null || Scene != scene) return;
                    try
                    {
                        WalkPathPoint(points[next]);
                    }
                    catch (Exception exception)
                    {
                        Log.Error(Tag, exception);
                        return;
                    }
                }
            }
        });
    }

    private void WalkPathPoint((float X, float Z) point)
    {
        MoveTo(point.X, point.Z);
        OnMoveRequested(point.X, point.Z);
    }

    private static float Distance((float X, float Z) a, (float X, float Z) b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
