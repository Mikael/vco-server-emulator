using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ContrabandCommands : ITextCommand
{
    public IEnumerable<string> Names => ["Contraband"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 3 || !int.TryParse(words[1], out int view) || !int.TryParse(words[2], out int index)) return false;
        session.SellContraband(view, index);
        return true;
    }
}
