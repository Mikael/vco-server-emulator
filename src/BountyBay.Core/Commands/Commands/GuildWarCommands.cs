using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class GuildWarCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "AskConsWarInfo", "ChangeConsWarState", "BeforeConsBid", GuildWarPackets.BidCommand, "csCloseConsWarBid", "csAskConWarEntryTimes",
        "csAskConwarApplyList", "csApplyConwar", "csAgreeApply", "csRefuseApply", "csConsCityBattleCommit", "csLeaveConsWar", "csLeaveConWar",
        "StatueSetMessage", "WarCitySetting",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        GuildWarDirectory.Tick(session.Context);
        switch (words[0])
        {
            case "AskConsWarInfo":
                GuildWarDirectory.ShowConsWar(session);
                return true;
            case "ChangeConsWarState" when BboArguments.Last(words, out int state):
                GuildWarDirectory.ChangeState(session, state);
                return true;
            case "BeforeConsBid":
                GuildWarDirectory.BeforeBid(session);
                return true;
            case GuildWarPackets.BidCommand:
                if (words.Length >= 2 && long.TryParse(words[^1].Replace(",", ""), out long units)) GuildWarDirectory.Bid(session, units);
                else session.SystemMessage("Enter the amount to add to your guild's bid.");
                return true;
            case "csCloseConsWarBid":
                session.GuildWarCityName = "";
                return true;
            case "csAskConWarEntryTimes":
                GuildWarDirectory.EntryTimes(session);
                return true;
            case "csAskConwarApplyList":
                GuildWarDirectory.ShowRegistrations(session);
                return true;
            case "csApplyConwar":
                GuildWarDirectory.Apply(session);
                return true;
            case "csAgreeApply" or "csRefuseApply" when words.Length >= 2:
                GuildWarDirectory.Decide(session, BboArguments.Rest(words, 1).Replace('\u0003', ' '), words[0] == "csAgreeApply");
                return true;
            case "csConsCityBattleCommit":
                GuildWarDirectory.Commit(session);
                return true;
            case "csLeaveConsWar":
                GuildWarDirectory.Surrender(session);
                return true;
            case "csLeaveConWar":
                GuildWarDirectory.Leave(session, BboArguments.Last(words, out int argument) ? argument : -1);
                return true;
            case "StatueSetMessage":
                GuildWarDirectory.SetStatueMessage(session, BboArguments.Rest(words, 1).Replace('\u0003', ' '));
                return true;
            case "WarCitySetting" when words.Length >= 8 && ParseSettings(words, out string city, out int[] values):
                GuildWarDirectory.SetCity(session, city, values[0], values[1], values[2], values[3], values[4], values[5]);
                return true;
            case "WarCitySetting":
                Log.Debug(session.Tag, $"{session.Name} malformed siege settings: {string.Join(' ', words)}");
                return true;
            default:
                return false;
        }
    }

    private static bool ParseSettings(string[] words, out string city, out int[] values)
    {
        values = new int[6];
        city = string.Join(' ', words[1..^6]).Replace('\u0003', ' ');
        for (int index = 0; index < 6; index++)
        {
            if (!int.TryParse(words[words.Length - 6 + index], out values[index])) return false;
        }
        return city.Length > 0;
    }
}
