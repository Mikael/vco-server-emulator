namespace BountyBay.Core.Protocol.Packets;

public static class AideArmPackets
{
    public static byte[] Result(bool shown) => UiPackets.Custom($"scRefreshArmProp {(shown ? 1 : 0)}");

    public static byte[] FreeCount(int left) => UiPackets.Custom($"scRefreshFreeCount {left}");

    public static byte[] Owned(int mode, int amount) => UiPackets.Custom($"scAideRebuildView {mode} {amount}");
}
