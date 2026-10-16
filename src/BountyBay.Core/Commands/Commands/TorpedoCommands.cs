using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class TorpedoCommands : ITextCommand
{
    public IEnumerable<string> Names => ["SetupTorpedo"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.SetupTorpedo();
        return true;
    }
}
