using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class BboSelectCommand : ITextCommand
{
    private readonly SelectCommand _classic = new();

    public IEnumerable<string> Names => ["Select", "DirectSelect"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 4 || !int.TryParse(words[1], out int view) || !int.TryParse(words[2], out int how)
            || !int.TryParse(words[3], out int index)) return false;
        string? extra = words.Length > 4 ? string.Join(' ', words.Skip(4)) : null;
        switch (view)
        {
            case ClientSession.LootView:
                return session.PickLoot(index, extra is null or "1" ? null : extra) || Quiet(session, words);
            case ClientSession.GlobalCollectView:
                session.CollectGlobal(index);
                return true;
            case ClientSession.ConsignSlotView when session.ConsignSlotClick():
                return true;
            case ClientSession.ShopBuyBackView when session.AskBuyBack(index):
                return true;
            case ClientSession.ImpawnView when session.ImpawnSlotClick(index):
                return true;
            case ClientSession.CompoundView:
                session.CompoundSlot(index);
                return true;
        }
        string[] classic = [words[0], words[1], words[2], words[3]];
        if (session.OnTradeSelect(view, how, index) || session.OnStallSelect(view, how, index)
            || session.OnWarehouseSelect(view, how, index)) return true;
        if (session.BagSelect(view, how, index)) return true;
        return _classic.Execute(session, classic);
    }

    private static bool Quiet(ClientSession session, string[] words)
    {
        Log.Debug(session.Tag, $"{session.Name}: {string.Join(' ', words)} (no spoils open)");
        return true;
    }
}

public sealed class BboSellCommand : ITextCommand
{
    public IEnumerable<string> Names => ["Sell"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (!Arguments.Numbers(words, 4, out int[] values)) return false;
        if (values[0] == ViewId.Booth)
        {
            session.SellToStall(values[1], values[2], values[3]);
            return true;
        }
        session.SellAndRemember(values[0], values[1], values[2], values[3]);
        return true;
    }
}

public sealed class BboBuyCommand : ITextCommand
{
    private readonly BuyCommand _classic = new();

    public IEnumerable<string> Names => ["Buy"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (!Arguments.Numbers(words, 3, out int[] values)) return false;
        if (values[0] == ClientSession.ShopBuyBackView && session.BuyBack(values[1], values[2])) return true;
        return _classic.Execute(session, words);
    }
}

public sealed class BboTakeUpCommand : ITextCommand
{
    public IEnumerable<string> Names => ["TakeUp"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (!Arguments.Numbers(words, 3, out int[] values)) return false;
        if (values[0] == ViewId.Warehouse)
        {
            session.WarehouseTake(values[1], values[2]);
            return true;
        }
        session.PickToHand(values[0], values[1], values[2]);
        return true;
    }
}

public sealed class HandToCommand : ITextCommand
{
    public IEnumerable<string> Names => ["HandTo", "Chuck"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "HandTo" when words.Length == 2 && words[1] is "i" or "a" or "I" or "A":
                session.HandTo(words[1]);
                return true;
            case "Chuck":
                session.Chuck(words.Length == 2 && words[1] == "sure");
                return true;
            default:
                return false;
        }
    }
}

