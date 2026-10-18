using System.Text;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class StallEntry
{
    public bool Buying { get; init; }
    public int BagSlot { get; init; }
    public string Name { get; init; } = "";
    public int Photo { get; init; }
    public int Amount { get; set; }
    public int Done { get; set; }
    public int Price { get; set; }

    public int Remaining => Amount - Done;
}

public sealed partial class ClientSession
{
    public const int StallView = 14;
    public const int StallSlots = 18;
    public const int StallState = 21;
    private const int FirstStallModel = 4002031;
    public const int StallStyles = 19;

    private int StallModel => FirstStallModel + Math.Clamp(Character.StallStyle, 1, StallStyles) - 1;

    public void ChooseStallStyle(string[] words)
    {
        if (words.Length < 2 || !int.TryParse(words[1], out int style) || style < 1 || style > StallStyles)
        {
            SystemMessage($"Your booth style is {Math.Clamp(Character.StallStyle, 1, StallStyles)}. Type /booth 1 to /booth {StallStyles} to pick another look for your stall.");
            return;
        }
        Character.StallStyle = style;
        Save();
        if (StallOpen && PropertyTable.Has("StallID")) SetProperty("StallID", StallModel, visibleToOthers: true);
        SystemMessage($"Booth style {style} chosen.");
    }
    public const int StallBuyEntry = 255;
    public const int StallInfoLength = 64;

    private static readonly object ArticleNamesLock = new();
    private static Dictionary<string, string>? _articleNames;

    private readonly Dictionary<int, StallEntry> _stall = [];
    private bool _stallViewOpen;
    private ClientSession? _browsing;

    public bool StallOpen { get; private set; }

    public bool BrowsingStall => _browsing != null;

    public void EnsureStallView()
    {
        if (_stallViewOpen) return;
        _stallViewOpen = true;
        Send(ViewPackets.Create(StallView, StallSlots), "(stall view)");
    }

    public void StallSell(int view, int index, int amount, int price)
    {
        EnsureStallView();
        if (StallRefused()) return;
        if (price < 0 || amount <= 0)
        {
            SystemMessage("Enter a selling price and a selling amount.");
            return;
        }

        if (view == StallView)
        {
            if (!_stall.TryGetValue(index, out StallEntry? listed) || listed.Buying) return;
            if (!Items.TryGetValue(listed.BagSlot, out ItemStack? stock) || stock.Name != listed.Name)
            {
                RemoveStallEntry(index);
                return;
            }
            if (amount > stock.Amount)
            {
                SystemMessage("You do not have this much amount!");
                return;
            }
            listed.Amount = amount;
            listed.Done = 0;
            listed.Price = price;
            Send(ViewPackets.UpdateItem(StallView, index, StallProperties(listed, index)), "(stall item changed)");
            return;
        }

        if (view != ViewId.BagItems)
        {
            SystemMessage("Only articles from the Item tab can be put on sale.");
            return;
        }
        if (!Items.TryGetValue(index, out ItemStack? item)) return;
        if (amount > item.Amount)
        {
            SystemMessage("You do not have this much amount!");
            return;
        }

        int slot = _stall.FirstOrDefault(pair => !pair.Value.Buying && pair.Value.BagSlot == index).Key;
        if (slot != 0)
        {
            StallEntry same = _stall[slot];
            same.Amount = amount;
            same.Done = 0;
            same.Price = price;
            Send(ViewPackets.UpdateItem(StallView, slot, StallProperties(same, slot)), "(stall item changed)");
            return;
        }
        if (_stall.Values.Count(entry => !entry.Buying) >= Properties.Int("StallSellMax", 4))
        {
            SystemMessage("You have reached the upper limit of the selling amount.");
            return;
        }
        slot = FreeStallSlot();
        if (slot == 0) return;
        var added = new StallEntry { BagSlot = index, Name = item.Name, Photo = item.Photo, Amount = amount, Price = price };
        _stall[slot] = added;
        Send(ViewPackets.AddItem(StallView, slot, StallProperties(added, slot)), "(stall item)");
    }

