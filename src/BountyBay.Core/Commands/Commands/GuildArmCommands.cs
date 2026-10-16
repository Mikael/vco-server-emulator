using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class GuildArmCommands : ITextCommand
{
    public IEnumerable<string> Names =>
        ["csMakeConsArm", "csSplitConsArm", "csRebindConsArm", "csUpLevelConsArm", "csViewConsArmUp", "csCloseConsArm"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csMakeConsArm" when words.Length >= 3 && int.TryParse(words[1], out int kind) && int.TryParse(words[2], out int mode):
                session.MakeGuildArm(kind, mode);
                return true;
            case "csSplitConsArm":
                session.SplitGuildArm();
                return true;
            case "csRebindConsArm":
                session.RebindGuildArm();
                return true;
            case "csUpLevelConsArm":
                session.UpgradeGuildArm();
                return true;
            case "csViewConsArmUp":
                session.ViewGuildArmUpgrade(BboArguments.Last(words, out int type) ? type : 0);
                return true;
            case "csCloseConsArm":
                session.CloseGuildArmWindow(BboArguments.Last(words, out int closed) ? closed : 1);
                return true;
            default:
                return false;
        }
    }
}
