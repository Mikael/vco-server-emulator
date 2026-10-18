using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private static readonly long[] VipLevelScores = [10000, 30000, 70000, 150000, 300000, 550000, 1050000];

    public int VipLevel => VipLevelScores.Count(score => Character.VipScore >= score);

    public void ApplyVipProperty()
    {
        if (PropertyTable.Has("VIP")) Properties["VIP"] = VipLevel;
    }

    public void GainVipScore(long points, string reason)
    {
        if (points <= 0) return;
        int before = VipLevel;
        Character.VipScore = Math.Min(long.MaxValue - points, Character.VipScore) + points;
        int after = VipLevel;
        Log.Info(Tag, $"{Name} gains {points} VIP points ({reason}), total {Character.VipScore}");
        if (after == before) return;
        if (PropertyTable.Has("VIP")) SetProperty("VIP", after, visibleToOthers: true);
        SystemMessage($"You are now VIP {after}!", SystemMessageKind.Upgrade);
    }

    public void SetVipScore(long score)
    {
        Character.VipScore = Math.Max(0, score);
        if (PropertyTable.Has("VIP")) SetProperty("VIP", VipLevel, visibleToOthers: true);
        Save();
        SystemMessage($"VIP points {Character.VipScore}, VIP {VipLevel}.");
    }

    public void BboShowVip()
    {
        int level = VipLevel;
        Send(BboProtocolPackets.VipInfo(Math.Max(1, level), level > 0 ? "VIP" + level : "-", (int)Math.Min(int.MaxValue, Character.VipScore), 0), "(vip info)");
    }
}