    public void CheckStallBuy(int index, string typed)
    {
        EnsureStallView();
        if (StallRefused()) return;
        if (index < 1 || index > StallSlots) return;
        string? name = ResolveArticle(typed);
        if (name == null)
        {
            SystemMessage($"There is no article called '{typed.Replace('\u0003', ' ')}'.");
            return;
        }
        if (_stall.TryGetValue(index, out StallEntry? old))
        {
            if (!old.Buying) return;
            RemoveStallEntry(index);
        }
        if (_stall.Values.Count(entry => entry.Buying) >= Properties.Int("StallBuyMax", 4))
        {
            SystemMessage("You have reached the upper limit of the purchased amount.");
            return;
        }
        var wanted = new StallEntry { Buying = true, Name = name, Photo = Context.Data.PhotoOf(name), Amount = 1, Price = Context.Data.PriceOf(name) };
        _stall[index] = wanted;
        Send(ViewPackets.AddItem(StallView, index, StallProperties(wanted, index)), "(stall wanted item)");
    }

    public void StallBuy(int index, int amount, int price)
    {
        EnsureStallView();
        if (StallRefused()) return;
        if (!_stall.TryGetValue(index, out StallEntry? wanted) || !wanted.Buying)
        {
            SystemMessage("Click Check first to verify the article you want to purchase.");
            return;
        }
        if (amount <= 0 || price <= 0)
        {
            SystemMessage("Enter the marked price and the amount of the purchased article.");
            return;
        }
        wanted.Amount = amount;
        wanted.Done = 0;
        wanted.Price = price;
        Send(ViewPackets.UpdateItem(StallView, index, StallProperties(wanted, index)), "(stall wanted item changed)");
    }

    public void CancelStallItem(int index)
    {
        if (StallRefused()) return;
        RemoveStallEntry(index);
    }

    public void StartStall(int repairPrice, string info)
    {
        EnsureStallView();
        if (StallOpen) return;
        if (AtSea)
        {
            SystemMessage("This function cannot be used at sea.");
            return;
        }
        if (Trading)
        {
            SystemMessage("You cannot open a stall while trading.");
            return;
        }
        foreach (int slot in _stall.Where(pair => !StillValid(pair.Value)).Select(pair => pair.Key).ToList())
            RemoveStallEntry(slot);
        if (_stall.Count == 0)
        {
            SystemMessage("Put at least one article on sale or on the purchase list first.");
            return;
        }

        CloseBooth();
        CloseShop();
        SetHand(null);
        _tradeRequested = null;
        if (Mover != null)
        {
            (float x, float z) = Mover.Now();
            MoveTo(x, z);
        }
        info = info.Replace('\u0003', ' ').Trim();
        if (info.Length > StallInfoLength) info = info[..StallInfoLength];
        if (info.Length == 0) info = $"{Name}'s stall";
        StallOpen = true;
        SetProperty("StallInfo", info, visibleToOthers: true);
        SetProperty("SelectState", StallState, visibleToOthers: true);
        if (PropertyTable.Has("StallID")) SetProperty("StallID", StallModel, visibleToOthers: true);
        SetStallRepair(repairPrice);
        Log.Info(Tag, $"{Name} opens a stall: {info}");
    }

    public void StopStall()
    {
        if (!StallOpen) return;
        StallOpen = false;
        SetStallRepair(0);
        foreach (ClientSession visitor in StallVisitors()) visitor.CloseBooth();
        SetProperty("SelectState", 0, visibleToOthers: true);
        if (PropertyTable.Has("StallID")) SetProperty("StallID", 0, visibleToOthers: true);
        SetProperty("StallInfo", "", visibleToOthers: true);
        foreach ((int slot, StallEntry entry) in _stall.ToList())
        {
            if (entry.Remaining <= 0 || !StillValid(entry))
            {
                RemoveStallEntry(slot);
            }
            else if (entry.Done > 0)
            {
                entry.Amount = entry.Remaining;
                entry.Done = 0;
                Send(ViewPackets.UpdateItem(StallView, slot, StallProperties(entry, slot)), "(stall item)");
            }
        }
        Log.Info(Tag, $"{Name} closes the stall");
    }

