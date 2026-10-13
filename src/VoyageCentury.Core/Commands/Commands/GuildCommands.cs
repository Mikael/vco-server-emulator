using VoyageCentury.Core.Protocol.Packets;
using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands.Commands;

public sealed class GuildCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "CreateCons", "JoinCons", "ProcessConsApply", "ConsFireOut", "ConsQuit", "ConsDismiss", "CloseConsApply",
        "SetConsTenet", "SetConsDuty", "TransferConsProffer", "ApplyConsDept", "CreateConsDept", "SetConsDept",
        "CloseConsDept", "ConsRelationCmd", "ChangeConsWarState", "BeforeConsBid", "ReferConsTaskItem",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "CreateCons" when words.Length >= 3 && int.TryParse(words[2], out int flag):
                GuildDirectory.Create(session, words[1], flag, Rest(words, 3));
                return true;
            case "JoinCons" when words.Length == 2:
                GuildDirectory.Apply(session, words[1]);
                return true;
            case "ProcessConsApply" when words.Length == 3 && words[2] is "0" or "1":
                GuildDirectory.ProcessApplication(session, words[1], words[2] == "1");
                return true;
            case "ConsFireOut" when words.Length == 2:
                GuildDirectory.Expel(session, words[1]);
                return true;
            case "ConsQuit":
                GuildDirectory.Quit(session);
                return true;
            case "ConsDismiss":
                GuildDirectory.Dismiss(session);
                return true;
            case "CloseConsApply" when words.Length == 2 && words[1] is "0" or "1":
                GuildDirectory.SetApplyClosed(session, words[1] == "1");
                return true;
            case "SetConsTenet":
                GuildDirectory.SetTenet(session, Rest(words, 1));
                return true;
            case "SetConsDuty" when words.Length == 3 && int.TryParse(words[2], out int duty):
                GuildDirectory.SetDuty(session, words[1], duty);
                return true;
            case "TransferConsProffer" when words.Length == 3 && int.TryParse(words[2], out int amount):
                GuildDirectory.TransferContribution(session, words[1], amount);
                return true;
            case "ApplyConsDept" when words.Length >= 2:
                GuildDirectory.ApplyDepartment(session, words[1], Rest(words, 2));
                return true;
            case "CreateConsDept" when words.Length == 4 && words[3] is "0" or "1":
                GuildDirectory.DecideDepartment(session, words[1], words[2], words[3] == "1");
                return true;
            case "SetConsDept" when words.Length is 2 or 3:
                GuildDirectory.SetDepartment(session, words[1], words.Length == 3 ? words[2] : null);
                return true;
            case "CloseConsDept" when words.Length == 2:
                GuildDirectory.CloseDepartment(session, words[1]);
                return true;
            case "ConsRelationCmd" when words.Length == 3 && int.TryParse(words[1], out int command):
                GuildDirectory.Relate(session, command, words[2]);
                return true;
            case "ChangeConsWarState":
                session.SystemMessage("Guild battles are not available on this server.");
                return true;
            case "BeforeConsBid" or "ReferConsTaskItem":
                session.SystemMessage("Guild tasks are not available on this server.");
                return true;
            default:
                return false;
        }
    }

    private static string Rest(string[] words, int start) => string.Join(' ', words.Skip(start));
}

public sealed class GuildChatCommand : ITextCommand
{
    public IEnumerable<string> Names => ["c6", "c7", "c8", "c9"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 2) return true;
        ChatChannel channel = words[0] switch
        {
            "c6" => ChatChannel.Guild,
            "c7" => ChatChannel.GuildNotice,
            "c8" => ChatChannel.Department,
            _ => ChatChannel.DepartmentNotice,
        };
        string text = string.Join(' ', words.Skip(1));
        string interval = channel switch
        {
            ChatChannel.Guild => "guild",
            ChatChannel.GuildNotice => "guildnotice",
            ChatChannel.Department => "department",
            _ => "departmentnotice",
        };
        if (session.ChatAllowed(interval, ref text)) GuildDirectory.Chat(session, channel, text);
        return true;
    }
}
