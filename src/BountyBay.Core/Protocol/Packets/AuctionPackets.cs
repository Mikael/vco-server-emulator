using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public static class AuctionPackets
{
    public static byte[] ItemInfo(int reserve, string name, int seconds, int? bid)
        => UiPackets.Custom(string.Join(' ',
            "scAuctionItemInfo",
            Number(reserve),
            UiPackets.Token(name),
            Number(seconds),
            bid is { } value ? Number(value) : ""));

    public static byte[] History(int row, string date, string winner, string item, int price)
        => UiPackets.Custom(string.Join(' ',
            "scHistoryAuction",
            Number(row),
            UiPackets.Token(date),
            UiPackets.Token(winner),
            UiPackets.Token(item),
            Number(price)));

    public static byte[] ImpawnBargain() => UiPackets.Custom("scImpawnBargain");

    public static byte[] ImpawnDone() => UiPackets.Custom("scImpawnDone");

    public static byte[] ImpawnCancel() => UiPackets.Custom("scImpawnCancel");

    private static string Number(int value) => Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
}