    public bool OnPlayerClicked(ClientSession target, int modifier)
    {
        if (target == this || !target.StallOpen || !target.InWorld || target.Scene != Scene) return false;
        if (Trading || StallOpen) return true;
        OpenBooth(target);
        return true;
    }

    public bool OnStallSelect(int view, int how, int index)
    {
        if (view == StallView) return true;
        if (view != ViewId.Booth) return false;
        if (how == 0) BuyFromStall(index, 1);
        return true;
    }

    public void BuyFromStall(int index, int amount)
    {
        ClientSession? owner = BoothOwner();
        if (owner == null) return;
        if (!owner._stall.TryGetValue(index, out StallEntry? entry)) return;
        if (entry.Buying)
        {
            SystemMessage("This article is being purchased by the stall. Choose Sell and click the article in your bag.");
            return;
        }
        if (entry.Remaining <= 0)
        {
            SystemMessage("Sold out.");
            return;
        }
        amount = Math.Clamp(amount, 1, entry.Remaining);
        if (!owner.Items.TryGetValue(entry.BagSlot, out ItemStack? stock) || stock.Name != entry.Name || stock.Amount < amount)
        {
            entry.Done = entry.Amount;
            owner.StallProgress(index, entry);
            SystemMessage("Sold out.");
            return;
        }
        long cost = (long)entry.Price * amount;
        if (cost > Money)
        {
            SystemMessage("Not enough money.");
            return;
        }
        if (owner.Money + cost > int.MaxValue)
        {
            SystemMessage($"{owner.Name} cannot carry more money.");
            return;
        }
        if (!HasRoomFor(entry.Name))
        {
            SystemMessage("Your bag is full.");
            return;
        }

        owner.TakeFromBag(entry.BagSlot, amount, "(stall item sold)");
        ReceiveIntoBag(entry.Name, entry.Photo, amount, "(bought from stall)");
        SetMoney(Money - (int)cost);
        owner.SetMoney(owner.Money + (int)cost);
        entry.Done += amount;
        owner.StallProgress(index, entry);
        Save();
        owner.Save();
        owner.SystemMessage($"{Name} bought {amount} of your articles for {cost} gold.");
        ItemObtained(entry.Name, amount, "bought");
        Log.Info(Tag, $"{Name} buys {amount} x {entry.Name} for {cost} from the stall of {owner.Name}");
    }

    public void SellToStall(int view, int index, int amount)
    {
        ClientSession? owner = BoothOwner();
        if (owner == null) return;
        if (view != ViewId.BagItems || !Items.TryGetValue(index, out ItemStack? item)) return;
        (int slot, StallEntry? entry) = owner._stall.FirstOrDefault(pair => pair.Value.Buying && pair.Value.Name == item.Name && pair.Value.Remaining > 0);
        if (entry == null)
        {
            SystemMessage("This stall does not purchase that article.");
            return;
        }
        amount = Math.Clamp(amount, 1, Math.Min(entry.Remaining, item.Amount));
        long cost = (long)entry.Price * amount;
        if (cost > owner.Money)
        {
            SystemMessage($"{owner.Name} does not have enough money.");
            return;
        }
        if (Money + cost > int.MaxValue) return;
        if (!owner.HasRoomFor(item.Name))
        {
            SystemMessage($"The bag of {owner.Name} is full.");
            return;
        }

        string name = item.Name;
        int photo = item.Photo;
        TakeFromBag(index, amount, "(sold to stall)");
        owner.ReceiveIntoBag(name, photo, amount, "(stall purchase)");
        owner.SetMoney(owner.Money - (int)cost);
        SetMoney(Money + (int)cost);
        entry.Done += amount;
        owner.StallProgress(slot, entry);
        Save();
        owner.Save();
        owner.SystemMessage($"{Name} sold you {amount} articles for {cost} gold.");
        Log.Info(Tag, $"{Name} sells {amount} x {name} for {cost} to the stall of {owner.Name}");
    }

    public void CloseBooth()
    {
        if (_browsing == null) return;
        _browsing = null;
        Send(ViewPackets.Delete(ViewId.Booth), "(close booth)");
    }

