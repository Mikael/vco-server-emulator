using System.Text.Json;
using VoyageCentury.Core.Configuration;

namespace VoyageCentury.Core.Data;

public enum QuestKind
{
    Talk = 0,
    Kill = 1,
    Bring = 2,
}

public sealed class QuestDefinition
{
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

    public int ReturnTo => Kind == QuestKind.Kill ? Giver : Target;

    public int RecordType => Kind == QuestKind.Kill ? 1 : 2;
}

public sealed class QuestBook
{
    private static readonly object Gate = new();
    private static QuestBook? _loaded;

    private QuestBook(List<QuestDefinition> quests) => Quests = quests;

    public IReadOnlyList<QuestDefinition> Quests { get; }

    public QuestDefinition? Find(int id) => Quests.FirstOrDefault(quest => quest.Id == id);

    public static QuestBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;
            string path = settings.Resolve("data\\game\\quests.json");
            List<QuestDefinition> quests = File.Exists(path)
                ? JsonSerializer.Deserialize<List<QuestDefinition>>(File.ReadAllText(path), EmulatorSettings.Json) ?? []
                : [];
            return _loaded = new QuestBook(quests.Where(quest => quest.Id > 0).ToList());
        }
    }
}
