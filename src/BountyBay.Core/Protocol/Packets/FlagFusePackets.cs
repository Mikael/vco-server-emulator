namespace BountyBay.Core.Protocol.Packets;

public static class FlagFusePackets
{
    public const string NoAmount = "-1,-1";

    public static byte[] FlagStuffAmount(string forge, string remake) => UiPackets.Custom($"scFlagStuffAmount {forge} {remake}");

    public static byte[] ForgeReturn(int view, int slot) => UiPackets.Custom($"scForgeReturn {view} {slot}");

    public static byte[] ForgeRestore(int page) => UiPackets.Custom($"csSucMake1 {page}");

    public static byte[] FuseButtons(int state) => UiPackets.Custom($"scFuseChangeBtn {state}");
}
