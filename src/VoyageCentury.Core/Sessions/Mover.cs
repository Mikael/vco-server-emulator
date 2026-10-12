using System.Diagnostics;

namespace VoyageCentury.Core.Sessions;

public sealed class Mover
{
    private float _startX;
    private float _startZ;
    private long _startedAt;

    public Mover(float x, float z, float speed)
    {
        _startX = DestinationX = x;
        _startZ = DestinationZ = z;
        Speed = speed;
        _startedAt = Stopwatch.GetTimestamp();
    }

    public float DestinationX { get; private set; }

    public float DestinationZ { get; private set; }

    public float Speed { get; set; }

    public int Heading { get; private set; }

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

    public (float X, float Z) MoveTo(float x, float z)
    {
        (_startX, _startZ) = Now();
        float dx = x - _startX;
        float dz = z - _startZ;
        if (dx * dx + dz * dz > 1e-6f)
        {
            double tenths = Math.Atan2(dx, dz) * 1800.0 / Math.PI;
            Heading = (int)Math.Round(tenths < 0 ? tenths + 3600.0 : tenths) % 3600;
        }
        DestinationX = x;
        DestinationZ = z;
        _startedAt = Stopwatch.GetTimestamp();
        return (_startX, _startZ);
    }
}
