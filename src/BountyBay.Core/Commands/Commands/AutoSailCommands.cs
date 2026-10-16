using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class AutoSailCommands : ITextCommand
{
    public IEnumerable<string> Names => ["AutoDriveChange"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.ToggleAutoSail();
        return true;
    }
}
