using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ExchangeCommand : ITextCommand
{
    public IEnumerable<string> Names => ["Exchange"];

    public bool Execute(ClientSession session, string[] words)
    {
        World world = session.Context.World;
        ClientSession? target = words.Length > 1
            ? world.FindPlayer(words[1])
            : world.Sessions.FirstOrDefault(other => other.ObjectId == session.ExchangeTargetId && other.InWorld);
        session.RequestTrade(target);
        return true;
    }
}

public sealed class SwitchExchangeCommand : ITextCommand
{
    public IEnumerable<string> Names => ["Switch"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 2) return false;
        switch (words[1])
        {
            case "Exchange":
                session.SwitchExchange();
                return true;
            case "Team":
                session.SwitchTeam();
                return true;
            default:
                return false;
        }
    }
}

public sealed class TradeCommands : ITextCommand
{
    public IEnumerable<string> Names => ["SetExchange", "SureExchange", "CancelSureExchange", "DoExchange", "CancelExchange", "ExchangeMoney"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "SetExchange":
                if (Arguments.Numbers(words, 3, out int[] offer)) session.OfferTradeItem(offer[0], offer[1], offer[2]);
                else if (Arguments.Numbers(words, 2, out offer)) session.OfferTradeItem(offer[0], offer[1], 0);
                else return false;
                return true;
            case "SureExchange":
                session.SetTradeReady(true);
                return true;
            case "CancelSureExchange":
                session.SetTradeReady(false);
                return true;
            case "DoExchange":
                session.ConfirmTrade();
                return true;
            case "CancelExchange":
                session.CancelTrade();
                return true;
            case "ExchangeMoney" when Arguments.Numbers(words, 1, out int[] money):
                session.OfferTradeMoney(money[0]);
                return true;
            default:
                return false;
        }
    }
}
