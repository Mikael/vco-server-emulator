using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class SystemCommands : ITextCommand
{
    private static readonly HashSet<string> Acknowledged =
    [
        "csSetVisualPlayers", "csBuffTable", "ASkTable39", "csWindow_ObjFactory_AskInit", "csAskRole",
        "csCloseNewArmUpdate", "csCloseFlagView", "AskTeamPos", "csAskPaleBoss", "csHideSailorBarWnd",
    ];

    private static readonly Dictionary<string, int> ChatCommands = new()
    {
        ["c12"] = BboChatKind.Popular,
        ["c13"] = BboChatKind.Newbie,
        ["c15"] = BboChatKind.Trumpet,
    };

    public IEnumerable<string> Names => [.. Acknowledged, .. ChatCommands.Keys, "csNetPing", "csMACAddress", "csAskVisibleTable"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (ChatCommands.TryGetValue(words[0], out int kind))
        {
            session.HudChat(kind, string.Join(' ', words.Skip(1)));
            return true;
        }
        switch (words[0])
        {
            case "csNetPing":
                session.AnswerPing(words.Length > 1 ? words[1] : "0");
                return true;
            case "csMACAddress":
                Log.Debug(session.Tag, $"{session.Name} reports a network address");
                return true;
            case "csAskVisibleTable":
                session.SendVisibleTable(words.Length > 1 ? words[1] : "");
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} client service command: {string.Join(' ', words)}");
                return true;
        }
    }
}
