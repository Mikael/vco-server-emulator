namespace BountyBay.Core.Protocol.Packets;

// dlgFGRebuild (General equipment rebuild).
public static class AideArmPackets
{
    // 1: rebuilt result in view 54 slot 2 is shown with Save/Cancel; 0: back to the rebuild buttons.
    public static byte[] Result(bool shown) => UiPackets.Custom($"scRefreshArmProp {(shown ? 1 : 0)}");

    // Free rebuilds left today; with any left the window shows the free rebuild button.
    public static byte[] FreeCount(int left) => UiPackets.Custom($"scRefreshFreeCount {left}");

    // Owned amount of the material for rebuild mode 0 or 1 (the window shows owned / needed).
    public static byte[] Owned(int mode, int amount) => UiPackets.Custom($"scAideRebuildView {mode} {amount}");
}
