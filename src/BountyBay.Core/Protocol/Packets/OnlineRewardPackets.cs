namespace BountyBay.Core.Protocol.Packets;

public static class OnlineRewardPackets
{
    public static byte[] Countdown(int stage, int seconds) => UiPackets.Custom($"scAwardStatus 0 {stage} {Math.Max(0, seconds)}");

    public static byte[] Claimed(int stage) => UiPackets.Custom($"scAwardStatus 1 1 {stage}");

    public static byte[] Resync(int stage, int seconds) => UiPackets.Custom($"scAwardStatus 1 0 {stage} {Math.Max(0, seconds)}");
}
