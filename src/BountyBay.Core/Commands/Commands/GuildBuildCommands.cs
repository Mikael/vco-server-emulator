using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class GuildBuildCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csAskConsActDetail", "csAskConsBuildInfo", "csAskConsLog"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAskConsActDetail":
                session.AskGuildActivity(BboArguments.Rest(words, 1));
                return true;
            case "csAskConsBuildInfo":
                session.AskGuildConstruction(BboArguments.Rest(words, 1));
                return true;
            case "csAskConsLog":
                session.AskGuildLog(BboArguments.Last(words, out int page) ? page : 0);
                return true;
            default:
                return false;
        }
    }
}
