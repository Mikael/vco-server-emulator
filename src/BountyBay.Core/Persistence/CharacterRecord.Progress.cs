namespace BountyBay.Core.Persistence;

public sealed class UniqueSkillEntry
{
    public string Key { get; set; } = "";
    public int Level { get; set; } = 1;
}

public sealed class QuestProgress
{
    public int Id { get; set; }
    public int Count { get; set; }
    public string Date { get; set; } = "";
}

public sealed class PetRecord
{
    public string Name { get; set; } = "Parrot";
    public string Kind { get; set; } = "Parrot";
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Gorge { get; set; } = 80;
    public int Health { get; set; } = 100;
    public int Appetency { get; set; } = 50;
    public int Fealty { get; set; } = 60;
    public int Life { get; set; } = 100;
    public bool Out { get; set; }
}

public sealed partial class CharacterRecord
{
    public Dictionary<string, int> LearnedSkills { get; set; } = [];
    public Dictionary<string, int> SkillExperience { get; set; } = [];
    public Dictionary<int, ItemStack> Warehouse { get; set; } = [];
    public int GamePoints { get; set; }
    public SortedSet<string> AdvancedSkills { get; set; } = [];
    public List<UniqueSkillEntry> UniqueSkills { get; set; } = [];
    public List<QuestProgress> Quests { get; set; } = [];
    public List<QuestProgress> CompletedQuests { get; set; } = [];
    public PetRecord? Pet { get; set; }
}
