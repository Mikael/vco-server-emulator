using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class LeaveWordCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csSendInvite", "csWatchFinding"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csSendInvite" when words.Length >= 3 && int.TryParse(words[1], out int paperType):
                session.SendLeaveWordInvite(paperType, words[2], string.Join(' ', words.Skip(3)));
                return true;
            case "csWatchFinding" when words.Length == 2 && int.TryParse(words[1], out int table):
                session.WatchFindings(table);
                return true;
            default:
                return false;
        }
    }
}
