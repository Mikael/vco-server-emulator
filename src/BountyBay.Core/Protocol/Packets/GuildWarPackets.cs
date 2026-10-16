namespace BountyBay.Core.Protocol.Packets;

public static class GuildWarList
{
    public const int CitySettings = 8;
    public const int HonorConsReport = 54;
    public const int Registrations = 55;
    public const int Bidders = 58;
}

public static class GuildWarRegistration
{
    public const int Awaiting = 1;
    public const int Approved = 2;
}

public static class GuildWarPackets
{
    public const string BidCommand = "ConsBid";
    public const string ImpeachVoteCommand = "csImpeachVote";

    public static byte[] ConsWar(int state, IEnumerable<(int Current, int Max)> bars)
        => UiPackets.Custom("ConsWar " + string.Join(GuildPackets.Separator,
            bars.Take(9).SelectMany(bar => new[] { Math.Max(0, bar.Current).ToString(), Math.Max(0, bar.Max).ToString() })
                .Prepend(state.ToString())));

    public static byte[] ShowBidList(bool canBid, long highestUnits, long minimumUnits)
        => UiPackets.Custom($"ShowWarBidList {(canBid ? 1 : 0)} {Math.Max(0, highestUnits)} {Math.Max(0, minimumUnits)}");

    public static byte[] HideBidList() => UiPackets.Custom("scHideConsWarBid");

    public static byte[] BidInput(int minimumUnits, int maximumUnits, string text, string title, string label)
        => UiPackets.NumberInputBox(minimumUnits, maximumUnits, text, title, label, BidCommand);

    public static byte[] EntryTimes(int used, int allowed) => UiPackets.Custom($"scConWarBattleInfo {Math.Max(0, used)} {Math.Max(0, allowed)}");

    public static byte[] AskLeave() => UiPackets.Custom("scLeaveConWar 1");

    public static byte[] Kill(string text) => UiPackets.Custom("scCons_kill " + UiPackets.Line(text));

    public static byte[] Killed(string text) => UiPackets.Custom("scConsBekill " + UiPackets.Line(text));

    public static byte[] ShowCitySettings() => UiPackets.Custom("ShowWarCitySetting");

    public static byte[] WarStat(int page, IReadOnlyList<string> values)
        => UiPackets.Custom(string.Join(' ', values.Select(UiPackets.Token).Prepend(values.Count.ToString()).Prepend(page.ToString()).Prepend("ShowWarStat")));

    public static byte[] ShowStatue(bool canEdit, string guild, string leader, string since, string commerce, string message)
        => UiPackets.Custom(string.Join(' ', new[]
        {
            "ShowStatue", canEdit ? "1" : "0", UiPackets.Token(guild), UiPackets.Token(leader), UiPackets.Token(since),
            UiPackets.Token(commerce), UiPackets.Token(message),
        }));

    public static byte[] TeamVote(int seconds, string command, IReadOnlyList<string> options, string text)
        => UiPackets.Custom(string.Join(' ', new[] { "scTeamVote", Math.Max(1, seconds).ToString(), UiPackets.Token(command), Math.Min(options.Count, 4).ToString() }
            .Concat(options.Take(4).Select(UiPackets.Token)).Append(UiPackets.Token(text))));

    public static byte[] Bulletin(string text, int kind) => ChatPackets.SystemMessage(text, (SystemMessageKind)kind);
}
