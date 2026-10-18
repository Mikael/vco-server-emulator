using BountyBay.Core.Data;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int FindBoothView = 33;
    private const int FindBoothPageSize = 6;

    private bool _findBoothBuying;
    private string _findBoothName = "";
    private bool _findBoothViewOpen;

    public IEnumerable<StallEntry> StallEntries => StallOpen ? _stall.Values : [];

    public void FindBooth(bool buying, string name, int page)
    {
        _findBoothBuying = buying;
        _findBoothName = name.Trim();
        ShowFindBoothPage(page);
    }

    public void FindBoothPage(int page) => ShowFindBoothPage(page);

    private void ShowFindBoothPage(int page)
    {
        List<(ClientSession Seller, StallEntry Entry)> found = Context.World.InScene(Scene)
            .Where(other => other != this)
            .SelectMany(other => other.StallEntries.Select(entry => (other, entry)))
            .Where(pair => pair.entry.Buying == _findBoothBuying && pair.entry.Remaining > 0)
            .Where(pair => _findBoothName.Length == 0 || ItemLabel(pair.entry.Name).Contains(_findBoothName, StringComparison.OrdinalIgnoreCase)
                || pair.entry.Name.Equals(_findBoothName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        int pages = Math.Max(1, (found.Count + FindBoothPageSize - 1) / FindBoothPageSize);
        page = Math.Clamp(page, 1, pages);
        if (_findBoothViewOpen) Send(ViewPackets.Delete(FindBoothView), "(booth search refresh)");
        _findBoothViewOpen = true;
        Send(ViewPackets.Create(FindBoothView, FindBoothPageSize), "(booth search)");
        int slot = 0;
        foreach ((ClientSession seller, StallEntry entry) in found.Skip((page - 1) * FindBoothPageSize).Take(FindBoothPageSize))
        {
            slot++;
            (float x, float z) = seller.Mover?.Now() ?? (0f, 0f);
            Send(ViewPackets.AddItem(FindBoothView, slot, SeaPackets.Known(new PropertyBag
            {
                ["Name"] = entry.Name,
                ["Index"] = slot,
                ["Amount"] = entry.Remaining,
                ["Photo"] = entry.Photo,
                ["ShowPrice"] = entry.Price,
                ["StallPlayerName"] = seller.Name,
                ["StallPlayerPosX"] = (int)x,
                ["StallPlayerPosZ"] = (int)z,
            })), "(booth search result)");
        }
        Send(ChatPackets.Script(0, $"scFindStallPage {page} {pages}"), "(booth search page)");
        if (found.Count == 0) SystemMessage("No stall nearby matches your search.", SystemMessageKind.Prompt);
    }

    public string ItemLabel(string key)
        => Context.Data.Items.Goods.TryGetValue(key, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : key;
}
