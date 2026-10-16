using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int BoothSearchView = 33;
    private const int BoothSearchAllClasses = 10;

    private readonly List<(ClientSession Seller, int Slot)> _boothSearchRows = [];
    private bool _boothSearchBuying;
    private string _boothSearchName = "";
    private int _boothSearchClass = BoothSearchAllClasses;
    private int _boothSearchPage = 1;
    private bool _boothSearchViewOpen;

    private BoothSearchData BoothSearchRules => BoothSearchCatalog.Get(Context.Settings);

    public void BoothSearch(bool buying, string name)
    {
        _boothSearchBuying = buying;
        _boothSearchName = name.Replace(UiPackets.Blank, ' ').Trim();
        ShowBoothSearchPage(1);
    }

    public void BoothSearchStep(int step) => ShowBoothSearchPage(_boothSearchPage + step);

    public void BoothSearchFilter(int searchClass)
    {
        _boothSearchClass = searchClass;
        ShowBoothSearchPage(1);
    }

    public void BoothSearchDetail(int row)
    {
        if (row < 1 || row > _boothSearchRows.Count) return;
        (ClientSession seller, int slot) = _boothSearchRows[row - 1];
        if (!seller.StallOpen || !seller._stall.TryGetValue(slot, out StallEntry? entry))
        {
            SystemMessage("The stall is closed.", SystemMessageKind.Prompt);
            return;
        }
        string what = entry.Buying ? "buys" : "sells";
        SystemMessage($"{seller.Name} {what} {BoothSearchLabel(entry.Name)}: {entry.Remaining} at {entry.Price} silver coins each.");
    }

    private void ShowBoothSearchPage(int page)
    {
        BoothSearchData rules = BoothSearchRules;
        int size = Math.Max(1, rules.PageSize);
        List<(ClientSession Seller, int Slot, StallEntry Entry)> found = Context.World.Sessions
            .Where(other => other != this && other.InWorld && other.StallOpen && (rules.WholeServer || other.Scene == Scene))
            .SelectMany(other => other._stall.OrderBy(pair => pair.Key).Select(pair => (Seller: other, Slot: pair.Key, Entry: pair.Value)))
            .Where(row => row.Entry.Buying == _boothSearchBuying && row.Entry.Remaining > 0)
            .Where(row => BoothSearchMatches(row.Entry.Name))
            .ToList();
        int pages = Math.Max(1, (found.Count + size - 1) / size);
        _boothSearchPage = Math.Clamp(page, 1, pages);
        if (_boothSearchViewOpen) Send(ViewPackets.Delete(BoothSearchView), "(booth search refresh)");
        _boothSearchViewOpen = true;
        _boothSearchRows.Clear();
        Send(ViewPackets.Create(BoothSearchView, size), "(booth search)");
        foreach ((ClientSession seller, int slot, StallEntry entry) in found.Skip((_boothSearchPage - 1) * size).Take(size))
        {
            _boothSearchRows.Add((seller, slot));
            int row = _boothSearchRows.Count;
            (float x, float z) = seller.Mover?.Now() ?? (0f, 0f);
            PropertyBag item = ItemProperties(new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = entry.Amount }, row, entry.Price);
            item["AmountStall"] = entry.Done;
            item["StallPlayerName"] = seller.Name;
            item["StallPlayerPosX"] = (int)x;
            item["StallPlayerPosZ"] = (int)z;
            Send(ViewPackets.AddItem(BoothSearchView, row, SeaPackets.Known(item)), "(booth search result)");
        }
        Send(ChatPackets.Script(0, $"scFindStallPage {_boothSearchPage} {pages}"), "(booth search page)");
        if (found.Count == 0) SystemMessage("No stall matches your search.", SystemMessageKind.Prompt);
        Log.Debug(Tag, $"{Name} booth search '{_boothSearchName}' class {_boothSearchClass}: {found.Count} hit(s), page {_boothSearchPage}/{pages}");
    }

    private bool BoothSearchMatches(string item)
    {
        if (_boothSearchName.Length > 0 && !item.Equals(_boothSearchName, StringComparison.OrdinalIgnoreCase)
            && !BoothSearchLabel(item).Contains(_boothSearchName, StringComparison.OrdinalIgnoreCase))
            return false;
        BoothSearchClass? wanted = BoothSearchRules.Classes.FirstOrDefault(entry => entry.Class == _boothSearchClass);
        if (wanted == null || wanted.All) return true;
        if (wanted.Rest) return !BoothSearchRules.Classes.Any(entry => !entry.Rest && !entry.All && BoothSearchInClass(entry, item));
        return (wanted.ItemTypes.Count == 0 && wanted.GoodsTypes.Count == 0) || BoothSearchInClass(wanted, item);
    }

    private static bool BoothSearchInClass(BoothSearchClass searchClass, string item)
        => searchClass.ItemTypes.Contains(ItemCatalog.Number(item, "ItemType"))
            || searchClass.GoodsTypes.Contains(ItemCatalog.Text(item, "GoodsType"), StringComparer.OrdinalIgnoreCase);

    private string BoothSearchLabel(string key)
        => Context.Data.Items.Goods.TryGetValue(key, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : key;
}
