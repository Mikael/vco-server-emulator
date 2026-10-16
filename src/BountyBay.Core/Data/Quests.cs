using System.Text.Json;
using BountyBay.Core.Configuration;

namespace BountyBay.Core.Data;

public enum QuestKind
{
    Talk = 0,
    Kill = 1,
    Bring = 2,
    Level = 3,
    Money = 4,
}

public sealed class QuestTarget
{
    public QuestKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public int Count { get; set; } = 1;
    public string Skill { get; set; } = "";
    public string DropFrom { get; set; } = "";
    public bool Buy { get; set; }
    public bool Sink { get; set; }

    public bool Recorded => Kind is QuestKind.Kill or QuestKind.Bring;
}

public sealed class QuestRewardItem
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Count { get; set; } = 1;
    public string Fallback { get; set; } = "";
}

public sealed class QuestDefinition
{
    public const string ProfessionLevel = "WORK";

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public QuestKind Kind { get; set; }
    public int Giver { get; set; }
    public int Target { get; set; }
    public string TargetName { get; set; } = "";
    public string Item { get; set; } = "";
    public int Count { get; set; } = 1;
    public int Gold { get; set; }
    public int Experience { get; set; }
    public string Skill { get; set; } = "";
    public int Reputation { get; set; }
    public int Amity { get; set; }
    public string Offer { get; set; } = "Will you help me?";
    public string Done { get; set; } = "Thank you.";
    public int TurnIn { get; set; }
    public int Prerequisite { get; set; }
    public List<int> PrerequisiteAny { get; set; } = [];
    public List<int> Alternatives { get; set; } = [];
    public List<int> ExcludedBy { get; set; } = [];
    public int Group { get; set; }
    public int MinLevel { get; set; }
    public string Objective { get; set; } = "";
    public List<QuestTarget> Targets { get; set; } = [];
    public bool AutoAccept { get; set; }
    public bool AutoComplete { get; set; }
    public bool Daily { get; set; }
    public bool RequiresWork { get; set; }
    public bool StartsStoryline { get; set; }
    public long ProfessionExp { get; set; }
    public Dictionary<string, long> SkillExp { get; set; } = [];
    public List<QuestRewardItem> RewardItems { get; set; } = [];
    public bool RewardChooseOne { get; set; }
    public bool RewardRandomOne { get; set; }

    public int ReturnTo => TurnIn > 0 ? TurnIn : (Kind == QuestKind.Kill ? Giver : Target);

    public int RecordType => Kind == QuestKind.Kill ? 1 : 2;

    public IReadOnlyList<QuestTarget> Objectives => Targets.Count > 0 || Kind == QuestKind.Talk
        ? Targets
        : new List<QuestTarget> { new() { Kind = Kind, Name = TargetName, Key = Item, Count = Count } };

    public IEnumerable<int> Requires => PrerequisiteAny.Count > 0 ? PrerequisiteAny : Enumerable.Repeat(Prerequisite, Prerequisite > 0 ? 1 : 0);
}

public sealed class QuestBook
{
    private static readonly object Gate = new();
    private static QuestBook? _loaded;

    private readonly Dictionary<int, QuestDefinition> _byId;
    private readonly Dictionary<int, List<QuestDefinition>> _next = [];

    private QuestBook(List<QuestDefinition> quests)
    {
        Quests = quests;
        _byId = quests.GroupBy(quest => quest.Id).ToDictionary(group => group.Key, group => group.First());
        foreach (QuestDefinition quest in quests)
        {
            foreach (int required in quest.Requires.Distinct())
            {
                if (!_next.TryGetValue(required, out List<QuestDefinition>? list)) _next[required] = list = [];
                list.Add(quest);
            }
        }
    }

    public IReadOnlyList<QuestDefinition> Quests { get; }

    public QuestDefinition? Find(int id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<QuestDefinition> Following(int id) => _next.TryGetValue(id, out List<QuestDefinition>? list) ? list : [];

    public static QuestBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;
            string path = settings.Resolve("data\\game\\bbo\\quests.json");
            List<QuestDefinition> quests = File.Exists(path)
                ? JsonSerializer.Deserialize<List<QuestDefinition>>(File.ReadAllText(path), EmulatorSettings.Json) ?? []
                : [];
            return _loaded = new QuestBook(quests.Where(quest => quest.Id > 0).ToList());
        }
    }
}
