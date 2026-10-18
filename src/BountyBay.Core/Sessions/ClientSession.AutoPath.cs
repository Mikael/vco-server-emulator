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
        FollowLegs(points);
    }

    private void FollowLegs(List<(float X, float Z)> points)
    {
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
                    await Task.Delay(TimeToNextLeg());
                    lock (Context.World.Sync)
                    {
                        if (serial != _pathSerial || Mover == null || Scene != scene) return;
                        if (Distance((Mover.DestinationX, Mover.DestinationZ), target) > PathPointReached) return;
                        if (Distance(Mover.Now(), target) <= MathF.Max(ArrivalSlack, Mover.Speed * PathLeadSeconds * 1.5f)) break;
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

    private const float PathLeadSeconds = 0.3f;
    private const int ArcStartTenths = 450;
    private const int ArcStepTenths = 200;

    public bool SteerShip(float x, float z)
    {
        if (!AtSea || Mover == null || SeaSunk) return false;
        (float px, float pz) = Mover.Now();
        float dx = x - px, dz = z - pz;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance < 1f) return false;
        double heading = Mover.HeadingNow();
        double wanted = Math.Atan2(dx, dz) * 1800.0 / Math.PI;
        double turn = (wanted - heading) % 3600.0;
        if (turn > 1800) turn -= 3600;
        if (turn < -1800) turn += 3600;
        if (Math.Abs(turn) <= ArcStartTenths) return false;
        float speed = ShipSpeed;
        float rate = Math.Max(1, ShipTurnRate);
        float radius = speed / (float)(rate / 1800.0 * Math.PI);
        if (distance < radius * 1.2f) return false;
        var points = new List<(float X, float Z)>();
        double sign = Math.Sign(turn), done = 0, sinceLast = 0;
        double step = 0.1;
        float cx = px, cz = pz;
        double theta = heading;
        while (done < Math.Abs(turn))
        {
            double radians = theta * Math.PI / 1800.0;
            cx += (float)(Math.Sin(radians) * speed * step);
            cz += (float)(Math.Cos(radians) * speed * step);
            double delta = Math.Min(rate * step, Math.Abs(turn) - done);
            theta += sign * delta;
            done += delta;
            sinceLast += delta;
            double left = Math.Atan2(x - cx, z - cz) * 1800.0 / Math.PI - theta;
            left = (left % 3600 + 5400) % 3600 - 1800;
            if (sinceLast >= ArcStepTenths || Math.Abs(left) <= ArcStepTenths / 2)
            {
                points.Add((cx, cz));
                sinceLast = 0;
                if (Math.Abs(left) <= ArcStepTenths / 2) break;
            }
            if (points.Count > 24) break;
        }
        points.Add((x, z));
        FollowLegs(points);
        return true;
    }

    private TimeSpan TimeToNextLeg()
    {
        if (Mover == null) return TimeSpan.FromMilliseconds(200);
        (float x, float z) = Mover.Now();
        float left = Distance((x, z), (Mover.DestinationX, Mover.DestinationZ)) / MathF.Max(0.5f, Mover.Speed);
        return TimeSpan.FromSeconds(Math.Clamp(left - PathLeadSeconds, 0.05f, 600f));
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
