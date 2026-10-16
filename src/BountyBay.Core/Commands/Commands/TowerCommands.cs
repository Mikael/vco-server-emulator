using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class TowerCommands : ITextCommand
{
    public IEnumerable<string> Names => ["LeaveTower"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (session.TowerHere != null) session.TowerLeave();
        else session.BattleLeave(words[0], 1);
        return true;
    }
}
