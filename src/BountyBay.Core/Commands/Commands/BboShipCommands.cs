using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class BboShipCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csHealSailor"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csHealSailor":
                session.BboHealSailors();
                return true;
            default:
                return false;
        }
    }
}
