using System.Text;

namespace BountyBay.Core.Protocol.Packets;

public sealed record GuildMatchSide(string Name, int Flag, long Activity, string Leader);

public sealed record GuildMatchBetRow(string Id, int ServerKind, int Round, string Match, string Guild, string Type, long Amount, int Result, long Reward);

public static class GuildMatchPackets
{
    public const int BetTable = 72;
    public const int Slots = 7;
    public const int Waiting = 0;
    public const int Betting = 1;
    public const int Fighting = 2;
    public const int LeftWon = 3;
    public const int RightWon = 4;

    public static byte[] ClearList(int list) => UiPackets.Custom($"scVConsClearMList{ListNumber(list)}");

    public static byte[] Slot(int list, int slot, string left, string right, int state)
        => UiPackets.Custom($"scVConsMatchMList{ListNumber(list)} {slot} {Name(left)} {Name(right)} {Math.Clamp(state, 0, 6)}");

    public static byte[] Champion(string guild) => UiPackets.Custom($"scWinCons1 {Name(guild)}");

    public static byte[] Detail(int list, int slot, GuildMatchSide left, GuildMatchSide right, IEnumerable<(string Type, int Unit)> wagers)
    {
        var text = new StringBuilder(list == 2 ? "scGambleDetail2" : "scGambleDetail").Append(' ').Append(slot);
        foreach (GuildMatchSide side in new[] { left, right })
            text.Append(' ').Append(Name(side.Name)).Append(' ').Append(side.Flag).Append(' ').Append(side.Activity).Append(' ').Append(Name(side.Leader));
        (string Type, int Unit)[] offered = wagers.ToArray();
        text.Append(' ').Append(offered.Length);
        foreach ((string type, int unit) in offered) text.Append(' ').Append(Name(type)).Append(' ').Append(1).Append(' ').Append(unit);
        return UiPackets.Custom(text.ToString());
    }

    public static byte[] Report(string left, string right, string score, IEnumerable<int> rounds)
    {
        var text = new StringBuilder("scMatchDetail ").Append(Field(left)).Append(',').Append(Field(right)).Append(",0,").Append(Field(score));
        int number = 0;
        foreach (int winner in rounds) text.Append(',').Append(winner).Append(' ').Append(++number);
        return UiPackets.Custom(text.ToString());
    }

    public static byte[] ClearBets() => GuildPackets.Clear(BetTable);

    public static byte[] RemoveBet(string id) => GuildPackets.Delete(BetTable, id);

    public static IEnumerable<byte[]> Bets(IEnumerable<GuildMatchBetRow> rows)
        => GuildPackets.Rows(BetTable, rows.Select(row => new[]
        {
            row.Id, row.ServerKind.ToString(), row.Round.ToString(), row.Match, row.Guild, row.Type, row.Amount.ToString(),
            row.Result.ToString(), row.Reward.ToString(),
        }));

    private static int ListNumber(int list) => list == 2 ? 2 : 1;

    private static string Name(string text) => UiPackets.Token(text);

    private static string Field(string text)
    {
        string cleaned = UiPackets.Line(text).Replace(',', ' ').Trim();
        return cleaned.Length == 0 ? "-" : cleaned;
    }
}
