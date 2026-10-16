using System.Diagnostics;

namespace BountyBay.Core.Sessions;

public sealed class Mover
{
    private float _startX;
    private float _startZ;
    private long _startedAt;
    private float _startHeading;

    public Mover(float x, float z, float speed, int heading = 0, int turnRate = 0)
    {
        _startX = DestinationX = x;
        _startZ = DestinationZ = z;
        Speed = speed;
        Heading = heading;
        _startHeading = heading;
        TurnRate = turnRate;
        _startedAt = Stopwatch.GetTimestamp();
    }

    public float DestinationX { get; private set; }

    public float DestinationZ { get; private set; }

    public float Speed { get; set; }

    public int Heading { get; private set; }

    public int TurnRate { get; set; }

    public (float X, float Z) Now()
    {
        float dx = DestinationX - _startX;
        float dz = DestinationZ - _startZ;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance < 1e-6f) return (DestinationX, DestinationZ);
        float elapsed = (float)Stopwatch.GetElapsedTime(_startedAt).TotalSeconds;
        float fraction = MathF.Min(1f, elapsed * Speed / distance);
        return (_startX + dx * fraction, _startZ + dz * fraction);
    }

    public int HeadingNow()
    {
        if (TurnRate <= 0) return Heading;
        float difference = Wrap(Heading - _startHeading);
        float turned = TurnRate * (float)Stopwatch.GetElapsedTime(_startedAt).TotalSeconds;
        if (turned >= MathF.Abs(difference)) return Heading;
        float heading = _startHeading + MathF.Sign(difference) * turned;
        return ((int)MathF.Round(heading) % 3600 + 3600) % 3600;
    }

    public (float X, float Z) MoveTo(float x, float z)
    {
        int heading = HeadingNow();
        (_startX, _startZ) = Now();
        _startHeading = heading;
        float dx = x - _startX;
        float dz = z - _startZ;
        if (dx * dx + dz * dz > 1e-6f)
        {
            double tenths = Math.Atan2(dx, dz) * 1800.0 / Math.PI;
            Heading = (int)Math.Round(tenths < 0 ? tenths + 3600.0 : tenths) % 3600;
        }
        else
        {
            Heading = heading;
        }
        DestinationX = x;
        DestinationZ = z;
        _startedAt = Stopwatch.GetTimestamp();
        return (_startX, _startZ);
    }

    private static float Wrap(float tenths)
    {
        tenths %= 3600f;
        if (tenths > 1800f) tenths -= 3600f;
        if (tenths <= -1800f) tenths += 3600f;
        return tenths;
    }
}
