using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class TowerCommands : ITextCommand
{
    public IEnumerable<string> Names => ["LeaveTower", "csAskCurDetail", "csNoticeShipInfo"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAskCurDetail":
                session.TowerInfo();
                return true;
            case "csNoticeShipInfo":
                session.SeaInstanceInfo();
                return true;
        }
        if (session.TowerHere != null) session.TowerLeave();
        else session.BattleLeave(words[0], 1);
        return true;
    }
}
