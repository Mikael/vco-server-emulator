using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class GuildCopyCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csApplyConcopy", "csAgreeConCopyApply", "csAskConCopyApplyList", "csConsCopyCommit", "csJoinConsCopy", "csJoinConsCopy2",
        "csConsCopyGiveUp", "csConsCopyGame", "RefuseConsCopy",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csApplyConcopy":
                session.ApplyGuildCopy();
                return true;
            case "csAgreeConCopyApply" when words.Length >= 2:
                foreach (string name in words.Skip(1)) session.ApproveGuildCopy(name);
                return true;
            case "csAskConCopyApplyList":
                session.AskGuildCopyApplicants();
                return true;
            case "csConsCopyCommit":
                session.CommitGuildCopy();
                return true;
            case "csJoinConsCopy" or "csJoinConsCopy2":
                session.JoinGuildCopy();
                return true;
            case "csConsCopyGiveUp":
                session.LeaveGuildCopy();
                return true;
            case "csConsCopyGame":
                session.GuildCopyGame(words);
                return true;
            case "RefuseConsCopy":
                return true;
            default:
                return false;
        }
    }
}
