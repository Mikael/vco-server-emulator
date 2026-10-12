using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed record UniqueSkillDefinition(string Key, string Skill, int CooldownSeconds, int Power);

public sealed partial class ClientSession
{
    public const int LearnSkillPrice = 200;
    public const int PromoteSkillPrice = 2000;
    public const int UniqueSkillPrice = 300;
    public const int TitlePrice = 500;
    public const int DemoteSkillPrice = 500;
    public const int ForgetUniqueSkillPrice = 300;
    public const int PromotionLevel = 31;
    public const int HighestUniqueSkillLevel = 10;

    private const float SkillReach = 12f;
    private const string BareHands = "SKILL_HAND";

    private static readonly string[] TeachableSkills =
    [
        "SKILL_SWORD", "SKILL_LEECHCRAFT", "SKILL_MINING", "SKILL_LUMBER", "SKILL_HALIEUTICS", "SKILL_PLANTING",
        "SKILL_FOUNDRY", "SKILL_ALCHEMY", "SKILL_SEWING", "SKILL_SHIPBUILDING",
    ];

    private static readonly UniqueSkillDefinition[] UniqueSkillCatalog =
    [
        new("US_SWORD1", "SKILL_SWORD", 30, 2),
        new("US_SWORD3", "SKILL_SWORD", 30, 2),
        new("US_SWORD4", "SKILL_SWORD", 120, 4),
        new("US_FALCHION1", "SKILL_FALCHION", 30, 2),
        new("US_FALCHION3", "SKILL_FALCHION", 30, 2),
        new("US_FALCHION4", "SKILL_FALCHION", 120, 4),
        new("US_AXE1", "SKILL_AXE", 30, 2),
        new("US_AXE3", "SKILL_AXE", 30, 2),
        new("US_AXE4", "SKILL_AXE", 120, 4),
        new("US_SHOT1", "SKILL_SHOT", 30, 2),
        new("US_SHOT3", "SKILL_SHOT", 6, 1),
        new("US_SHOT4", "SKILL_SHOT", 120, 4),
    ];

    private static readonly Dictionary<string, string> SkillTitles = new()
    {
        ["SKILL_NAVIGATE"] = "Navigator",
        ["SKILL_SPEECHCRAFT"] = "Negotiator",
        ["SKILL_SEABATTLE"] = "Gunner",
        ["SKILL_HAND"] = "Brawler",
        ["SKILL_FALCHION"] = "Corsair",
        ["SKILL_AXE"] = "Axeman",
        ["SKILL_SHOT"] = "Marksman",
        ["SKILL_SWORD"] = "Swordsman",
        ["SKILL_LEECHCRAFT"] = "Surgeon",
        ["SKILL_MINING"] = "Miner",
        ["SKILL_LUMBER"] = "Woodcutter",
        ["SKILL_HALIEUTICS"] = "Fisherman",
        ["SKILL_PLANTING"] = "Planter",
        ["SKILL_FOUNDRY"] = "Blacksmith",
        ["SKILL_ALCHEMY"] = "Alchemist",
        ["SKILL_SEWING"] = "Tailor",
        ["SKILL_SHIPBUILDING"] = "Shipbuilder",
    };

    private readonly Dictionary<string, DateTime> _skillReady = [];

    public int CombatTarget { get; set; }

    public int SkillLevel(string key)
    {
        int id = Context.Data.Creation.Skills.IndexOf(key);
        if (id >= 0) return Character.Skills.FirstOrDefault(skill => skill.Id == id)?.Level ?? 0;
        return Character.LearnedSkills.GetValueOrDefault(key);
    }

    public int UniqueSkillLevel(string key) => Character.UniqueSkills.FirstOrDefault(entry => entry.Key == key)?.Level ?? 0;

    public int SkillPoints(string key)
    {
        int level = SkillLevel(key);
        if (level <= 0) return 0;
        int spent = Character.UniqueSkills
            .Where(entry => UniqueSkillCatalog.Any(definition => definition.Key == entry.Key && definition.Skill == key))
            .Sum(entry => entry.Level);
        return Math.Max(0, 1 + level / 10 - spent);
    }

