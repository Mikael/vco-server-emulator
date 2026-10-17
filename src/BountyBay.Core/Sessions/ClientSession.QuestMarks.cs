using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Data;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private QuestMarkBook QuestMarkBook => QuestMarkBook.For(Context.Settings);

    public void SendQuestMarks()
    {
        Send(QuestMarkTables.Clear(), "(clear Task_State)");
        if (!InWorld || AtSea) return;
        List<object[]> rows = [];
        foreach (NpcDefinition npc in Context.Data.Npcs.Values.Where(npc => npc.Scene == Scene))
        {
            (QuestMark mark, int quest) = QuestMarkFor(npc.Id);
            if (mark != QuestMark.None) rows.Add([QuestMarkTables.Key(npc.Id), quest, (int)mark]);
        }
        if (rows.Count > 0) Send(QuestMarkTables.Rows(rows), $"(Task_State {rows.Count})");
    }

    public (QuestMark Mark, int Quest) QuestMarkFor(int npcId)
    {
        List<(QuestDefinition Quest, QuestProgress Progress)> active = Character.Quests
            .Select(progress => (Quest: QuestBook.Find(progress.Id), Progress: progress))
            .Where(pair => pair.Quest != null && QuestTurnIn(pair.Quest) == npcId)
            .Select(pair => (pair.Quest!, pair.Progress))
            .ToList();
        foreach ((QuestDefinition quest, QuestProgress progress) in active)
        {
            if (QuestObjectivesDone(quest, progress)) return (QuestMark.Complete, quest.Id);
        }
        foreach (QuestDefinition quest in QuestBook.Quests)
        {
            if (quest.Giver == npcId && !quest.AutoAccept && QuestOpen(quest) && QuestUnlocked(quest)) return (QuestMark.Available, quest.Id);
        }
        return active.Count > 0 ? (QuestMark.InProgress, active[0].Quest.Id) : (QuestMark.None, 0);
    }

    public int QuestTurnIn(QuestDefinition quest)
    {
        int turnIn = QuestMarkBook.Find(quest.Id)?.TurnIn ?? 0;
        return turnIn > 0 ? turnIn : quest.ReturnTo;
    }

    public int TaskAcceptedState(int questId)
    {
        QuestMarkInfo? info = QuestMarkBook.Find(questId);
        int group = info == null ? 0 : info.TaskAcceptedGroup > 0 ? info.TaskAcceptedGroup : info.Group;
        return group is > 0 and <= 9999 ? int.Parse(QuestMarkTables.AcceptedPrefix + group.ToString("D4")) : 1;
    }

    public bool QuestUnlocked(QuestDefinition quest)
    {
        int minLevel = Math.Max(quest.MinLevel, QuestMarkBook.Find(quest.Id)?.MinLevel ?? 0);
        if (minLevel > 0 && TotalSkillLevel < minLevel) return false;
        if (quest.ExcludedBy.Concat(quest.Alternatives).Any(QuestTaken)) return false;
        List<int> required = quest.Requires.Where(id => QuestBook.Find(id) != null).ToList();
        return required.Count == 0 || required.Any(id => Character.CompletedQuests.Any(progress => progress.Id == id));
    }

    private bool QuestTaken(int id)
        => Character.Quests.Any(progress => progress.Id == id) || Character.CompletedQuests.Any(progress => progress.Id == id);

    private bool QuestObjectivesDone(QuestDefinition quest, QuestProgress progress)
    {
        IReadOnlyList<QuestTarget> targets = quest.Objectives;
        for (int i = 0; i < targets.Count; i++)
        {
            if (TargetAmount(quest, progress, i, targets[i]) < targets[i].Count) return false;
        }
        return true;
    }
}

public sealed class QuestMarkInfo
{
    public int Id { get; set; }
    public int TurnIn { get; set; }
    public int Prerequisite { get; set; }
    public int MinLevel { get; set; }
    public int Group { get; set; }
    public int TaskAcceptedGroup { get; set; }
}

public sealed class QuestMarkBook
{
    private static readonly object Gate = new();
    private static QuestMarkBook? _loaded;

    private readonly Dictionary<int, QuestMarkInfo> _quests;

    private QuestMarkBook(Dictionary<int, QuestMarkInfo> quests) => _quests = quests;

    public QuestMarkInfo? Find(int id) => _quests.GetValueOrDefault(id);

    public static QuestMarkBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;
            string path = settings.Resolve("data\\game\\bbo\\quests.json");
            List<QuestMarkInfo> quests = File.Exists(path)
                ? JsonSerializer.Deserialize<List<QuestMarkInfo>>(File.ReadAllText(path), EmulatorSettings.Json) ?? []
                : [];
            return _loaded = new QuestMarkBook(quests.Where(quest => quest.Id > 0)
                .GroupBy(quest => quest.Id)
                .ToDictionary(group => group.Key, group => group.First()));
        }
    }
}
