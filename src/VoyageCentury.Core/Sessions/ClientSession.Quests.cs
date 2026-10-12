using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed partial class ClientSession
{
    public const int SkillTutorOption = 7;
    public const int QuestsOption = 8;
    public const int PetOption = 9;
    public const int PetCallOption = 90;
    public const int PetWindowOption = 91;
    public const int FirstQuestOption = 100;

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
        SendTable(ProgressTables.TaskAccepted, Character.Quests.Select(progress => new object[] { progress.Id, 1, progress.Date }), clear);
        SendTable(ProgressTables.TaskRecord, RecordedQuests().Select(pair => new object[]
        {
            pair.Quest.Id, pair.Quest.RecordType, 0, 0, pair.Quest.Count, pair.Progress.Count, 0, pair.Quest.TargetName,
        }), clear);
        SendTable(ProgressTables.TaskCompleted, Character.CompletedQuests.Select(progress => new object[] { progress.Id, progress.Date }), clear);
    }

    public void ShowQuests(int npc)
    {
        SendQuestState();
        var entries = new List<(DialogueEntry, int, string)>();
        IReadOnlyList<QuestDefinition> quests = QuestBook.Quests;
        for (int i = 0; i < quests.Count; i++)
        {
            if (quests[i].Giver == npc && QuestOpen(quests[i].Id))
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
        var recorded = RecordedQuests().ToList();
        for (int row = 0; row < recorded.Count; row++)
        {
            (QuestDefinition quest, QuestProgress progress) = recorded[row];
            if (quest.Kind != QuestKind.Kill || quest.Target != monsterId || progress.Count >= quest.Count) continue;
            progress.Count++;
            ReportProgress(row, progress.Count);
            if (progress.Count >= quest.Count)
                SystemMessage($"{quest.Name}: done. Return for your reward.", SystemMessageKind.Prompt);
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
            if (quest == null || quest.ReturnTo != npcId) continue;
            if (quest.Kind != QuestKind.Talk && progress.Count < quest.Count) continue;
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
        => Math.Min(quest.Count, Items.Values.Where(item => item.Name == quest.Item).Sum(item => item.Amount));

    private void ReportProgress(int row, int amount)
    {
        Send(TablePackets.UpdateCell(ProgressTables.TaskRecord, row, RecordAmountColumn, amount), "(task progress)");
        Send(ChatPackets.Script(0, $"UpdateTaskState {row} {amount}"), "(task notice)");
    }

    private void CompleteQuest(QuestDefinition quest, QuestProgress progress)
    {
        if (quest.Kind == QuestKind.Bring)
        {
            int owed = quest.Count;
            foreach (int slot in Items.Where(pair => pair.Value.Name == quest.Item).Select(pair => pair.Key).ToList())
            {
                int taken = Math.Min(owed, Items[slot].Amount);
                TakeFromBag(slot, taken, "(task item handed in)");
                owed -= taken;
                if (owed <= 0) break;
            }
        }
        Character.Quests.Remove(progress);
        Character.CompletedQuests.Add(new QuestProgress { Id = quest.Id, Count = quest.Count, Date = Today() });
        if (Properties.Int("SeeRw_ListNum") == -quest.Id) SetKnownProperty("SeeRw_ListNum", 0);
        SendQuestState();
        SystemMessage(quest.Done);
        SystemMessage($"Task completed: {quest.Name}. You receive {quest.Gold} silver coins.", SystemMessageKind.Upgrade);
        SetMoney(Money + quest.Gold);
        Log.Info(Tag, $"{Name} completes quest {quest.Id} ({quest.Name})");
        if (quest.Experience > 0) GainExperience(quest.Experience);
        else Save();
    }

    private static string Today()
    {
        DateTime now = DateTime.Now;
        return $"{now.Year}-{now.Month}-{now.Day}";
    }
}
