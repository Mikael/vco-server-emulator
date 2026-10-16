using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed record RankingEntry(int Rank, string Name, long Value, string Extra);

public static class Rankings
{
    public const int Size = 10;

    public static List<CharacterRecord> Characters(ServerContext context)
    {
        var byName = new Dictionary<string, CharacterRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (CharacterRecord character in context.Accounts.AllCharacters()) byName[character.Name] = character;
        foreach (ClientSession session in context.World.Sessions.Where(session => session.InWorld && session.Persistent))
            byName[session.Name] = session.Character;
        return byName.Values.ToList();
    }

    public static List<RankingEntry> Richest(ServerContext context)
        => Number(Characters(context)
            .Select(character => (character.Name, Value: (long)Math.Max(0, character.Money) + Math.Max(0, character.Bank), Extra: character.Guild))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase));

    public static List<RankingEntry> Guilds(ServerContext context)
        => Number(Characters(context)
            .Select(character => character.Guild)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => GuildDirectory.Find(context, name))
            .OfType<Guild>()
            .OrderByDescending(guild => guild.Members.Count)
            .ThenByDescending(guild => guild.Level)
            .ThenByDescending(guild => guild.Fund)
            .Select(guild => (guild.Name, Value: (long)guild.Members.Count,
                Extra: guild.Members.FirstOrDefault(member => member.Duty == GuildDuty.Leader)?.Name ?? "")));

    public static List<RankingEntry> Masters(ServerContext context)
        => Number(Characters(context)
            .Select(character => (character.Name, Value: (long)character.Apprentices.Count + character.GraduatedApprentices, Extra: character.Guild))
            .Where(entry => entry.Value > 0)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase));

    public static void Tell(ClientSession session, string command)
    {
        string kind = command.TrimStart('/').ToLowerInvariant();
        if (kind.StartsWith("order/", StringComparison.Ordinal)) kind = kind["order/".Length..];
        switch (kind)
        {
            case "getrich":
                Report(session, "Fortune Ranking", "silver", Richest(session.Context));
                break;
            case "getcons":
                Report(session, "Guild Ranking", "members", Guilds(session.Context));
                break;
            default:
                Report(session, "Master Ranking", "apprentices", Masters(session.Context));
                break;
        }
    }

    public static bool IsRequest(string text)
    {
        string word = text.Trim().Split(' ', 2)[0].ToLowerInvariant();
        if (word.StartsWith("order/", StringComparison.Ordinal)) word = word["order".Length..];
        return word is "/getrich" or "/getcons" or "/getteacher";
    }

    public static void Open(ClientSession session, RankingList list)
    {
        List<RankingEntry> entries = list switch
        {
            RankingList.Guilds => Guilds(session.Context),
            RankingList.Treasure or RankingList.Business => Richest(session.Context),
            _ => [],
        };
        session.Send(BboSocialPackets.OpenRanking(list), $"(ranking window {list})");
        session.Send(BboSocialPackets.ClearRanking(), "(clear ranking)");
        foreach (RankingEntry entry in entries)
            session.Send(BboSocialPackets.RankingRow(entry.Rank, entry.Name, entry.Value, entry.Extra), "(ranking row)");
        Log.Debug(session.Tag, $"{session.Name} opens ranking {list} with {entries.Count} rows");
    }

    private static void Report(ClientSession session, string title, string unit, List<RankingEntry> entries)
    {
        if (entries.Count == 0)
        {
            session.SystemMessage($"{title}: nobody is listed yet.");
            return;
        }
        session.SystemMessage($"{title}:");
        foreach (RankingEntry entry in entries)
            session.SystemMessage($"{entry.Rank}. {entry.Name} - {entry.Value} {unit}{(entry.Extra.Length > 0 ? $" ({entry.Extra})" : "")}");
    }

    private static List<RankingEntry> Number(IEnumerable<(string Name, long Value, string Extra)> ordered)
        => ordered.Take(Size).Select((entry, index) => new RankingEntry(index + 1, entry.Name, entry.Value, entry.Extra)).ToList();
}
