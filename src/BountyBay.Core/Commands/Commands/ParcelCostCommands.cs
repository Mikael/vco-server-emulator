using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ParcelCostCommands : ITextCommand
{
    public IEnumerable<string> Names => ["QuerySendCost", "SendItem"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "QuerySendCost" when words.Length == 3 && int.TryParse(words[1], out int view) && int.TryParse(words[2], out int index):
                session.QueryParcelCost(view, index);
                return true;
            case "SendItem" when words.Length >= 5 && int.TryParse(words[1], out int view) && int.TryParse(words[2], out int index)
                && int.TryParse(words[3], out int amount):
                session.SendParcelWithPostage(view, index, amount, words[4], string.Join(' ', words.Skip(5)));
                return true;
            default:
                return false;
        }
    }
}
