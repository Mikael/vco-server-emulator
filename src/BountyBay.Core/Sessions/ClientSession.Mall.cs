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

    public void OpenMall()
    {
        List<MallGoods> goods = ItemMall.Load(Context.Settings).Goods;
        if (goods.Count == 0)
        {
            NotAvailable("The item mall", "store realbuy");
            return;
        }
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
                ["Photo"] = WhistleBook.Whistle(entry.Item)?.Image is > 0 and int image ? image : Context.Data.PhotoOf(entry.Item),
            };
            ItemCatalog.Extend(item, entry.Item);
            item["Cost"] = entry.Price;
            item["ShowPrice"] = entry.Price;
            Send(ViewPackets.AddItem(MallView, i + 1, item), "(mall goods)");
        }
        SendMallPoints();
        Log.Info(Tag, $"{Name} opens the item mall ({goods.Count} goods, {Character.GamePoints} points)");
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
        Log.Info(Tag, $"{Name} buys {quantity} x {goods.Item} in the item mall for {cost} points");
        return true;
    }
}
