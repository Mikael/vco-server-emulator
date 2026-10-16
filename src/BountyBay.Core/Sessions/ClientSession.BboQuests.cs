using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int MostTrackedQuests = 5;

    private readonly HashSet<int> _offeredQuests = [];

    public void SendBboQuestState(bool clear = true)
    {
        Character.TrackedQuests.RemoveAll(id => Character.Quests.All(progress => progress.Id != id));
        List<object[]> rows = Character.TrackedQuests.Distinct().Select(id => new object[] { id }).ToList();
        if (clear) Send(TablePackets.Clear(BboSocialTables.TaskAttached), "(clear Task_Attached)");
        if (rows.Count > 0) Send(TablePackets.Rows(BboSocialTables.TaskAttached, rows), "(Task_Attached)");
    }

    public void AttachQuest(int id)
    {
        if (Character.Quests.All(progress => progress.Id != id))
        {
            SendBboQuestState();
            return;
        }
        if (Character.TrackedQuests.Contains(id)) return;
        if (Character.TrackedQuests.Count >= MostTrackedQuests)
        {
            SystemMessage($"You can track at most {MostTrackedQuests} quests.");
            return;
        }
        Character.TrackedQuests.Add(id);
        Save();
        SendBboQuestState();
    }

    public void DetachQuest(int id)
    {
        if (Character.TrackedQuests.RemoveAll(tracked => tracked == id) == 0) return;
        Save();
        SendBboQuestState();
    }

    public void ToggleAutoTrack()
    {
        Character.AutoTrackQuests = !Character.AutoTrackQuests;
        Save();
        SystemMessage(Character.AutoTrackQuests ? "New quests are tracked automatically." : "New quests are no longer tracked automatically.");
    }

    public void GiveUpQuest(int id)
    {
        QuestProgress? progress = Character.Quests.FirstOrDefault(entry => entry.Id == id);
        if (progress == null)
        {
            SendQuestState();
            SendBboQuestState();
            return;
        }
        Character.Quests.Remove(progress);
        Character.TrackedQuests.RemoveAll(tracked => tracked == id);
        Save();
        SendQuestState();
        SendBboQuestState();
        QuestDefinition? quest = QuestBook.Find(id);
        SystemMessage($"Task abandoned: {quest?.Name ?? id.ToString()}.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} gives up quest {id}");
    }

    public void OfferQuest(QuestDefinition quest)
    {
        if (!QuestOpen(quest.Id)) return;
        _offeredQuests.Add(quest.Id);
        Send(BboSocialPackets.NewTaskPop(quest.Id), $"(quest offer {quest.Id})");
    }

    public void AcceptOfferedQuest(int id)
    {
        QuestDefinition? quest = QuestBook.Find(id);
        if (quest == null || (!_offeredQuests.Remove(id) && TalkingTo != quest.Giver))
        {
            SystemMessage("That task is not offered to you.");
            return;
        }
        AcceptQuest(quest.Giver, quest);
    }

    public void OnBboQuestAccepted(QuestDefinition quest)
    {
        _offeredQuests.Remove(quest.Id);
        if (Character.AutoTrackQuests && !Character.TrackedQuests.Contains(quest.Id) && Character.TrackedQuests.Count < MostTrackedQuests)
        {
            Character.TrackedQuests.Add(quest.Id);
            Save();
        }
        SendBboQuestState();
        Send(BboSocialPackets.NewTaskFlash(quest.Id), $"(quest flash {quest.Id})");
    }

    public void OnBboQuestObjectivesDone(QuestDefinition quest)
        => Send(BboSocialPackets.TaskFinished(quest.Id), $"(quest objectives done {quest.Id})");

    public void OnBboQuestCompleted(QuestDefinition quest)
    {
        if (Character.TrackedQuests.RemoveAll(tracked => tracked == quest.Id) == 0) return;
        SendBboQuestState();
    }

    public void StartNoviceGuide()
    {
        Character.NoviceStep = 0;
        Save();
        Send(BboSocialPackets.NoviceGuide(0), "(novice guide)");
    }

    public void NextNoviceStep()
    {
        if (Character.NoviceStep < 0)
        {
            SystemMessage("Talk to a guide to start the beginner's tutorial.");
            return;
        }
        Character.NoviceStep++;
        if (Character.NoviceStep >= BboSocialPackets.NoviceSteps)
        {
            Character.NoviceStep = -1;
            Save();
            SystemMessage("You have finished the beginner's tutorial.", SystemMessageKind.Prompt);
            return;
        }
        Save();
        Send(BboSocialPackets.NoviceGuide(Character.NoviceStep), $"(novice guide step {Character.NoviceStep})");
    }
}
