using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public void QueryParcelCost(int view, int index)
    {
        double unit = MailBagOf(view) is { } bag && bag.TryGetValue(index, out ItemStack? item) ? ParcelUnitCost(item.Name) : 0;
        Send(ChatPackets.Script(0, "SendCost " + unit.ToString("0.###", CultureInfo.InvariantCulture)), "(parcel postage)");
    }

    public void SendParcelWithPostage(int view, int index, int amount, string recipient, string message)
    {
        if (MailBagOf(view) is not { } bag || !bag.TryGetValue(index, out ItemStack? item) || amount <= 0 || amount > item.Amount)
        {
            SendParcel(view, index, amount, recipient, message);
            return;
        }
        long cost = ParcelCost(item.Name, amount);
        if (Money < cost)
        {
            SystemMessage($"The postage of this parcel is {cost} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        string name = item.Name;
        int before = item.Amount;
        SendParcel(view, index, amount, recipient, message);
        int after = bag.TryGetValue(index, out ItemStack? left) && left.Name == name ? left.Amount : 0;
        if (after >= before) return;
        SetMoney(Money - cost);
        Save();
        Log.Info(Tag, $"{Name} pays {cost} silver postage");
    }

    private double ParcelUnitCost(string item)
    {
        ParcelCostRules rules = ParcelCostCatalog.Get(Context.Settings);
        return rules.CostPerArticle + rules.CostPerWeight * Math.Max(0, ItemCatalog.Number(item, "Weight"));
    }

    private long ParcelCost(string item, int amount)
    {
        ParcelCostRules rules = ParcelCostCatalog.Get(Context.Settings);
        long cost = (long)(ParcelUnitCost(item) * amount);
        return Math.Clamp(cost, rules.Minimum, rules.Maximum);
    }
}
