using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class CopyCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csGroupToCopy", "csLeaveCopyList", "csAskQueueST", "csAskCopyInfo", "csSelectRole", "csAskRole", "ShowCopyInfo",
        "csInvitePlayer", "csConscribeTeam", "csConsTeamCancel", "csAskSkillNameOption",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        int Number(int index) => words.Length > index && int.TryParse(words[index], out int value) ? value : -1;
        switch (words[0])
        {
            case "csGroupToCopy" when words.Length >= 4:
                session.InstanceJoinQueue(Number(1), Number(2), Number(3), string.Join(' ', words.Skip(4)).Replace(UiPackets.Blank, ' '));
                return true;
            case "csLeaveCopyList" when words.Length >= 3:
                session.InstanceLeaveQueue(Number(1), Number(2));
                return true;
            case "csAskQueueST":
                session.InstanceSendQueue();
                return true;
            case "csAskCopyInfo" when words.Length >= 3:
                session.InstanceQueueInfo(Number(1), Number(2));
                return true;
            case "csSelectRole" when words.Length >= 2:
                session.InstanceSelectRole(Number(1));
                return true;
            case "ShowCopyInfo":
                session.InstanceShowLeftTimes();
                return true;
            case "csInvitePlayer" when words.Length >= 2:
                TeamDirectory.Invite(session, words[1].Replace(UiPackets.Blank, ' '));
                return true;
            case "csConscribeTeam" when words.Length >= 3:
                session.InstanceRecruit(Number(1), Number(2));
                return true;
            case "csConsTeamCancel":
                session.InstanceStopRecruiting();
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} instance notice: {string.Join(' ', words)}");
                return true;
        }
    }
}
