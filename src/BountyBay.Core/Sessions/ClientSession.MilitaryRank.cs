using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int MilitaryAwardLocked = 0;
    public const int MilitaryAwardReady = 1;
    public const int MilitaryAwardTaken = 2;

    private MilitaryRankRules MilitaryRules => LeisureRules.For(Context.Settings).MilitaryRank;

    public int MilitaryRankLevel => MilitaryRules.RankFor(Character.MilitaryHonor);

    public int MilitaryTrainingPoints
        => Math.Max(0, MilitaryRankLevel * MilitaryRules.TrainingPointsPerRank - Character.MilitaryTraining.Values.Sum());

    public void ApplyMilitaryRankProperties()
    {
        PutBboProperty("HonorLevel", MilitaryRankLevel);
        PutBboProperty("HonorExp", (int)Math.Clamp(Character.MilitaryHonor, 0, int.MaxValue));
    }

    public void AddMilitaryHonor(int amount)
    {
        if (amount == 0) return;
        int before = MilitaryRankLevel;
        Character.MilitaryHonor = Math.Max(0, Character.MilitaryHonor + amount);
        if (PropertyTable.Has("HonorExp")) SetProperty("HonorExp", (int)Math.Clamp(Character.MilitaryHonor, 0, int.MaxValue));
        int after = MilitaryRankLevel;
        if (after == before) return;
        if (PropertyTable.Has("HonorLevel")) SetProperty("HonorLevel", after, visibleToOthers: true);
        if (after > before) SystemMessage($"Your military rank rises to level {after}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} military rank {before} -> {after}");
    }

    public void SendMilitaryRankInfo()
    {
        SendLeisureTable(LeisureTables.MilitaryTraining,
            MilitaryRules.Attributes.Select(attribute => new[] { attribute.Key, MilitaryValue(attribute) }), "(military training)");
        SendMilitaryAwards();
    }

    public void TrainMilitaryAttribute(string key)
    {
        if (MilitaryRules.Attribute(key) is not { } attribute || attribute.Levels.Count == 0)
        {
            Log.Debug(Tag, $"{Name} trains an unknown military attribute {key}");
            return;
        }
        int level = Character.MilitaryTraining.GetValueOrDefault(key);
        if (level + 1 >= attribute.Levels.Count)
        {
            SystemMessage($"{attribute.Label} is already fully trained.", SystemMessageKind.Prompt);
            return;
        }
        int needed = attribute.Levels[level].Rank;
        if (MilitaryRankLevel < needed)
        {
            SystemMessage($"Your military rank is too low: level {needed} is needed to train {attribute.Label} further.", SystemMessageKind.Prompt);
            return;
        }
        if (MilitaryTrainingPoints <= 0)
        {
            SystemMessage("You have no training points left. Raise your military rank to earn more.", SystemMessageKind.Prompt);
            return;
        }
        Character.MilitaryTraining[key] = level + 1;
        Save();
        SendMilitaryRankInfo();
        SystemMessage($"{attribute.Label} training rises to {MilitaryValue(attribute)}. Training points left: {MilitaryTrainingPoints}.");
        Log.Info(Tag, $"{Name} trains {key} to level {level + 1}");
    }

    public void ClaimMilitaryAward(int rank)
    {
        MilitaryAward? award = MilitaryRules.Awards.FirstOrDefault(entry => entry.Rank == rank);
        if (award == null) return;
        if (Character.MilitaryAwards.Contains(rank))
        {
            SystemMessage("You have already received this reward.", SystemMessageKind.Prompt);
            return;
        }
        if (MilitaryRankLevel < rank)
        {
            SystemMessage("Your military rank is not high enough for this reward.", SystemMessageKind.Prompt);
            return;
        }
        if (award.Item.Length > 0 && !ReceiveIntoBag(award.Item, Context.Data.PhotoOf(award.Item), Math.Max(1, award.Amount), "(military reward)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        if (award.Money > 0) SetMoney((long)Money + award.Money);
        Character.MilitaryAwards.Add(rank);
        Save();
        SendMilitaryAwards();
        SystemMessage($"You receive the reward for military rank {rank}.");
    }

    private void SendMilitaryAwards()
    {
        SendLeisureTable(LeisureTables.HonorAward, MilitaryRules.Awards.OrderBy(award => award.Rank)
            .Select(award => new[]
            {
                award.Rank.ToString(CultureInfo.InvariantCulture), award.Rank.ToString(CultureInfo.InvariantCulture),
                award.Text, MilitaryAwardState(award.Rank).ToString(CultureInfo.InvariantCulture),
            }), "(military rewards)");
    }

    private int MilitaryAwardState(int rank)
        => Character.MilitaryAwards.Contains(rank) ? MilitaryAwardTaken : MilitaryRankLevel >= rank ? MilitaryAwardReady : MilitaryAwardLocked;

    private string MilitaryValue(MilitaryAttribute attribute)
    {
        int level = Math.Clamp(Character.MilitaryTraining.GetValueOrDefault(attribute.Key), 0, attribute.Levels.Count - 1);
        return attribute.Levels[level].Value;
    }
}
