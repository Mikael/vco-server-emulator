using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int CampNone = 0;
    public const int CampGuards = 1;
    public const int CampConquerors = 2;

    private CampRules CampData => LeisureRules.For(Context.Settings).Camp;

    public void ApplyCampProperties()
    {
        PutBboProperty("Camp", Character.PopularityCamp);
        PutBboProperty("PopularityUp", Character.PopularityUp);
        PutBboProperty("PopularityDown", Character.PopularityDown);
    }

    public void SetCamp(int camp)
    {
        if (camp is not (CampNone or CampGuards or CampConquerors)) return;
        Character.PopularityCamp = camp;
        Save();
        if (PropertyTable.Has("Camp")) SetProperty("Camp", camp, visibleToOthers: true);
    }

    public void SetCampTrack(int track)
    {
        Character.CampTrack = Math.Clamp(track, 0, 2);
        Log.Debug(Tag, $"{Name} camp tracking {Character.CampTrack}");
    }

    public void AddCampPopularity(int amount)
    {
        if (amount == 0 || Character.PopularityCamp == CampNone) return;
        Character.PopularityUp = (int)Math.Clamp((long)Character.PopularityUp + amount, 0, int.MaxValue);
        if (PropertyTable.Has("PopularityUp")) SetProperty("PopularityUp", Character.PopularityUp);
        if (amount > 0) SystemMessage($"Your camp reputation rises by {amount}.");
    }

    public void AddCampHostility(int amount)
    {
        if (amount == 0) return;
        Character.PopularityDown = (int)Math.Clamp((long)Character.PopularityDown + amount, 0, int.MaxValue);
        if (PropertyTable.Has("PopularityDown")) SetProperty("PopularityDown", Character.PopularityDown);
    }

    public void CampMonsterKilled(string monsterKey)
    {
        if (Character.PopularityCamp == CampNone || !CampData.Monsters.TryGetValue(monsterKey, out Dictionary<string, int>? byCamp)) return;
        if (byCamp.TryGetValue(Character.PopularityCamp.ToString(CultureInfo.InvariantCulture), out int amount)) AddCampPopularity(amount);
    }

    public void CampTaskDone(int task)
    {
        if (Character.PopularityCamp != CampNone && CampData.Tasks.TryGetValue(task.ToString(CultureInfo.InvariantCulture), out int amount))
            AddCampPopularity(amount);
    }

    public void HandInCampItems()
    {
        if (Character.PopularityCamp == CampNone)
        {
            SystemMessage("You have not joined a camp yet.", SystemMessageKind.Prompt);
            return;
        }
        int total = 0;
        foreach (CampSubmission submission in CampData.Submissions.Where(entry => entry.Camp == Character.PopularityCamp))
        {
            int owned = Owned(submission.Item);
            if (owned <= 0) continue;
            TakeOwned(submission.Item, owned, "(camp items handed in)");
            total += owned * submission.Popularity;
        }
        if (total == 0)
        {
            SystemMessage("You have nothing to hand in for your camp.", SystemMessageKind.Prompt);
            return;
        }
        AddCampPopularity(total);
        Save();
    }

    public void OpenCampExchange()
    {
        Send(LeisurePackets.ThingShow(), "(camp exchange window)");
    }

    public void SendCampGoodsInfo()
    {
        ResetCampPurchases();
        SendLeisureTable(LeisureTables.CampGoods, CampData.Goods
            .Select(goods => new[] { goods.Key, Character.CampBought.GetValueOrDefault(goods.Key).ToString(CultureInfo.InvariantCulture) }),
            "(camp goods bought)");
        if (PropertyTable.Has("PopularityUp")) SetProperty("PopularityUp", Character.PopularityUp);
    }

    public void BuyCampGoods(string key, int count)
    {
        ResetCampPurchases();
        CampRules rules = CampData;
        if (rules.Find(key) is not { } goods || count <= 0) return;
        if (Character.PopularityCamp == CampNone || goods.Camp != Character.PopularityCamp)
        {
            SystemMessage(Character.PopularityCamp == CampNone ? "You have not joined a camp yet." : "These goods are only sold to the other camp.", SystemMessageKind.Prompt);
            return;
        }
        int tier = Math.Min(rules.Tier(Character.PopularityUp), Math.Max(1, rules.PriceTiers) - 1);
        int limit = goods.At(goods.Limit, tier);
        int bought = Character.CampBought.GetValueOrDefault(key);
        if (tier <= 0 || limit <= 0)
        {
            SystemMessage("Your reputation with your camp is too low to buy these goods.", SystemMessageKind.Prompt);
            return;
        }
        if (bought + count > limit)
        {
            SystemMessage($"You can buy only {Math.Max(0, limit - bought)} more of these today.", SystemMessageKind.Prompt);
            return;
        }
        long reputation = (long)goods.At(goods.PopularityCost, tier) * count;
        long silver = (long)goods.At(goods.Price, tier) * count * (goods.PriceType == 0 ? 1 : rules.CouponMoney);
        if (Character.PopularityUp < reputation || Money < silver)
        {
            SystemMessage("You do not have enough camp reputation or silver coins.", SystemMessageKind.Prompt);
            return;
        }
        string item = goods.Item.Length > 0 ? goods.Item : goods.Key;
        int photo = goods.Photo != 0 ? goods.Photo : Context.Data.PhotoOf(item);
        if (!ReceiveIntoBag(item, photo, count, "(camp goods)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        if (silver > 0) SetMoney(Money - silver);
        Character.PopularityUp -= (int)reputation;
        Character.CampBought[key] = bought + count;
        Save();
        SendCampGoodsInfo();
        Log.Info(Tag, $"{Name} buys {count} x {key} for {reputation} camp reputation and {silver} silver");
    }

    private void ResetCampPurchases()
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (Character.CampBoughtDay == today) return;
        Character.CampBoughtDay = today;
        Character.CampBought.Clear();
    }
}
