using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class TeamCommands : ITextCommand
{
    public IEnumerable<string> Names => ["TeamInvite", "TeamObj", "KickOut", "FallOut", "ChangeTeamOceanMode", TeamDirectory.RequestKind];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "TeamInvite" or "TeamObj" when words.Length == 2:
                TeamDirectory.Invite(session, words[1]);
                return true;
            case "KickOut" when words.Length == 2:
                TeamDirectory.Kick(session, words[1]);
                return true;
            case "FallOut":
                TeamDirectory.Leave(session);
                return true;
            case "ChangeTeamOceanMode" when words.Length == 2 && int.TryParse(words[1], out int mode):
                TeamDirectory.SetOceanMode(session, mode);
                return true;
            case TeamDirectory.RequestKind when words.Length == 2 && words[1] is "0" or "1":
                TeamDirectory.Answer(session, words[1] == "1");
                return true;
            default:
                return false;
        }
    }
}

public sealed class TeamChatCommand : ITextCommand
{
    public IEnumerable<string> Names => ["c1"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 2) return true;
        string text = string.Join(' ', words.Skip(1));
        if (session.ChatAllowed("team", ref text)) TeamDirectory.Chat(session, text);
        return true;
    }
}
