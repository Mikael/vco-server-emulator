namespace BountyBay.Core.Protocol.Packets;

public static class ColonyList
{
    public const int CityMembers = 13;
    public const int Unions = 18;
    public const int UnionGuilds = 49;
}

public static class ColonyRouteType
{
    public const int Industry = 0;
    public const int Trade = 1;
    public const int Gathering = 2;
    public const int Military = 3;
}

public static class ColonyPackets
{
    public static byte[] ChooseStyle() => UiPackets.Custom("ShowCSCBox");

    public static byte[] InputCityName() => UiPackets.Custom("InputCityName");

    public static byte[] Route(bool update, int type, int points, IEnumerable<(bool Enabled, int Current, int Max)> buttons)
        => UiPackets.Custom(string.Join(' ', buttons.Select(button => $"{(button.Enabled ? 1 : 0)},{button.Current},{button.Max}")
            .Prepend(Math.Max(0, points).ToString()).Prepend(type.ToString()).Prepend(update ? "UpdataDLMap" : "ShowDLMap")));

    public static byte[] CityInfo(int cityType, string location, string region, int grade, string route, long fund,
        IReadOnlyList<(int Value, int Max)> indexes, int population, int food)
        => UiPackets.Custom(string.Join(' ', new[]
        {
            "CityInfo_A", cityType.ToString(), $"{Plain(location)},{Plain(region)}", grade.ToString(), UiPackets.Token(route), Math.Max(0, fund).ToString(),
        }.Concat(indexes.Take(4).Select(index => $"{Math.Max(0, index.Value)},{Math.Max(1, index.Max)}")).Concat(new[] { population.ToString(), food.ToString() })));

    public static byte[] BuildCityInfo(string name, int unionCurrent, int unionRequired, int guildCurrent, int guildRequired)
        => UiPackets.Custom($"BuildCityInfo {UiPackets.Token(name)} {unionCurrent} {unionRequired} {guildCurrent} {guildRequired}");

    public static byte[] NoNextPage() => UiPackets.Custom("NoNextPage");

    public static byte[] NoPrevPage() => UiPackets.Custom("NoPrevPage");

    private static string Plain(string text)
    {
        string token = UiPackets.Token(text).Replace(",", "");
        return token.Length == 0 ? "-" : token;
    }
}
