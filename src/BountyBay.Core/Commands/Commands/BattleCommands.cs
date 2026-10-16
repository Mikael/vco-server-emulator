using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class BattleCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csAskForEntryBattle", "csAskForLeaveBattle", "csAskForEntryTimes", "AskToBattle_Yes", "AskToBattle_No", "csChooseCamp",
        "csLeaveHonorWar", "csLeavePaleWar", "csLeaveSpartaWar", "csLeaveConstestWar", "csLeaveMatch", "csCreteGiveUp", "csEuropGiveUp",
        "csTeamBattle", "csAskForCreteWar", "csAskForEurop", "csHonorWarWinner", "csPaleWarWinner", "csTeamWarWinner",
        "csShowHonorWarTable", "csShowPaleWarTable", "csShowTeamWarTable", "csShowPKTable", "csPVPTipInfo", "csFreshReadyTime",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAskForEntryBattle" when words.Length >= 3 && int.TryParse(words[1], out int type) && int.TryParse(words[2], out int level):
                session.BattleAskEntry(type, level);
                return true;
            case "csAskForLeaveBattle":
                session.BattleLeaveQueue();
                return true;
            case "csAskForEntryTimes" when words.Length >= 2 && int.TryParse(words[1], out int asked):
                session.BattleAskTimes(asked);
                return true;
            case "AskToBattle_Yes":
                session.BattleConfirm(true);
                return true;
            case "AskToBattle_No":
                session.BattleConfirm(false);
                return true;
            case "csChooseCamp" when words.Length >= 2 && int.TryParse(words[1], out int camp):
                session.BattleChooseCamp(camp);
                return true;
            case "csLeaveHonorWar" or "csLeavePaleWar" or "csLeaveSpartaWar" or "csLeaveConstestWar" or "csLeaveMatch" or "csCreteGiveUp" or "csEuropGiveUp":
                session.BattleLeave(words[0], words.Length >= 2 && int.TryParse(words[1], out int argument) ? argument : 1);
                return true;
            case "csTeamBattle":
                session.SystemMessage("Cross-server battles are not available on this server.");
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} battle notice: {string.Join(' ', words)}");
                return true;
        }
    }
}
