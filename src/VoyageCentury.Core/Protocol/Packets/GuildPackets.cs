using System.Text;

namespace VoyageCentury.Core.Protocol.Packets;

public static class GuildList
{
    public const int Guilds = 0;
    public const int Members = 1;
    public const int Applicants = 2;
    public const int Relations = 4;
}

public static class GuildPackets
{
    public const char Separator = UiPackets.FieldSeparator;
    public const string Empty = "-";
    public const int RowsPerMessage = 8;

    public static string Cell(string? text)
    {
        string cleaned = (text ?? "").Replace("\r", "").Replace('\n', ' ').Replace(Separator, ' ').Trim();
        return cleaned.Length == 0 ? Empty : cleaned;
    }

    public static byte[] ShowCreate() => UiPackets.Custom("ShowCreateCons");

    public static byte[] ShowAll() => UiPackets.Custom("ShowAllCons");

    public static byte[] Clear(int list) => UiPackets.Custom($"ct {list}");

    public static byte[] Delete(int list, string key) => UiPackets.Custom($"dt {list}{Separator}{Cell(key)}");

    public static IEnumerable<byte[]> Rows(int list, IEnumerable<string[]> rows)
    {
        foreach (string[][] chunk in rows.Chunk(RowsPerMessage))
        {
            var text = new StringBuilder($"ut {list}");
            foreach (string[] row in chunk)
            {
                foreach (string cell in row) text.Append(Separator).Append(Cell(cell));
            }
            yield return UiPackets.Custom(text.ToString());
        }
    }

    public static byte[] Detail(bool applyClosed, int flag, string tenet, string name, int members, int limit, int fund,
        string headquarters, int commerce, int industry, int level)
        => UiPackets.Custom("ConsDetail " + Join(
        [
            applyClosed ? "1" : "0", flag.ToString(), tenet, name, members.ToString(), limit.ToString(), fund.ToString(),
            headquarters, commerce.ToString(), industry.ToString(), level.ToString(),
        ]));

    public static byte[] Applicants(IReadOnlyCollection<string> names)
        => UiPackets.Custom("ConsAllApplicant " + Join(names.Prepend(names.Count.ToString())));

    public static byte[] ShowCreateDepartment(string applicant, string department, string explanation)
        => UiPackets.Custom("ShowCreateDept " + Join([applicant, department, explanation]));

    private static string Join(IEnumerable<string> cells) => string.Join(Separator, cells.Select(Cell));
}
