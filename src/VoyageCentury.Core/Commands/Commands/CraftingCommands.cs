using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands.Commands;

public sealed class CraftingCommands : ITextCommand
{
    public IEnumerable<string> Names => ["QMI", "MakingItem"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length > 2) return false;
        string name = words.Length == 2 ? words[1] : "";
        if (words[0] == "QMI")
        {
            session.QueryRecipe(name);
            return true;
        }
        if (name.Length == 0) return false;
        session.Craft(name);
        return true;
    }
}