    private void OpenBooth(ClientSession owner)
    {
        CloseBooth();
        CloseShop();
        _browsing = owner;
        Send(ViewPackets.Create(ViewId.Booth, StallSlots), "(booth view)");
        Send(ViewPackets.Properties(ViewId.Booth, new PropertyBag { ["RepairPrice"] = owner.StallRepairPrice }), "(booth repair price)");
        foreach ((int slot, StallEntry entry) in owner._stall.OrderBy(pair => pair.Key))
            Send(ViewPackets.AddItem(ViewId.Booth, slot, StallProperties(entry, slot)), "(booth item)");
        Log.Debug(Tag, $"{Name} looks at the stall of {owner.Name}");
    }

    private ClientSession? BoothOwner()
    {
        ClientSession? owner = _browsing;
        if (owner == null) return null;
        if (owner.StallOpen && owner.InWorld && owner.Scene == Scene && InWorld) return owner;
        CloseBooth();
        SystemMessage("The stall is closed.");
        return null;
    }

    private List<ClientSession> StallVisitors()
        => Context.World.Sessions.Where(other => other._browsing == this).ToList();

    private void StallProgress(int slot, StallEntry entry)
    {
        var update = new PropertyBag { ["AmountStall"] = entry.Done };
        Send(ViewPackets.UpdateItem(StallView, slot, update), "(stall progress)");
        foreach (ClientSession visitor in StallVisitors())
            visitor.Send(ViewPackets.UpdateItem(ViewId.Booth, slot, update), "(booth progress)");
    }

    private void RemoveStallEntry(int slot)
    {
        if (!_stall.Remove(slot)) return;
        Send(ViewPackets.RemoveItem(StallView, slot), "(stall item removed)");
        foreach (ClientSession visitor in StallVisitors())
            visitor.Send(ViewPackets.RemoveItem(ViewId.Booth, slot), "(booth item removed)");
    }

    private bool StallRefused()
    {
        if (!StallOpen) return false;
        SystemMessage("Vending, unable to operate.");
        return true;
    }

    private bool StillValid(StallEntry entry)
        => entry.Buying
            ? entry.Price > 0 && entry.Amount > 0
            : Items.TryGetValue(entry.BagSlot, out ItemStack? item) && item.Name == entry.Name && item.Amount >= entry.Remaining && entry.Remaining > 0;

    private int FreeStallSlot()
    {
        for (int slot = 1; slot <= StallSlots; slot++)
        {
            if (!_stall.ContainsKey(slot)) return slot;
        }
        return 0;
    }

    private static PropertyBag StallProperties(StallEntry entry, int slot)
    {
        PropertyBag bag = ItemProperties(new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = entry.Amount }, slot, entry.Price);
        bag["AmountStall"] = entry.Done;
        bag["SelectState"] = entry.Buying ? StallBuyEntry : 0;
        return bag;
    }

    private string? ResolveArticle(string typed)
    {
        string wanted = ArticleKey(typed);
        if (wanted.Length == 0) return null;
        string? direct = Context.Data.Items.Goods.Keys.FirstOrDefault(key => ArticleKey(key) == wanted);
        if (direct != null) return direct;
        return ArticleNames().TryGetValue(wanted, out string? name) && Context.Data.Items.Goods.ContainsKey(name) ? name : null;
    }

    private Dictionary<string, string> ArticleNames()
    {
        lock (ArticleNamesLock)
        {
            if (_articleNames != null) return _articleNames;
            var names = new Dictionary<string, string>();
            string path = Path.Combine(Context.Settings.ResourcePath, "ui", "lang", "china", "mapped", "namelist.idres");
            if (File.Exists(path))
            {
                string section = "";
                foreach (string raw in File.ReadLines(path, Encoding.Latin1))
                {
                    string line = raw.Trim();
                    if (line.StartsWith('[') && line.EndsWith(']')) section = line[1..^1];
                    else if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase) && section.Length > 0)
                        names.TryAdd(ArticleKey(line[5..]), section);
                }
            }
            return _articleNames = names;
        }
    }

    private static string ArticleKey(string text)
        => new(text.Where(letter => letter != '\u0003' && !char.IsWhiteSpace(letter)).Select(char.ToLowerInvariant).ToArray());
}
