using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public static class AideTables
{
    public const byte RecruitId = 73;

    public static readonly TableDefinition Recruit = new(RecruitId, "AideConscribe",
    [
        new("Code", PropertyType.U32),
        new("Skill", PropertyType.String),
        new("Quality", PropertyType.U32),
        new("Price", PropertyType.U32),
        new("Taken", PropertyType.U32),
    ]);

    public static readonly TableDefinition[] All = [Recruit];
}

public static class AidePackets
{
    public static byte[] ShowRecruitList() => UiPackets.Custom("scShowAideList");

    public static byte[] NewAide() => UiPackets.Custom("scGetNewAide");

    public static byte[] RecruitItem(int code, string skill, int quality, int price, bool taken)
        => UiPackets.Custom($"scAideItem {code} {UiPackets.Token(skill)} {quality} {price} {(taken ? 1 : 0)}");

    public static byte[] TrainResult(IReadOnlyList<int> deltas)
        => UiPackets.Custom("scPropChange " + string.Join(' ', deltas.Take(4).Select(delta => delta.ToString(CultureInfo.InvariantCulture))));

    public static byte[] TrainFailed() => UiPackets.Custom("scPropChangeFail");

    public static byte[] TransferRatio(int lossPercent) => UiPackets.Custom($"scSelectAide {Math.Clamp(lossPercent, 0, 100)}");

    public static byte[] SuitAdded(int aide, int suit, int slot, string item)
        => UiPackets.Custom($"scAddAideSuitOK {aide} {suit} {slot} {UiPackets.Token(item)}");

    public static byte[] SuitRemoved(int aide, int suit, int slot) => UiPackets.Custom($"scDelAideSuitOK {aide} {suit} {slot}");
}
