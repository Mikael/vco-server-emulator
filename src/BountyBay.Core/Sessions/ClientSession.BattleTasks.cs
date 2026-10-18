using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Daily battlefield quests. "scOpenTeamBattleTask 0" opens dlgBattleTask (St Deus), "1" dlgRelicTask (Palenque); the
// windows answer csAcceptTeamBTask <id>, 0-3 and 4-7 = the four tiers of each. A quest lasts for the day and adds a
// share of the battlefield's next reward.
public sealed partial class ClientSession
{
    private const int TaskTiers = 4;

    private static string BattleTaskDay => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void OpenBattleTasks(int window) => Send(UiPackets.Custom($"scOpenTeamBattleTask {window}"), "(daily battlefield quests)");

    public void AcceptBattleTask(int id)
    {
        int window = id / TaskTiers, tier = id % TaskTiers;
        if (window < 0 || window >= BattleRules.DailyTasks.Count) return;
        BattleDailyTask task = BattleRules.DailyTasks[window];
        string name = BattleRules.Battle(task.Battle)?.Name ?? "the battlefield";
        if (Character.BattleTasks.TryGetValue(task.Battle, out BattleTaskRecord? taken) && taken.Day == BattleTaskDay)
        {
            SystemMessage($"You already have today's {name} quest.", SystemMessageKind.Prompt);
            return;
        }
        int cost = tier < task.Costs.Count ? task.Costs[tier] : 0;
        if (cost > 0 && task.CostPoints)
        {
            if (Character.GamePoints < cost)
            {
                SystemMessage($"This quest costs {cost} points.", SystemMessageKind.Prompt);
                return;
            }
            Character.GamePoints -= cost;
            SendMallPoints();
        }
        else if (cost > 0)
        {
            if (BagTotal(task.CostItem) < cost)
            {
                string label = Context.Data.Items.Goods.GetValueOrDefault(task.CostItem)?.Label ?? task.CostItem;
                SystemMessage($"This quest costs {cost} x {label}.", SystemMessageKind.Prompt);
                return;
            }
            TakeFromBags(task.CostItem, cost);
        }
        Character.BattleTasks[task.Battle] = new BattleTaskRecord { Day = BattleTaskDay, Tier = tier };
        Save();
        SystemMessage($"You accepted today's {name} quest. Your next reward there is increased.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} accepts the {name} daily quest, tier {tier}");
    }

    // Called with the rewards of a finished battle; returns the bonus rewards of today's quest, once.
    private List<BattleReward> BattleTaskBonus(BattleDefinition battle, IEnumerable<BattleReward> earned)
    {
        if (!Character.BattleTasks.TryGetValue(battle.Type, out BattleTaskRecord? task) || task.Day != BattleTaskDay || task.Done)
            return [];
        BattleDailyTask? rules = BattleRules.DailyTasks.FirstOrDefault(entry => entry.Battle == battle.Type);
        if (rules == null) return [];
        int percent = task.Tier < rules.BonusPercent.Count ? rules.BonusPercent[task.Tier] : 0;
        task.Done = true;
        SystemMessage($"Daily quest completed: {battle.Name}.", SystemMessageKind.Upgrade);
        return earned.Select(reward => new BattleReward { Item = reward.Item, Honor = reward.Honor, Amount = reward.Amount * percent / 100 })
            .Where(reward => reward.Amount > 0).ToList();
    }
}
