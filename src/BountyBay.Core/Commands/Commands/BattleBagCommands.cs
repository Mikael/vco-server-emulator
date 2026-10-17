using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class BattleBagCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csOpenGlobalView", "csCloseGlobalView"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words[0] == "csOpenGlobalView") session.OpenBattleBag();
        else session.CloseBattleBag();
        return true;
    }
}