    public List<object[]> ProgressSkillRows()
    {
        List<string> keys = Context.Data.Creation.Skills;
        List<object[]> rows = Character.Skills
            .Where(skill => skill.Id >= 0 && skill.Id < keys.Count)
            .Select(skill => SkillRow(keys[skill.Id], skill.Level))
            .ToList();
        rows.AddRange(Character.LearnedSkills.Select(skill => SkillRow(skill.Key, skill.Value)));
        return rows;
    }

    public void SendSkillState(bool clear = true)
    {
        if (clear)
        {
            Send(TablePackets.Clear(GameTables.Skill), "(clear Skill)");
            Send(TablePackets.Rows(GameTables.Skill, ProgressSkillRows()), "(skills)");
        }
        SendTable(ProgressTables.SkillAdv, Character.AdvancedSkills.Select(key => new object[] { key, 1 }), clear);
        SendTable(ProgressTables.UniqueSkill, Character.UniqueSkills.Select(entry => new object[]
        {
            entry.Key, entry.Level, 0, UniqueSkillCatalog.FirstOrDefault(definition => definition.Key == entry.Key)?.Skill ?? "",
        }), clear);
        string fighting = FightingSkill();
        if (Properties.Text("FightSkill") != fighting) SetKnownProperty("FightSkill", fighting);
    }

    public void OpenSkillTutor()
    {
        SendTutorLists();
        Send(ChatPackets.Script(0, "ShowSkillTeacher"), "(skill tutor)");
    }

    public bool TutorCommand(int kind, string name)
    {
        switch (kind)
        {
            case 1:
                LearnSkill(name);
                return true;
            case 2:
                PromoteSkill(name);
                return true;
            case 3:
                LearnUniqueSkill(name);
                return true;
            case 4:
                ApplyForTitle(name);
                return true;
            case 5:
                DemoteSkill(name);
                return true;
            case 6:
                ForgetUniqueSkill(name);
                return true;
            default:
                return false;
        }
    }

    public void UseUniqueSkill(int row)
    {
        if (row < 0 || row >= Character.UniqueSkills.Count)
        {
            SystemMessage("You have not learned that special skill.");
            return;
        }
        UniqueSkillEntry entry = Character.UniqueSkills[row];
        UniqueSkillDefinition? definition = UniqueSkillCatalog.FirstOrDefault(candidate => candidate.Key == entry.Key);
        if (definition == null || Mover == null) return;
        if (AtSea)
        {
            SystemMessage("That special skill can only be used on land.");
            return;
        }
        if (_skillReady.TryGetValue(entry.Key, out DateTime ready) && ready > DateTime.UtcNow)
        {
            SystemMessage($"That special skill is ready again in {Math.Ceiling((ready - DateTime.UtcNow).TotalSeconds)} seconds.");
            return;
        }
        MonsterDefinition? monster = SkillTarget();
        if (monster == null)
        {
            SystemMessage("There is no enemy within reach.");
            return;
        }

        _skillReady[entry.Key] = DateTime.UtcNow.AddSeconds(definition.CooldownSeconds);
        Send(ChatPackets.Script(0, $"UniqueSkillReload {entry.Key} {definition.CooldownSeconds * 1000}"), "(special skill cooldown)");
        int normal = Random.Shared.Next(12, 23) + 3 * (Character.Level - 1);
        int dealt = normal * (1 + definition.Power) + 5 * entry.Level;
        Log.Info(Tag, $"{Name} uses {entry.Key} on {monster.Name} for {dealt}");
        StrikeWithSkill(monster, dealt);
    }

    private void SetKnownProperty(string name, object value)
    {
        if (PropertyTable.Has(name)) SetProperty(name, value);
        else Log.Debug(Tag, $"property {name} is not defined, not sent");
    }

    private object[] SkillRow(string key, int level) => [key, level, "0", "100", SkillPoints(key)];

    private void SendTable(TableDefinition table, IEnumerable<object[]> rows, bool clear = true)
    {
        if (clear) Send(TablePackets.Clear(table), $"(clear {table.Name})");
        List<object[]> list = rows.ToList();
        if (list.Count > 0) Send(TablePackets.Rows(table, list), $"({table.Name})");
    }

