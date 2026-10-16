namespace BountyBay.Core.Protocol.Packets;

public static class ArmRebuildPackets
{
    public const int RebuildView = 46;
    public const int RecordView = 47;
    public const int TransferView = 37;

    public static byte[] Resources(IEnumerable<(string Key, int Have, int Need)> lines)
        => UiPackets.Custom("scArmRebuildResource" + Triples(lines));

    public static byte[] ExtraResources(IEnumerable<(string Key, int Have, int Need)> lines)
        => UiPackets.Custom("scArmRebuildEx" + Triples(lines));

    public static byte[] Animation(bool start) => UiPackets.Custom($"ArmRebuild {(start ? 1 : 0)}");

    public static byte[] ResultLine(string text) => UiPackets.Custom("scArmRebuildResult " + UiPackets.Line(text));

    public static byte[] ResetLocks() => UiPackets.Custom("scArmRebuildLock");

    public static byte[] ClearRecords() => UiPackets.Custom("scClearArmRebuildProp");

    public static byte[] Record(int index, string item, string info, int sign, string skill)
        => UiPackets.Custom($"scRebuildInfo {index} {UiPackets.Token(item)}${info}${sign}$0" + (skill.Length > 0 ? "$" + skill : ""));

    public static byte[] TakeOff(int view, int index) => UiPackets.Custom($"scTakeOffThing {view} {index}");

    private static string Triples(IEnumerable<(string Key, int Have, int Need)> lines)
        => string.Concat(lines.Select(line => $" {UiPackets.Token(line.Key)} {line.Have} {line.Need}"));
}
