using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class FleetCommandCommands : ITextCommand
{
    public IEnumerable<string> Names => ["AppointCaptian"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.AppointFleetCaptain();
        return true;
    }
}