    private string FightingSkill()
    {
        string? key = Character.UniqueSkills.Select(entry => entry.Key).FirstOrDefault();
        return UniqueSkillCatalog.FirstOrDefault(definition => definition.Key == key)?.Skill ?? BareHands;
    }

    private void SendTutorLists()
    {
        string learn = string.Concat(TeachableSkills.Select(key => $" {key} {LearnSkillPrice}"));
        string promote = string.Concat(Context.Data.Creation.Skills.Concat(TeachableSkills).Select(key => $" {key} {PromoteSkillPrice}"));
        string unique = string.Concat(UniqueSkillCatalog.Select(definition =>
            $" {definition.Skill} {definition.Key} {UniqueSkillLevel(definition.Key)} {UniqueSkillCost(definition.Key)}"));
        string titles = string.Concat(SkillTitles.Select(title => $" {title.Key} {title.Value} {TitlePrice}"));
        Send(ChatPackets.Script(0, "laSkill" + learn), "(tutor skills)");
        Send(ChatPackets.Script(0, "laAdvSkill" + promote), "(tutor promotions)");
        Send(ChatPackets.Script(0, "laUSkill" + unique), "(tutor special skills)");
        Send(ChatPackets.Script(0, "laTitle" + titles), "(tutor titles)");
    }

    private int UniqueSkillCost(string key) => UniqueSkillPrice * (UniqueSkillLevel(key) + 1);

    private bool Pay(int price)
    {
        if (Money < price)
        {
            SystemMessage($"You need {price} silver coins for that.");
            return false;
        }
        SetMoney(Money - price);
        return true;
    }

    private void TutorDone(string what)
    {
        Save();
        SendSkillState();
        SendTutorLists();
        Log.Info(Tag, $"{Name} {what}");
    }

    private void LearnSkill(string key)
    {
        if (!TeachableSkills.Contains(key) || SkillLevel(key) > 0)
        {
            SystemMessage("You cannot learn that skill here.");
            return;
        }
        if (!Pay(LearnSkillPrice)) return;
        Character.LearnedSkills[key] = 1;
        TutorDone($"learns {key}");
    }

    private void PromoteSkill(string key)
    {
        int level = SkillLevel(key);
        if (level <= 0 || Character.AdvancedSkills.Contains(key))
        {
            SystemMessage("That skill cannot be promoted.");
            return;
        }
        if (level < PromotionLevel)
        {
            SystemMessage($"A skill must reach level {PromotionLevel} before it can be promoted.");
            return;
        }
        if (!Pay(PromoteSkillPrice)) return;
        Character.AdvancedSkills.Add(key);
        TutorDone($"promotes {key}");
    }

    private void DemoteSkill(string key)
    {
        if (!Character.AdvancedSkills.Contains(key))
        {
            SystemMessage("That skill has not been promoted.");
            return;
        }
        if (!Pay(DemoteSkillPrice)) return;
        Character.AdvancedSkills.Remove(key);
        SetSkillLevel(key, Math.Min(SkillLevel(key), PromotionLevel));
        Character.UniqueSkills.RemoveAll(entry => UniqueSkillCatalog.Any(definition => definition.Key == entry.Key && definition.Skill == key));
        TutorDone($"demotes {key}");
    }

    private void LearnUniqueSkill(string key)
    {
        UniqueSkillDefinition? definition = UniqueSkillCatalog.FirstOrDefault(candidate => candidate.Key == key);
        if (definition == null || SkillLevel(definition.Skill) <= 0)
        {
            SystemMessage("You must learn the weapon skill before its special skills.");
            return;
        }
        UniqueSkillEntry? entry = Character.UniqueSkills.FirstOrDefault(candidate => candidate.Key == key);
        if (entry != null && entry.Level >= HighestUniqueSkillLevel)
        {
            SystemMessage("That special skill cannot be raised any further.");
            return;
        }
        if (SkillPoints(definition.Skill) < 1)
        {
            SystemMessage("You have no skill points left for that skill.");
            return;
        }
        if (!Pay(UniqueSkillCost(key))) return;
        if (entry == null) Character.UniqueSkills.Add(new UniqueSkillEntry { Key = key, Level = 1 });
        else entry.Level++;
        TutorDone($"raises {key} to {UniqueSkillLevel(key)}");
    }

