using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int SkillTutorOption = 7;
    public const int QuestsOption = 8;
    public const int PetOption = 9;
    public const int PetCallOption = 90;
    public const int PetWindowOption = 91;
    public const int FirstQuestOption = 10000;

    private const int ByeOption = 3;
    private const int RecordAmountColumn = 5;

    private QuestBook QuestBook => QuestBook.For(Context.Settings);

    public bool HandleProgressOption(int target, int option)
    {
        switch (option)
        {
            case SkillTutorOption:
                OpenSkillTutor();
                return true;
            case QuestsOption:
                ShowQuests(target);
                return true;
            case PetOption:
                ShowPetMenu(target);
                return true;
            case PetCallOption:
                TogglePet();
                ShowPetMenu(target);
                return true;
            case PetWindowOption:
                OpenPetWindow();
                return true;
        }
        int index = option - FirstQuestOption;
        if (index < 0 || index >= QuestBook.Quests.Count) return false;
        AcceptQuest(target, QuestBook.Quests[index]);
        return true;
    }

    public void SendProgress()
    {
        SendSkillState(clear: false);
        SendQuestState(clear: false);
        if (!Properties.ContainsKey("SeeRw_ListNum")) SetKnownProperty("SeeRw_ListNum", 0);
        if (!Properties.ContainsKey("SeeMemoryNum")) SetKnownProperty("SeeMemoryNum", 0);
    }

    public void SendQuestState(bool clear = true)
    {
        foreach (QuestProgress progress in Character.Quests)
        {
            QuestDefinition? quest = QuestBook.Find(progress.Id);
            if (quest?.Kind == QuestKind.Bring) progress.Count = Carried(quest);
        }
        SendTable(ProgressTables.TaskAccepted, Character.Quests.Select(progress => new object[] { progress.Id, TaskAcceptedState(progress.Id), progress.Date }), clear);
        SendTable(ProgressTables.TaskRecord, RecordedQuests().Select(pair => new object[]
        {
            pair.Quest.Id, pair.Quest.RecordType, 0, 0, pair.Quest.Count, pair.Progress.Count, 0, pair.Quest.TargetName,
        }), clear);
        SendTable(ProgressTables.TaskCompleted, Character.CompletedQuests.Select(progress => new object[] { progress.Id, progress.Date }), clear);
        SendQuestMarks();
    }

    public void ShowQuests(int npc)
    {
        SendQuestState();
        var entries = new List<(DialogueEntry, int, string)>();
        IReadOnlyList<QuestDefinition> quests = QuestBook.Quests;
        for (int i = 0; i < quests.Count; i++)
        {
            if (quests[i].Giver == npc && QuestOpen(quests[i].Id) && QuestUnlocked(quests[i]))
                entries.Add((DialogueEntry.Option, FirstQuestOption + i, quests[i].Name));
        }
        List<string> waiting = Character.Quests
            .Select(progress => QuestBook.Find(progress.Id))
            .Where(quest => quest != null && (quest.Giver == npc || quest.ReturnTo == npc))
            .Select(quest => quest!.Name)
            .ToList();
        string text = entries.Count > 0 ? "I could use a capable hand." : "I have no work for you right now.";
        if (waiting.Count > 0) text += $" Still open: {string.Join(", ", waiting)}.";
        entries.Insert(0, (DialogueEntry.Text, 0, text));
        entries.Add((DialogueEntry.Option, ByeOption, "Goodbye"));
        Send(ChatPackets.NpcDialogue(npc, entries), "(quest dialogue)");
    }

    public void AcceptQuest(int npc, QuestDefinition quest)
    {
        if (quest.Giver != npc || !QuestOpen(quest.Id))
        {
            SystemMessage("That task is not available.");
            return;
        }
        Character.Quests.Add(new QuestProgress { Id = quest.Id, Date = Today() });
        Save();
        SendQuestState();
        OnBboQuestAccepted(quest);
        SystemMessage($"Task accepted: {quest.Name}.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} accepts quest {quest.Id} ({quest.Name})");
        Send(ChatPackets.NpcDialogue(npc,
        [
            (DialogueEntry.Text, 0, quest.Offer),
            (DialogueEntry.Option, ByeOption, "Goodbye"),
        ]), "(quest dialogue)");
    }

    public void OnMonsterKilled(int monsterId)
    {
        PetGainExperience(10);
        Context.Data.Monsters.TryGetValue(monsterId, out MonsterDefinition? killed);
        if (killed != null) GainExploreKill(killed);
        CountKill(monsterId, killed?.Name ?? "");
    }

    public void OnSeaNpcSunk(string name) => CountKill(0, name);

    private bool QuestHunts(QuestDefinition quest, int monsterId, string name)
    {
        if (monsterId != 0 && quest.Target == monsterId) return true;
        if (name.Length == 0) return false;
        string wanted = Context.Data.Monsters.TryGetValue(quest.Target, out MonsterDefinition? target) ? target.Name : quest.TargetName;
        return SameQuestName(wanted, name);
    }

    private void CountKill(int monsterId, string name)
    {
        var recorded = RecordedQuests().ToList();
        for (int row = 0; row < recorded.Count; row++)
        {
            (QuestDefinition quest, QuestProgress progress) = recorded[row];
            if (quest.Kind != QuestKind.Kill || progress.Count >= quest.Count || !QuestHunts(quest, monsterId, name)) continue;
            progress.Count++;
            ReportProgress(row, progress.Count);
            if (progress.Count >= quest.Count)
            {
                SystemMessage($"{quest.Name}: done. Return for your reward.", SystemMessageKind.Prompt);
                OnBboQuestObjectivesDone(quest);
            }
        }
        Save();
    }

    public void OnNpcTalked(int npcId)
    {
        var recorded = RecordedQuests().ToList();
        for (int row = 0; row < recorded.Count; row++)
        {
            (QuestDefinition quest, QuestProgress progress) = recorded[row];
            if (quest.Kind != QuestKind.Bring) continue;
            int carried = Carried(quest);
            if (carried == progress.Count) continue;
            progress.Count = carried;
            ReportProgress(row, carried);
        }
        foreach (QuestProgress progress in Character.Quests.ToList())
        {
            QuestDefinition? quest = QuestBook.Find(progress.Id);
            if (quest == null || QuestTurnIn(quest) != npcId) continue;
            if (quest.Kind != QuestKind.Talk && progress.Count < quest.Count) continue;
            if (!QuestReadyToHandIn(quest)) continue;
            CompleteQuest(quest, progress);
        }
    }

    public void WatchTask(string property, int id)
    {
        if (property is "SeeRw_ListNum" or "SeeMemoryNum") SetKnownProperty(property, id);
    }

    private bool QuestOpen(int id)
        => Character.Quests.All(progress => progress.Id != id) && Character.CompletedQuests.All(progress => progress.Id != id);

    private IEnumerable<(QuestDefinition Quest, QuestProgress Progress)> RecordedQuests()
    {
        foreach (QuestProgress progress in Character.Quests)
        {
            QuestDefinition? quest = QuestBook.Find(progress.Id);
            if (quest != null && quest.Kind != QuestKind.Talk) yield return (quest, progress);
        }
    }

    private int Carried(QuestDefinition quest)
        => Math.Min(quest.Count, Owned(quest.Item));

    private void ReportProgress(int row, int amount)
    {
        Send(TablePackets.UpdateCell(ProgressTables.TaskRecord, row, RecordAmountColumn, amount), "(task progress)");
        Send(ChatPackets.Script(0, $"UpdateTaskState {row} {amount}"), "(task notice)");
        SendQuestMarks();
    }

    private void CompleteQuest(QuestDefinition quest, QuestProgress progress)
    {
        OnBboQuestCompleted(quest);
        if (quest.Kind == QuestKind.Bring)
        {
            TakeOwned(quest.Item, quest.Count, "(task item handed in)");
        }
        Character.Quests.Remove(progress);
        Character.CompletedQuests.Add(new QuestProgress { Id = quest.Id, Count = quest.Count, Date = Today() });
        if (Properties.Int("SeeRw_ListNum") == -quest.Id) SetKnownProperty("SeeRw_ListNum", 0);
        SendQuestState();
        SystemMessage(quest.Done);
        SystemMessage($"Task completed: {quest.Name}. You receive {quest.Gold} silver coins.", SystemMessageKind.Upgrade);
        SetMoney(Money + quest.Gold);
        GrantQuestExtras(quest);
        Log.Info(Tag, $"{Name} completes quest {quest.Id} ({quest.Name})");
        if (quest.Experience > 0 && quest.Skill.Length > 0) GainSkillExperience(quest.Skill, quest.Experience);
        if (quest.Reputation > 0) GainReputation(quest.Reputation);
        if (quest.Amity > 0)
        {
            Character.Amity += quest.Amity;
            SystemMessage($"Your amity with {Properties.Text("CountryName", "this country")} rises by {quest.Amity}.");
        }
        Save();
    }

    private static string Today()
    {
        DateTime now = DateTime.Now;
        return $"{now.Year}-{now.Month}-{now.Day}";
    }
}
