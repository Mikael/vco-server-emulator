using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class FireFlagCommands : ITextCommand
{
    public IEnumerable<string> Names =>
        ["csOpenFlagView", "csCloseFlagView", "csClearFlagView", "csStartForge", "csForgeFlag", "csRemakeFlag"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csOpenFlagView":
                session.OpenFlagView();
                return true;
            case "csCloseFlagView":
                session.CloseFlagView();
                return true;
            case "csClearFlagView":
                session.ClearFlagView();
                return true;
            case "csStartForge" when words.Length >= 2:
                session.StartForge(words[1], words.Length > 2 ? words[2] : words[1]);
                return true;
            case "csForgeFlag" when words.Length >= 2:
                session.ForgeFlag(words[1], words.Length > 2 ? words[2] : words[1]);
                return true;
            case "csRemakeFlag" when words.Length >= 5 && int.TryParse(words[3], out int oldProp) && int.TryParse(words[4], out int newProp):
                session.RemakeFlag(words[1], words[2], oldProp, newProp);
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} fire flag command ignored: {string.Join(' ', words)}");
                return true;
        }
    }
}

public sealed class BeliefFuseCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csFuseStart", "csFuseSave", "csFuseCancel", "csFuseClose"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csFuseStart" when words.Length >= 2:
                session.StartBeliefFuse(words[1]);
                return true;
            case "csFuseSave":
                session.SaveBeliefFuse();
                return true;
            case "csFuseCancel":
                session.CancelBeliefFuse();
                return true;
            case "csFuseClose":
                session.CloseBeliefFuse();
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} faith fusion command ignored: {string.Join(' ', words)}");
                return true;
        }
    }
}
