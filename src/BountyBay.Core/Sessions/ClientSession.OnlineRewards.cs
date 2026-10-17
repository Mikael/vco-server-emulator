using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private long _onlineRewardMark = -1;

    private OnlineRewardRules RewardRules => OnlineRewardTable.Get(Context.Settings);

    private static string RewardDay => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void StartOnlineRewards()
    {
        ResetRewardDay();
        _onlineRewardMark = Environment.TickCount64;
        SendOnlineRewardCountdown();
        if (PropertyTable.Has("AwardFlag")) SetProperty("AwardFlag", Character.DailyRewardDay == RewardDay ? 0 : 1);
    }

    public void LeaveWorldOnlineRewards()
    {
        FoldOnlineRewardTime();
        _onlineRewardMark = -1;
    }

    public void ClaimOnlineReward(int stage)
    {
        FoldOnlineRewardTime();
        if (ResetRewardDay()) SendOnlineRewardCountdown();
        OnlineRewardStage? current = CurrentRewardStage();
        if (current == null || stage < current.Stage)
        {
            Send(OnlineRewardPackets.Claimed(stage), $"(online reward {stage} already collected)");
            SystemMessage(current == null ? "You have collected every online reward today." : "That online reward was already collected.",
                SystemMessageKind.Prompt);
            return;
        }
        int left = OnlineRewardSecondsLeft(current);
        if (stage > current.Stage || left > RewardRules.ClaimToleranceSeconds)
        {
            Send(OnlineRewardPackets.Resync(current.Stage, left), $"(online reward {current.Stage} in {left}s)");
            SystemMessage($"Stay online {RewardWait(left)} longer to collect this reward.", SystemMessageKind.Prompt);
            return;
        }
        List<string> gained = GrantOnlineReward(current.Money, current.Items, "(online reward)");
        Character.OnlineRewardStage = current.Stage + 1;
        Character.OnlineRewardSeconds = 0;
        _onlineRewardMark = Environment.TickCount64;
        Save();
        Send(OnlineRewardPackets.Claimed(current.Stage), $"(online reward {current.Stage} collected)");
        SystemMessage($"Online reward {current.Stage}: {(gained.Count > 0 ? string.Join(", ", gained) : "nothing")}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} collects online reward {current.Stage} ({string.Join(", ", gained)})");
        if (CurrentRewardStage() == null) SystemMessage("You have collected every online reward today.", SystemMessageKind.Prompt);
        else SendOnlineRewardCountdown();
    }

    public void ClaimDailyReward()
    {
        if (Character.DailyRewardDay == RewardDay)
        {
            SystemMessage("You have already collected today's reward.", SystemMessageKind.Prompt);
            if (PropertyTable.Has("AwardFlag")) SetProperty("AwardFlag", 0);
            return;
        }
        Character.DailyRewardDay = RewardDay;
        List<string> gained = GrantOnlineReward(RewardRules.DailyMoney, RewardRules.DailyItems, "(daily reward)");
        Save();
        if (PropertyTable.Has("AwardFlag")) SetProperty("AwardFlag", 0);
        SystemMessage($"Daily reward: {(gained.Count > 0 ? string.Join(", ", gained) : "nothing")}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} collects the daily reward ({string.Join(", ", gained)})");
    }

    private void SendOnlineRewardCountdown()
    {
        OnlineRewardStage? current = CurrentRewardStage();
        if (current == null) return;
        int left = OnlineRewardSecondsLeft(current);
        Send(OnlineRewardPackets.Countdown(current.Stage, left), $"(online reward {current.Stage} in {left}s)");
    }

    private OnlineRewardStage? CurrentRewardStage()
    {
        int stage = Math.Max(1, Character.OnlineRewardStage);
        return RewardRules.Stages.FirstOrDefault(entry => entry.Stage == stage);
    }

    private int OnlineRewardSecondsLeft(OnlineRewardStage stage)
    {
        long running = _onlineRewardMark < 0 ? 0 : (Environment.TickCount64 - _onlineRewardMark) / 1000;
        return (int)Math.Max(0, stage.Minutes * 60L - Character.OnlineRewardSeconds - running);
    }

    private void FoldOnlineRewardTime()
    {
        if (_onlineRewardMark < 0) return;
        long now = Environment.TickCount64;
        long seconds = (now - _onlineRewardMark) / 1000;
        if (seconds <= 0) return;
        Character.OnlineRewardSeconds = (int)Math.Min(int.MaxValue, Character.OnlineRewardSeconds + seconds);
        _onlineRewardMark += seconds * 1000;
    }

    private bool ResetRewardDay()
    {
        if (Character.OnlineRewardDay == RewardDay) return false;
        Character.OnlineRewardDay = RewardDay;
        Character.OnlineRewardStage = 1;
        Character.OnlineRewardSeconds = 0;
        if (_onlineRewardMark >= 0) _onlineRewardMark = Environment.TickCount64;
        if (PropertyTable.Has("AwardFlag") && InWorld && Character.DailyRewardDay != RewardDay) SetProperty("AwardFlag", 1);
        return true;
    }

    private List<string> GrantOnlineReward(int money, List<OnlineRewardItem> items, string note)
    {
        var gained = new List<string>();
        if (money > 0)
        {
            SetMoney((long)Money + money);
            gained.Add($"{money} silver");
        }
        foreach (OnlineRewardItem item in items.Where(entry => entry.Name.Length > 0 && entry.Amount > 0))
        {
            var stack = new ItemStack { Name = item.Name, Photo = Context.Data.PhotoOf(item.Name), Amount = item.Amount };
            string label = item.Label.Length > 0 ? item.Label : ItemCatalog.Text(item.Name, "Name") is { Length: > 0 } text ? text : item.Name;
            if (GiveItem(stack, note) || WarehouseStore(stack.Clone())) gained.Add($"{item.Amount} x {label}");
            else SystemMessage($"Your bags are full: {item.Amount} x {label} could not be given.", SystemMessageKind.Prompt);
        }
        return gained;
    }

    private static string RewardWait(int seconds)
        => seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60}m" : seconds >= 60 ? $"{seconds / 60}m {seconds % 60}s" : $"{seconds}s";
}
