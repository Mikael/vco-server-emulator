namespace BountyBay.Core.Protocol.Packets;

public static class RobberyPackets
{
    public const int Bribe = 0;
    public const int Fight = 1;

    public static byte[] VictimTimer(int seconds, int money) => ChatPackets.Script(0, $"scRobTimer {Math.Max(1, seconds)} 0 {Math.Max(0, money)}");

    public static byte[] RobberTimer(int seconds) => ChatPackets.Script(0, $"scRobTimer {Math.Max(1, seconds)} 1");

    public static byte[] Close() => ChatPackets.Script(0, "CloseRobBox");
}
