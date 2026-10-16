using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class PetWhistleCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csOpenWhistle", "csShowWhistle", "csSwitchWhistle"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csOpenWhistle":
                session.OpenWhistles();
                return true;
            case "csSwitchWhistle" when words.Length >= 2 && int.TryParse(words[1], out int tab):
                session.SwitchWhistleTab(tab);
                return true;
            case "csShowWhistle" when words.Length >= 3 && int.TryParse(words[1], out int page) && int.TryParse(words[2], out int index):
                session.ShowWhistle(page, index);
                return true;
            default:
                return false;
        }
    }
}
