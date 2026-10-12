using VoyageCentury.Core.Protocol.Packets;
using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands.Commands;

public sealed class LogoutCommands : ITextCommand
{
    public IEnumerable<string> Names => ["AskLogout", "CancelLogout"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words[0] == "AskLogout") session.BeginLogout();
        else session.CancelLogout();
        return true;
    }
}

public sealed class PlayerInfoCommand : ITextCommand
{
    public IEnumerable<string> Names => ["GetPlayerInfo"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.SendPlayerInfo(words.Length > 1 ? words[1] : "");
        return true;
    }
}

public sealed class FriendCommands : ITextCommand
{
    public IEnumerable<string> Names => ["AddFriend", "RemoveFriend", "AddBlacklist", "RemoveBlacklist"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length is < 2 or > 3) return false;
        bool blacklist = words[0].EndsWith("Blacklist", StringComparison.Ordinal) || (words.Length == 3 && words[2] == "1");
        session.ChangeFriend(words[1], words[0].StartsWith("Add", StringComparison.Ordinal), blacklist);
        return true;
    }
}

public sealed class ChatChannelCommand : ITextCommand
{
    public IEnumerable<string> Names => ["c2", "c4", "c5"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 2) return true;
        switch (words[0])
        {
            case "c5":
                if (words.Length > 2) session.Whisper(words[1], string.Join(' ', words.Skip(2)));
                return true;
            case "c2":
                session.Broadcast(ChatChannel.City, string.Join(' ', words.Skip(1)));
                return true;
            default:
                session.Broadcast(ChatChannel.Public, string.Join(' ', words.Skip(1)));
                return true;
        }
    }
}

public sealed class AnswerCommand : ITextCommand
{
    public IEnumerable<string> Names => ["Answer"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 2 || words[1] is not ("Y" or "N")) return false;
        session.Answer(words[1] == "Y");
        return true;
    }
}

public sealed class LeaveGroupCommand : ITextCommand
{
    public IEnumerable<string> Names => ["LeaveGroup"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.Send(UiPackets.CanLeaveGroup(LeaveGroupState.Hidden), "(hide leave button)");
        session.SystemMessage("There is nothing to leave here.");
        return true;
    }
}