public sealed class BagServiceCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "RefreshViewport", "csExpandLock", "ExpandContainer", "csExpandCapacity", "UseTo", "UseToBoxItem", "SellCard",
        "NpcPassWord_Get", "NpcPassWord_Change",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "RefreshViewport" when Arguments.Numbers(words, 1, out int[] tab):
                session.SortBag(tab[0]);
                return true;
            case "csExpandLock" when Arguments.Numbers(words, 2, out int[] cells):
                session.ExpandBag(cells[0], cells[1]);
                return true;
            case "ExpandContainer" when Arguments.Numbers(words, 2, out int[] row):
                session.ExpandContainer(row[0], row[1]);
                return true;
            case "UseTo" when Arguments.Numbers(words, 2, out int[] use):
                session.UseOnTarget(use[0], use[1], session.ExchangeTargetId);
                return true;
            case "UseToBoxItem" when Arguments.Numbers(words, 4, out int[] box):
                session.UseOnBoxItem(box[0], box[1], box[2], box[3]);
                return true;
            case "csExpandCapacity":
                session.ExpandBag(words.Length > 1 && int.TryParse(words[1], out int view) ? view : 0, 0);
                return true;
            case "SellCard":
                session.SystemMessage("Point cards cannot be traded here.", SystemMessageKind.Prompt);
                Log.Info(session.Tag, $"{session.Name} tries to trade a point card ({words.Length - 1} argument(s))");
                return true;
            case "NpcPassWord_Get" or "NpcPassWord_Change":
                session.SystemMessage("Storage passwords are not used here.", SystemMessageKind.Prompt);
                Log.Info(session.Tag, $"{session.Name} sends {words[0]}");
                return true;
            default:
                return false;
        }
    }
}

public sealed class ConsignCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "AskPostSellOpen", "AskPostSellClose", "AskPostSellPageSell", "AskPostSellPageSearch", "AskPostSellSell",
        "AskPostSellUnSell", "AskPostSellModify", "AskPostSellBuy", "AskPostSellSearch", "AskPostSellPrev", "AskPostSellNext",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "AskPostSellOpen":
                session.OpenConsign();
                return true;
            case "AskPostSellClose":
                session.CloseConsign();
                return true;
            case "AskPostSellPageSell":
                if (!session.ConsignOpen) session.OpenConsign();
                return true;
            case "AskPostSellPageSearch":
                session.ConsignShowAll();
                return true;
            case "AskPostSellSell" when words.Length >= 4 && int.TryParse(words[1], out int payType)
                && int.TryParse(words[2], out int price) && int.TryParse(words[3], out int hours):
                session.ConsignSell(payType, price, hours, string.Join(' ', words.Skip(4)));
                return true;
            case "AskPostSellUnSell" when Arguments.Numbers(words, 1, out int[] withdrawn):
                session.ConsignUnSell(withdrawn[0], reprice: false);
                return true;
            case "AskPostSellModify" when Arguments.Numbers(words, 1, out int[] repriced):
                session.ConsignUnSell(repriced[0], reprice: true);
                return true;
            case "AskPostSellBuy" when Arguments.Numbers(words, 2, out int[] bought):
                session.ConsignBuy(bought[0], bought[1]);
                return true;
            case "AskPostSellSearch":
                session.ConsignSearch(words.Skip(1).ToArray());
                return true;
            case "AskPostSellPrev":
                session.ConsignTurnPage(-1, words.Skip(1).ToArray());
                return true;
            case "AskPostSellNext":
                session.ConsignTurnPage(1, words.Skip(1).ToArray());
                return true;
            default:
                return false;
        }
    }
}

public sealed class CompoundCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csAppartusStart", "csAppartusClose", "csAppartusCancel", "csAppartusSave"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAppartusStart" when words.Length >= 2:
                session.StartCompound(words[1]);
                return true;
            case "csAppartusClose":
                session.CloseCompound();
                return true;
            case "csAppartusCancel":
                session.CompoundSlot(1);
                session.CompoundSlot(2);
                return true;
            case "csAppartusSave":
                session.CompoundSlot(2);
                return true;
            default:
                return false;
        }
    }
}

public sealed class TotemFuseCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "SetFuseTotem", "DoFuse", "SureFuse", "CancelFuse", "CancelSureFuse", "csStartAloneFuse", "csCloseFuseAlone",
        "csTotemTrans", "csTotemTransClose",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words[0] is "DoFuse" or "SureFuse" or "csStartAloneFuse" or "csTotemTrans")
            session.SystemMessage("Totems cannot be fused here yet.", SystemMessageKind.Prompt);
        Log.Info(session.Tag, $"{session.Name} totem command: {string.Join(' ', words)}");
        return true;
    }
}
