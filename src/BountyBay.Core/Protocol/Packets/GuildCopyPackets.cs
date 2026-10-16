namespace BountyBay.Core.Protocol.Packets;

public static class GuildCopyPackets
{
    public const int ApplicantTable = 80;
    public const int Waiting = 1;
    public const int Approved = 2;

    public static byte[] Show() => UiPackets.Custom("scShowConsCopy");

    public static byte[] SeasonTime(int seconds) => UiPackets.Custom($"scConSeasonChangeTime {Math.Max(0, seconds)}");

    public static byte[] ClearApplicants() => GuildPackets.Clear(ApplicantTable);

    public static byte[] RemoveApplicant(string name) => GuildPackets.Delete(ApplicantTable, name);

    public static IEnumerable<byte[]> Applicants(IEnumerable<(string Name, bool Online, int State)> rows)
        => GuildPackets.Rows(ApplicantTable, rows.Select(row => new[] { row.Name, row.Online ? "1" : "0", row.State.ToString() }));

    public static byte[] Invite() => UiPackets.Custom("scInviteConCopy");

    public static byte[] TimeLeft(int seconds) => UiPackets.Custom($"scConCopyTime {Math.Max(0, seconds)}");

    public static byte[] Stage(int stage) => UiPackets.Custom($"scConCopyInfo {Math.Max(1, stage)}");
}
