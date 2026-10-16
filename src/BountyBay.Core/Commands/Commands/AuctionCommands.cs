using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class AuctionCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csAskAuction", "csAuction", "csImpawnBargain", "csImpawnDone", "csImpawnCancel", "csCloseImpawn"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAskAuction":
                session.AskAuction();
                return true;
            case "csAuction" when words.Length >= 2 && int.TryParse(words[1], out int amount):
                session.AuctionBid(amount);
                return true;
            case "csAuction":
                Log.Debug(session.Tag, $"{session.Name} sends a malformed bid: {string.Join(' ', words)}");
                session.RefreshAuction();
                return true;
            case "csImpawnBargain":
                session.ImpawnBargain();
                return true;
            case "csImpawnDone":
                session.ImpawnDone();
                return true;
            case "csImpawnCancel":
                session.ImpawnCancel();
                return true;
            case "csCloseImpawn":
                session.CloseImpawn();
                return true;
            default:
                return false;
        }
    }
}
