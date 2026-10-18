using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int MallView = 32;

    private List<MallGoods>? _mall;

    private const int MallMenuOption = 47000;
    private static readonly string[] MallCategories = ["Experience", "Refine", "Sea Battle", "Land Battle", "Items", "Clothes", "Pets"];
    private int _mallRequest;

    public void OpenMall()
    {
        if (ItemMall.Load(Context.Settings).Goods.Count == 0)
        {
            NotAvailable("The item mall", "store realbuy");
            return;
        }
        int request = ++_mallRequest;
        _ = Task.Run(async () =>
        {
            await Task.Delay(400);
            lock (Context.World.Sync)
            {
                if (request != _mallRequest || !InWorld) return;
                ShowMall(0);
            }
        });
    }

    public bool HandleMallOption(int target, int option)
    {
        if (option < MallMenuOption || option > MallMenuOption + MallCategories.Length) return false;
        _mallRequest++;
        Send(UiPackets.CloseMenu(), "(mall category menu)");
        ShowMall(option - MallMenuOption);
        return true;
    }

    private void PreloadMallMenu()
    {
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, "Item Mall") };
        for (int i = 0; i < MallCategories.Length; i++) entries.Add((DialogueEntry.Option, MallMenuOption + i + 1, MallCategories[i]));
        entries.Add((DialogueEntry.Option, MallMenuOption, "All goods"));
        Send(ChatPackets.NpcDialogue(ProtocolConstants.SelfObjectId, entries), "(mall category menu)");
        Send(UiPackets.CloseMenu(), "(mall category menu)");
    }

    private void ShowMall(int category)
    {
        List<MallGoods> goods = ItemMall.Load(Context.Settings).Goods.Where(entry => category == 0 || entry.Category == category).ToList();
        if (_mall != null) Send(ViewPackets.Delete(MallView), "(mall refresh)");
        _mall = goods;
        Send(ViewPackets.Create(MallView, goods.Count), "(item mall)");
        for (int i = 0; i < goods.Count; i++)
        {
            MallGoods entry = goods[i];
            var item = new PropertyBag
            {
                ["Name"] = entry.Item,
                ["Index"] = i + 1,
                ["Amount"] = 1,
                ["Photo"] = Context.Data.PhotoOf(entry.Item) is > 0 and int photo ? photo
                    : WhistleBook.Whistle(entry.Item)?.Image is > 0 and int image ? image : 0,
            };
            ItemCatalog.Extend(item, entry.Item);
            item["Cost"] = entry.Price;
            item["ShowPrice"] = entry.Price;
            Send(ViewPackets.AddItem(MallView, i + 1, item), "(mall goods)");
        }
        SendMallPoints();
        PreloadMallMenu();
        Log.Info(Tag, $"{Name} opens the item mall ({(category == 0 ? "all" : MallCategories[category - 1])}: {goods.Count} goods, {Character.GamePoints} points)");
    }

    private void SendMallPoints()
    {
        if (PropertyTable.Has("LeftCardPoints")) SetProperty("LeftCardPoints", Character.GamePoints);
    }

    public bool MallSelect(int view, int how, int index)
    {
        if (view != MallView || _mall == null) return false;
        if (index < 1 || index > _mall.Count) return true;
        MallGoods goods = _mall[index - 1];
        int most = Math.Min(99, Character.GamePoints / Math.Max(1, goods.Price));
        if (most < 1)
        {
            SystemMessage($"Not enough points: {goods.Name} costs {goods.Price} points and you have {Character.GamePoints}.");
            return true;
        }
        Send(BboSeaPackets.BuyInfo(view, index, goods.Price, most), "(mall offer)");
        return true;
    }

    public bool MallBuy(int view, int index, int quantity)
    {
        if (view != MallView || _mall == null) return false;
        if (index < 1 || index > _mall.Count) return true;
        MallGoods goods = _mall[index - 1];
        quantity = Math.Clamp(quantity, 1, 99);
        long cost = (long)goods.Price * quantity;
        if (cost > Character.GamePoints)
        {
            SystemMessage($"Not enough points: {quantity} x {goods.Name} costs {cost} points and you have {Character.GamePoints}.");
            return true;
        }
        if (WhistleBook.Whistle(goods.Item) != null)
        {
            int given = 0;
            for (; given < quantity && GiveWhistle(goods.Item, sea: false) != null; given++)
            {
            }
            if (given == 0)
            {
                SystemMessage($"You cannot keep more than {WhistleMostOwned} pet whistles.", SystemMessageKind.Prompt);
                return true;
            }
            quantity = given;
            cost = (long)goods.Price * quantity;
        }
        else if (!GiveItem(new ItemStack { Name = goods.Item, Photo = Context.Data.PhotoOf(goods.Item), Amount = quantity }, "(mall purchase)"))
        {
            SystemMessage("Your bag is full.");
            return true;
        }
        Character.GamePoints -= (int)cost;
        GainVipScore(cost, "mall purchase");
        Save();
        SendMallPoints();
        Send(UiPackets.Custom("scBuyGameGoodsSuccess"), "(mall purchase done)");
        SystemMessage(WhistleBook.Whistle(goods.Item) != null
            ? $"You bought {quantity} x {goods.Name} for {cost} points. Open the pet list to call your pet."
            : $"You bought {quantity} x {goods.Name} for {cost} points.");
        ItemObtained(goods.Item, quantity, "");
        Log.Info(Tag, $"{Name} buys {quantity} x {goods.Item} in the item mall for {cost} points");
        return true;
    }
}