    private void ForgetUniqueSkill(string key)
    {
        if (UniqueSkillLevel(key) <= 0)
        {
            SystemMessage("You have not learned that special skill.");
            return;
        }
        if (!Pay(ForgetUniqueSkillPrice)) return;
        Character.UniqueSkills.RemoveAll(entry => entry.Key == key);
        _skillReady.Remove(key);
        TutorDone($"forgets {key}");
    }

    private void ApplyForTitle(string key)
    {
        if (!SkillTitles.TryGetValue(key, out string? title) || SkillLevel(key) <= 0)
        {
            SystemMessage("There is no title for that skill.");
            return;
        }
        if (Character.Titles.Contains(title))
        {
            SystemMessage($"You already hold the title {title}.");
            return;
        }
        if (!Pay(TitlePrice)) return;
        GrantTitle(title);
        SendTutorLists();
    }

    public void GrantTitle(string title)
    {
        if (Character.Titles.Contains(title)) return;
        Character.Titles.Add(title);
        Save();
        SendTable(GameTables.TitleList, Character.Titles.Select(name => new object[] { name }));
        SystemMessage($"You have earned the title {title}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} earns the title {title}");
    }

    private void SetSkillLevel(string key, int level)
    {
        int id = Context.Data.Creation.Skills.IndexOf(key);
        SkillEntry? entry = id >= 0 ? Character.Skills.FirstOrDefault(skill => skill.Id == id) : null;
        if (entry != null) entry.Level = level;
        else if (Character.LearnedSkills.ContainsKey(key)) Character.LearnedSkills[key] = level;
    }

    private MonsterDefinition? SkillTarget()
    {
        (float x, float z) = Mover!.Now();
        List<MonsterDefinition> near = Context.Data.Monsters.Values
            .Where(monster => monster.Scene == Scene && !Context.World.Monster(monster).Dead)
            .Where(monster => Distance(monster, x, z) <= SkillReach)
            .ToList();
        return near.FirstOrDefault(monster => monster.Id == CombatTarget) ?? near.MinBy(monster => Distance(monster, x, z));
    }

    private static float Distance(MonsterDefinition monster, float x, float z)
        => MathF.Sqrt((monster.X - x) * (monster.X - x) + (monster.Z - z) * (monster.Z - z));

    private void StrikeWithSkill(MonsterDefinition monster, int dealt, bool swing = true)
    {
        MonsterState state = Context.World.Monster(monster);
        state.Hp -= dealt;
        if (swing) PlayAction(Context.Data.Actions.Attack, "(special skill)", attack: true);
        List<ClientSession> everyone = [this, .. Others()];
        (float x, float z) = Mover!.Now();
        byte[] hit = ChatPackets.FloatingText(monster.Id, $"-{dealt}", 0xff8000, "wound", x, Height, z);
        foreach (ClientSession session in everyone) session.Send(hit, "(damage number)");

        if (state.Hp > 0)
        {
            byte[] update = ScenePackets.ObjectProperties(monster.Id, new PropertyBag
            {
                ["HP"] = state.Hp,
                ["RatioHP"] = 100 * state.Hp / Math.Max(1, monster.Hp),
            });
            foreach (ClientSession session in everyone) session.Send(update, "(monster hp)");
            return;
        }

        state.Dead = true;
        foreach (ClientSession session in everyone)
            session.Send(ScenePackets.RemoveObject(monster.Id), "(monster dies)");
        SetMoney(Money + monster.Gold);
        Save();
        SystemMessage($"You defeated the {monster.Name} and found {monster.Gold} gold.");
        GainExperience(monster.Hp / 2);
        Log.Info(Tag, $"{Name} killed {monster.Name}");
        Context.World.ScheduleRespawn(monster);
        OnMonsterKilled(monster.Id);
    }
}
