using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int ConsignMarketView = 39;
    public const int ConsignSlotView = 40;
    public const int ConsignMineView = 41;

    private const int ConsignLongestHours = 72;

    private readonly List<int> _consignMarketIds = [];
    private readonly List<int> _consignMineIds = [];
    private (int View, int Index, string Name)? _consignItem;
    private bool _consignOpen;
    private int _consignMarketShown;
    private int _consignMineShown;
    private int _consignPage = 1;
    private string _consignFilter = "";

    public bool ConsignOpen => _consignOpen;

    public void OpenConsign()
    {
        if (!_consignOpen)
        {
            _consignOpen = true;
            _consignMarketShown = 0;
            _consignMineShown = 0;
            _consignPage = 1;
            _consignFilter = "";
            Send(ViewPackets.Create(ConsignMarketView, ConsignDirectory.PageSize), "(consignment market)");
            Send(ViewPackets.Create(ConsignSlotView, 1), "(consignment slot)");
            Send(ViewPackets.Create(ConsignMineView, ConsignDirectory.ListingsPerSeller), "(own consignments)");
            Send(UiPackets.Custom($"POSTSELL_PAYTYPE {ConsignDirectory.MoneyPayType}"), "(consignment pay type)");
        }
        CollectConsignment();
        SendConsignMine();
        SendConsignMarket();
        Log.Info(Tag, $"{Name} opens the consignment market");
    }

    public void CloseConsign()
    {
        if (!_consignOpen) return;
        _consignOpen = false;
        _consignItem = null;
        _consignMarketIds.Clear();
        _consignMineIds.Clear();
        _consignMarketShown = 0;
        _consignMineShown = 0;
        foreach (int view in new[] { ConsignMarketView, ConsignSlotView, ConsignMineView })
            Send(ViewPackets.Delete(view), "(close consignment)");
        Send(UiPackets.Custom($"ct {ConsignTables.Market}"), "(close consignment)");
        Send(UiPackets.Custom($"ct {ConsignTables.Mine}"), "(close consignment)");
    }

    public bool ConsignSlotClick()
    {
        if (!_consignOpen) return false;
        if (Hand != null && HandSource() is { } from)
        {
            ItemStack item = MailBagOf(from.View)![from.Index];
            DropHand();
            ShowConsignSlot(from.View, from.Index, item);
            return true;
        }
        DropHand();
        ClearConsignSlot();
        return true;
    }

    public void ConsignSell(int payType, int price, int hours, string password)
    {
        if (!_consignOpen) return;
        if (_consignItem is not { } slot || MailBagOf(slot.View) is not { } bag || !bag.TryGetValue(slot.Index, out ItemStack? item)
            || item.Name != slot.Name)
        {
            ClearConsignSlot();
            SystemMessage("Put the article you want to sell into the consignment slot first.", SystemMessageKind.Prompt);
            return;
        }
        if (payType != ConsignDirectory.MoneyPayType)
        {
            SystemMessage("Only silver coins are accepted at this market.", SystemMessageKind.Prompt);
            return;
        }
        if (price <= 0 || price > Rule.Economy.SilverCap)
        {
            SystemMessage("Enter a valid price.", SystemMessageKind.Prompt);
            return;
        }
        if (ConsignDirectory.Of(Context, Name).Count >= ConsignDirectory.ListingsPerSeller)
        {
            SystemMessage($"You can consign at most {ConsignDirectory.ListingsPerSeller} articles at a time.", SystemMessageKind.Prompt);
            return;
        }
        int fee = ConsignDirectory.Fee(price);
        if (Money < fee)
        {
            SystemMessage($"Consigning this article costs a fee of {fee} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        ItemStack consigned = item.Clone();
        TakeFrom(slot.View, slot.Index, item.Amount, "(article consigned)");
        SetMoney(Money - fee);
        ClearConsignSlot();
        ConsignListing listing = ConsignDirectory.Add(Context, Name, consigned, price, payType,
            Math.Clamp(hours, 1, ConsignLongestHours), password.Trim());
        RefreshLoad();
        Save();
        SendConsignMine();
        SendConsignMarket();
        SystemMessage($"Your article is on sale for {price} silver coins (fee {fee}).");
        Log.Info(Tag, $"{Name} consigns {consigned.Amount} x {consigned.Name} for {price} (listing {listing.Id})");
    }

    public void ConsignUnSell(int index, bool reprice)
    {
        if (!_consignOpen) return;
        if (index < 1 || index > _consignMineIds.Count) return;
        ConsignListing? listing = ConsignDirectory.Find(Context, _consignMineIds[index - 1]);
        if (listing == null || !listing.Seller.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            SendConsignMine();
            return;
        }
        if (!GiveItem(listing.Item.Clone(), "(consignment withdrawn)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        ConsignDirectory.Remove(Context, listing.Id);
        RefreshLoad();
        Save();
        SendConsignMine();
        SendConsignMarket();
        if (reprice && FindInBags(listing.Item.Name) is { } back)
        {
            ShowConsignSlot(back.View, back.Index, MailBagOf(back.View)![back.Index]);
            SystemMessage("Set a new price and put the article up for sale again.");
        }
        Log.Info(Tag, $"{Name} withdraws consignment {listing.Id} ({listing.Item.Amount} x {listing.Item.Name})");
    }

    public void ConsignBuy(int index, int payType)
    {
        if (!_consignOpen) return;
        int number = index >= 1 && index <= _consignMarketIds.Count ? index : index + 1;
        if (number < 1 || number > _consignMarketIds.Count) return;
        ConsignListing? listing = ConsignDirectory.Find(Context, _consignMarketIds[number - 1]);
        if (listing == null)
        {
            SystemMessage("That article has already been sold.", SystemMessageKind.Prompt);
            SendConsignMarket();
            return;
        }
        if (listing.Seller.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            SystemMessage("You cannot buy your own article.", SystemMessageKind.Prompt);
            return;
        }
        Log.Debug(Tag, $"{Name} asks for consignment {listing.Id} (pay flag {payType})");
        if (Money < listing.Price)
        {
            SystemMessage(CannotAfford, SystemMessageKind.Prompt);
            return;
        }
        if (!GiveItem(listing.Item.Clone(), "(consignment bought)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        ConsignDirectory.Remove(Context, listing.Id);
        SetMoney(Money - listing.Price);
        ConsignDirectory.Credit(Context, listing.Seller, listing.Price);
        RefreshLoad();
        Save();
        SendConsignMarket();
        SystemMessage($"You bought {listing.Item.Amount} article(s) from {listing.Seller} for {listing.Price} silver coins.");
        Log.Info(Tag, $"{Name} buys consignment {listing.Id} ({listing.Item.Amount} x {listing.Item.Name}) from {listing.Seller} for {listing.Price}");
        ClientSession? seller = Context.World.Sessions.FirstOrDefault(other => other.InWorld
            && other.Name.Equals(listing.Seller, StringComparison.OrdinalIgnoreCase));
        if (seller == null) return;
        seller.SystemMessage($"{Name} bought your consigned article for {listing.Price} silver coins.");
        seller.CollectConsignment();
        if (seller._consignOpen) seller.SendConsignMine();
    }

    public void ConsignSearch(string[] criteria)
    {
        if (!_consignOpen) return;
        _consignFilter = ConsignFilter(criteria);
        _consignPage = 1;
        SendConsignMarket();
    }

    public void ConsignTurnPage(int step, string[] criteria)
    {
        if (!_consignOpen) return;
        string filter = ConsignFilter(criteria);
        if (filter != _consignFilter)
        {
            _consignFilter = filter;
            _consignPage = 1;
        }
        else
        {
            _consignPage = Math.Max(1, _consignPage + step);
        }
        SendConsignMarket();
    }

    public void ConsignShowAll()
    {
        if (!_consignOpen) return;
        _consignFilter = "";
        _consignPage = 1;
        SendConsignMarket();
    }

    public int CollectConsignment()
    {
        int handed = 0;
        long owed = ConsignDirectory.TakeProceeds(Context, Name);
        if (owed > 0)
        {
            long pay = Math.Min(owed, (long)Rule.Economy.SilverCap - Money);
            if (pay > 0)
            {
                SetMoney(Money + pay);
                SystemMessage($"You receive {pay} silver coins from the consignment market.");
                handed++;
            }
            if (owed > pay) ConsignDirectory.Credit(Context, Name, owed - pay);
        }
        foreach (ConsignListing listing in ConsignDirectory.TakeReturns(Context, Name))
        {
            if (GiveItem(listing.Item.Clone(), "(consignment returned)") || WarehouseStore(listing.Item.Clone()))
            {
                SystemMessage("An unsold consignment was returned to you.");
                handed++;
                continue;
            }
            ConsignDirectory.KeepReturn(Context, listing);
        }
        if (handed > 0)
        {
            RefreshLoad();
            Save();
        }
        return handed;
    }

    private void SendConsignMine()
    {
        List<ConsignListing> mine = ConsignDirectory.Of(Context, Name).Take(ConsignDirectory.ListingsPerSeller).ToList();
        _consignMineShown = RenderConsign(ConsignMineView, ConsignTables.Mine, ConsignTables.MineColumns, mine, _consignMineIds, _consignMineShown);
    }

    private void SendConsignMarket()
    {
        List<ConsignListing> found = ConsignDirectory.Search(Context, _consignFilter);
        int pages = Math.Max(1, (found.Count + ConsignDirectory.PageSize - 1) / ConsignDirectory.PageSize);
        _consignPage = Math.Clamp(_consignPage, 1, pages);
        List<ConsignListing> page = found.Skip((_consignPage - 1) * ConsignDirectory.PageSize).Take(ConsignDirectory.PageSize).ToList();
        _consignMarketShown = RenderConsign(ConsignMarketView, ConsignTables.Market, ConsignTables.MarketColumns, page, _consignMarketIds, _consignMarketShown);
        if (PropertyTable.Has("PsSearchPage"))
            SetProperty("PsSearchPage", PropertyTable.Parse("PsSearchPage", $"{_consignPage}/{pages}") ?? _consignPage);
    }

    private int RenderConsign(int view, int table, int columns, List<ConsignListing> listings, List<int> ids, int shown)
    {
        for (int slot = 1; slot <= shown; slot++) Send(ViewPackets.RemoveItem(view, slot), "(consignment slot cleared)");
        Send(UiPackets.Custom($"ct {table}"), "(consignment rows cleared)");
        ids.Clear();
        var rows = new List<string[]>();
        for (int i = 0; i < listings.Count; i++)
        {
            ConsignListing listing = listings[i];
            ids.Add(listing.Id);
            Send(ViewPackets.AddItem(view, i + 1, ItemProperties(listing.Item, i + 1, listing.Price)), $"(consignment {listing.Item.Name})");
            string[] row =
            [
                (i + 1).ToString(), GuildPackets.Cell(listing.Item.Name), GuildPackets.Cell(listing.Seller), listing.Price.ToString(),
                GuildPackets.Cell(ConsignDirectory.TimeLeft(listing)),
                listing.Password.Length == 0 ? ConsignTables.NoPassword : GuildPackets.Cell(listing.Password),
                listing.PayType.ToString(),
            ];
            rows.Add(row.Take(columns).ToArray());
        }
        foreach (string[][] chunk in rows.Chunk(ConsignTables.RowsPerMessage))
        {
            string cells = string.Concat(chunk.SelectMany(row => row).Select(cell => UiPackets.FieldSeparator + cell));
            Send(UiPackets.Custom($"ut {table}{cells}"), "(consignment rows)");
        }
        return listings.Count;
    }

    private void ShowConsignSlot(int view, int index, ItemStack item)
    {
        if (_consignItem != null) Send(ViewPackets.RemoveItem(ConsignSlotView, 1), "(consignment slot)");
        _consignItem = (view, index, item.Name);
        Send(ViewPackets.AddItem(ConsignSlotView, 1, ItemProperties(item, 1)), "(consignment slot)");
    }

    private void ClearConsignSlot()
    {
        if (_consignItem == null) return;
        _consignItem = null;
        if (_consignOpen) Send(ViewPackets.RemoveItem(ConsignSlotView, 1), "(consignment slot)");
    }

    private (int View, int Index)? FindInBags(string name)
    {
        foreach (int view in BboBagViews)
        {
            int slot = MailBagOf(view)!.FirstOrDefault(pair => pair.Value.Name == name).Key;
            if (slot != 0) return (view, slot);
        }
        return null;
    }

    private static string ConsignFilter(string[] criteria)
    {
        if (criteria.Length == 0) return "";
        int skip = criteria[0] == "1" ? 4 : criteria[0] == "0" ? 1 : 0;
        return string.Join(' ', criteria.Skip(skip)).Trim();
    }
}
