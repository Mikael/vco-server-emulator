using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class GuildMatchCommands : ITextCommand
{
    public IEnumerable<string> Names =>
        ["csVConsMatchMList", "csViewConsMatch", "csViewGamble", "csViewMyGamble", "csGambleConsMatch", "csGetConsMReward"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csVConsMatchMList":
                session.ShowGuildMatches(words.Length > 1 && words[1] == "2" ? 2 : 1);
                return true;
            case "csViewConsMatch" when BboArguments.Last(words, out int report):
                session.ViewGuildMatchReport(report);
                return true;
            case "csViewGamble" when BboArguments.Last(words, out int slot):
                session.ViewGuildMatchWager(slot, 1);
                return true;
            case "csViewMyGamble":
                session.ViewGuildMatchBets();
                return true;
            case "csGambleConsMatch" when words.Length >= 5 && int.TryParse(words[1], out int match) && int.TryParse(words[2], out int side)
                && int.TryParse(words[^1], out int amount):
                session.WagerGuildMatch(match, side, string.Join(' ', words[3..^1]), amount);
                return true;
            case "csGetConsMReward" when words.Length >= 2:
                session.ClaimGuildMatchReward(words[1]);
                return true;
            default:
                return false;
        }
    }
}
