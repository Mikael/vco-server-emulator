using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ViewArmCommands : ITextCommand
{
    public IEnumerable<string> Names => ["QueryArms", "csViewOtherArm", "csQueryWearInfo"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "QueryArms":
                session.QueryArms();
                return true;
            case "csViewOtherArm" when words.Length == 3 && int.TryParse(words[1], out int view) && int.TryParse(words[2], out int slot):
                session.ViewOtherArm(view, slot);
                return true;
            case "csQueryWearInfo" when words.Length == 3 && int.TryParse(words[1], out int view) && int.TryParse(words[2], out int index):
                session.QueryWearInfo(view, index);
                return true;
            default:
                return false;
        }
    }
}
