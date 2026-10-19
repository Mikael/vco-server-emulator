using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed record UniqueSkillDefinition(string Key, string Skill, int Scenes = 0, string FightSkill = "", int Equipment = 0,
    int CooldownSeconds = 0, int SpCost = 0)
{
    public const int OnLand = 1;
    public const int Offshore = 2;
    public const int HighSea = 4;
    public const int Workshop = 8;
    public const int Boarding = 16;

    public bool Active => CooldownSeconds > 0;
}

public sealed record SkillLesson(string Key, int Price, string Requires = "", int RequiredLevel = 0, bool AnyBattleSkill = false);

public sealed partial class ClientSession
{
    private const string BareHands = "SKILL_HAND";
    private const string Medicine = "SKILL_LEECHCRAFT";
    private const int ShipArmHook = 5;
    private const int RamSlot = 2;
    private const int AnyAuxiliarySlot = 7;
    private const int TutorListLength = 1000;

    private static readonly string[] BattleSkills = ["SKILL_HAND", "SKILL_SWORD", "SKILL_FALCHION", "SKILL_AXE", "SKILL_SHOT"];

    private static readonly SkillLesson[] Lessons =
    [
        new("SKILL_MINING", 5000),
        new("SKILL_LUMBER", 5000),
        new("SKILL_PLANTING", 5000),
        new("SKILL_HALIEUTICS", 5000),
        new("SKILL_FOUNDRY", 5000, "SKILL_MINING", 5),
        new("SKILL_ALCHEMY", 1200, "SKILL_PLANTING", 3),
        new("SKILL_SEWING", 18000, "SKILL_PLANTING", 5),
        new("SKILL_SHIPBUILDING", 8000),
        new("SKILL_FALCHION", 7000),
        new("SKILL_SWORD", 7000),
        new("SKILL_AXE", 7000),
        new("SKILL_SHOT", 10000),
        new("SKILL_LEECHCRAFT", 10000, "", 3, true),
    ];

    private static readonly UniqueSkillDefinition[] UniqueSkillCatalog =
    [
        new("US_SWORD1", "SKILL_SWORD", 1, "SKILL_SWORD", 0, 30, 30),
        new("US_SWORD3", "SKILL_SWORD", 1, "SKILL_SWORD", 0, 30, 30),
        new("US_SWORD4", "SKILL_SWORD", 1, "SKILL_SWORD", 0, 120, 100),
        new("US_FALCHION1", "SKILL_FALCHION", 1, "SKILL_FALCHION", 0, 30, 30),
        new("US_FALCHION3", "SKILL_FALCHION", 1, "SKILL_FALCHION", 0, 30, 30),
        new("US_FALCHION4", "SKILL_FALCHION", 1, "SKILL_FALCHION", 0, 120, 100),
        new("US_AXE1", "SKILL_AXE", 1, "SKILL_AXE", 0, 30, 30),
        new("US_AXE3", "SKILL_AXE", 1, "SKILL_AXE", 0, 30, 30),
        new("US_AXE4", "SKILL_AXE", 1, "SKILL_AXE", 0, 120, 100),
        new("US_SHOT1", "SKILL_SHOT", 1, "SKILL_SHOT", 0, 30, 30),
        new("US_SHOT3", "SKILL_SHOT", 1, "SKILL_SHOT", 0, 6, 10),
        new("US_SHOT4", "SKILL_SHOT", 1, "SKILL_SHOT", 0, 120, 100),
        new("US_ATTACK", "SKILL_NAVIGATE", 2, "", 0, 120, 30),
        new("US_CHASE", "SKILL_NAVIGATE", 2, "", 0, 120, 30),
        new("US_SPEEDUP", "SKILL_NAVIGATE", 6, "", 0, 120, 30),
        new("US_BUMP", "SKILL_SEABATTLE", 2, "", 2, 20, 30),
        new("US_HOOK", "SKILL_SEABATTLE", 2, "", 7, 20, 30),
        new("US_PLANTING3", "SKILL_PLANTING", 1, "", 0, 10, 10),
        new("US_SEWING3", "SKILL_SEWING", 9, "", 0, 300, 200),
        new("US_SEWING4", "SKILL_SEWING", 9, "", 0, 10, 100),
        new("US_FOUNDRY3", "SKILL_FOUNDRY", 9, "", 0, 300, 200),
        new("US_SHIPBUILDING3", "SKILL_SHIPBUILDING", 2, "", 0, 300, 100),
        new("US_SHIPBUILDING4", "SKILL_SHIPBUILDING", 16, "", 0, 900, 100),
        new("US_FOUNDRY4", "SKILL_FOUNDRY", 1, "", 0, 180, 100),
        new("US_ALCHEMY4", "SKILL_ALCHEMY", 1, "", 0, 300, 100),
        new("US_SWORD2", "SKILL_SWORD"),
        new("US_FALCHION2", "SKILL_FALCHION"),
        new("US_AXE2", "SKILL_AXE"),
        new("US_SHOT2", "SKILL_SHOT"),
        new("US_CANNON", "SKILL_SEABATTLE"),
        new("US_TRADE", "SKILL_SPEECHCRAFT"),
        new("US_NEGOTIATE", "SKILL_SPEECHCRAFT"),
        new("US_STALL", "SKILL_SPEECHCRAFT"),
        new("US_HAND1", "SKILL_HAND"),
        new("US_HAND2", "SKILL_HAND"),
        new("US_HAND3", "SKILL_HAND"),
        new("US_LUMBER1", "SKILL_LUMBER"),
        new("US_LUMBER2", "SKILL_LUMBER"),
        new("US_LUMBER3", "SKILL_LUMBER"),
        new("US_MINING1", "SKILL_MINING"),
        new("US_MINING2", "SKILL_MINING"),
        new("US_MINING3", "SKILL_MINING"),
        new("US_PLANTING1", "SKILL_PLANTING"),
        new("US_PLANTING2", "SKILL_PLANTING"),
        new("US_HALIEUTICS1", "SKILL_HALIEUTICS"),
        new("US_HALIEUTICS2", "SKILL_HALIEUTICS"),
        new("US_HALIEUTICS3", "SKILL_HALIEUTICS"),
        new("US_ALCHEMY1", "SKILL_ALCHEMY"),
        new("US_ALCHEMY2", "SKILL_ALCHEMY"),
        new("US_ALCHEMY3", "SKILL_ALCHEMY"),
        new("US_SEWING1", "SKILL_SEWING"),
        new("US_SEWING2", "SKILL_SEWING"),
        new("US_FOUNDRY1", "SKILL_FOUNDRY"),
        new("US_FOUNDRY2", "SKILL_FOUNDRY"),
        new("US_SHIPBUILDING1", "SKILL_SHIPBUILDING"),
        new("US_SHIPBUILDING2", "SKILL_SHIPBUILDING"),
        new("US_HEAL", "SKILL_LEECHCRAFT"),
        new("US_TOXIC", "SKILL_LEECHCRAFT"),
        new("US_EXCITE", "SKILL_LEECHCRAFT"),
    ];

    private static readonly string[] LoadStunts = ["US_HAND1", "US_LUMBER1", "US_MINING1", "US_PLANTING1"];

    private static readonly string[] MaxSpStunts =
    [
        "US_LUMBER3", "US_MINING3", "US_HALIEUTICS1", "US_ALCHEMY1", "US_SEWING1", "US_FOUNDRY1", "US_SHIPBUILDING1",
    ];

    private static readonly string[] LateStunts = ["US_ALCHEMY4", "US_SEWING4", "US_FOUNDRY4", "US_SHIPBUILDING4"];

    private static readonly string[] UltimateStunts = ["US_SWORD4", "US_FALCHION4", "US_AXE4", "US_SHOT4"];

    private static readonly string[] OtherWorkingStunts =
    [
        "US_HAND2", "US_HAND3", "US_HALIEUTICS2", "US_ALCHEMY3", "US_SEWING2", "US_FOUNDRY2",
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

    public int SkillCap(string key) => Character.AdvancedSkills.Contains(key) ? Rule.Skills.PhaseTwoCap : Rule.Skills.PhaseOneCap;

    public int SkillExperienceMax(string key) => Rule.Skills.ExperienceToNext(Math.Max(1, SkillLevel(key)));

    public int UniqueSkillLevel(string key) => Character.UniqueSkills.FirstOrDefault(entry => entry.Key == key)?.Level ?? 0;

    public int StuntLevels(IEnumerable<string> keys) => keys.Sum(UniqueSkillLevel);

    public int SkillPoints(string key)
    {
        int level = SkillLevel(key);
        if (level <= 0) return 0;
        int spent = Character.UniqueSkills
            .Where(entry => UniqueSkillCatalog.Any(definition => definition.Key == entry.Key && definition.Skill == key))
            .Sum(entry => entry.Level);
        return Math.Max(0, Rule.Skills.StuntPoints(level) - spent);
    }

    public void GainSkillExperience(string key, long amount)
    {
        int level = SkillLevel(key);
        if (amount <= 0 || level <= 0) return;
        SkillRules rules = Rule.Skills;
        int cap = SkillCap(key);
        if (key == "SKILL_SEABATTLE") GainShipExperience(ShipAttribute.Fight, amount);
        if (key == "SKILL_SPEECHCRAFT") GainShipExperience(ShipAttribute.Load, amount * rules.StructureSharePercent / 100);
        if (level >= cap) return;
        long experience = Character.SkillExperience.GetValueOrDefault(key) + amount;
        bool raised = false;
        while (level < cap && experience >= rules.ExperienceToNext(level))
        {
            experience -= rules.ExperienceToNext(level);
            level++;
            raised = true;
        }
        if (level >= cap) experience = 0;
        Character.SkillExperience[key] = (int)Math.Min(int.MaxValue, experience);
        if (!raised)
        {
            if (SkillRowIndex(key) >= 0)
            {
                Send(TablePackets.Clear(GameTables.Skill), "(clear Skill)");
                Send(TablePackets.Rows(GameTables.Skill, ProgressSkillRows()), "(skill experience)");
            }
            if (InWorld) SendLevelProgress();
            return;
        }
        SetSkillLevel(key, level);
        PlayLevelUpEffect();
        SystemMessage(level >= cap && cap == rules.PhaseOneCap
            ? $"{ClientNames.Of(Context.Settings, key)} reached level {level}. Visit a Skill Tutor to open its second phase."
            : $"{ClientNames.Of(Context.Settings, key)} reached level {level}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} raises {key} to {level}");
        KeepTotalSkillCap(key);
        Send(TablePackets.Clear(GameTables.Skill), "(clear Skill)");
        Send(TablePackets.Rows(GameTables.Skill, ProgressSkillRows()), "(skills)");
        if (InWorld) SendStuntStates();
        RefreshStats();
        Send(UiPackets.Custom($"scUpLevel {TotalSkillLevel}"), "(level up)");
        Save();
    }

    public void MigrateSkills()
    {
        SkillRules rules = Rule.Skills;
        if (Character.RulesVersion < 1)
        {
            Character.RulesVersion = 1;
            Character.ShipHull = -1;
            Character.ShipSail = -1;
            foreach (OwnedShip ship in Character.Fleet ?? [])
            {
                ship.Hull = -1;
                ship.Sail = -1;
            }
        }
        if (rules.BornPromoted.Length > 0 && SkillLevel(rules.BornPromoted) > 0) Character.AdvancedSkills.Add(rules.BornPromoted);
        foreach (UniqueSkillEntry entry in Character.UniqueSkills)
            entry.Level = Math.Clamp(entry.Level, 1, StuntLevelMax(entry.Key));
        foreach (string key in SkillKeys().ToList())
        {
            int cap = SkillCap(key);
            if (SkillLevel(key) > cap) SetSkillLevel(key, cap);
            if (Character.SkillExperience.GetValueOrDefault(key) >= rules.ExperienceToNext(Math.Max(1, SkillLevel(key))))
                Character.SkillExperience[key] = 0;
        }
    }

    private void KeepTotalSkillCap(string raised)
    {
        int cap = Rule.Skills.TotalLevelCap;
        while (cap > 0 && TotalSkillLevel > cap)
        {
            string? lowest = SkillKeys().Where(key => key != raised && SkillLevel(key) > 1).MinBy(SkillLevel);
            if (lowest == null) return;
            SetSkillLevel(lowest, SkillLevel(lowest) - 1);
            Character.SkillExperience.Remove(lowest);
            SystemMessage("The total of your skill levels is at its limit, so your lowest skill dropped one level.", SystemMessageKind.Prompt);
        }
    }

    private int StuntLevelMax(string key) => UltimateStunts.Contains(key) ? Rule.Skills.UltimateStuntLevelMax : Rule.Skills.StuntLevelMax;

    private int StuntSkillLevelFor(string key, int stuntLevel)
    {
        SkillRules rules = Rule.Skills;
        int needed = rules.StuntSkillLevel(stuntLevel);
        if (UltimateStunts.Contains(key)) needed = Math.Max(needed, rules.UltimateStuntSkillLevel);
        if (LateStunts.Contains(key)) needed = Math.Max(needed, rules.LateStuntSkillLevel);
        return needed;
    }

    private int PromotionCost()
    {
        SkillRules rules = Rule.Skills;
        return Character.AdvancedSkills.Count >= rules.FreePromotions ? rules.PaidPromotionFee : rules.PromotionFee;
    }

    public List<object[]> ProgressSkillRows() => SkillKeys().Select(key => SkillRow(key, SkillLevel(key))).ToList();

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
            entry.Key, entry.Level, StuntGrid(entry.Key), UniqueSkillCatalog.FirstOrDefault(definition => definition.Key == entry.Key)?.Skill ?? "",
        }), clear);
        string fighting = FightingSkill();
        if (Properties.Text("FightSkill") != fighting) SetFightSkill(fighting);
        else if (InWorld) SendStuntStates();
    }

    private int StuntGrid(string key)
    {
        BboStuntData? stunt = BboSkills.Stunt(key);
        if (stunt is not { Active: true }) return 0;
        int scenes = stunt.Flags & 0x1f;
        return scenes == 0 || (scenes & (UniqueSkillDefinition.OnLand | UniqueSkillDefinition.Workshop)) != 0 ? 1 : 2;
    }
    public void OpenSkillTutor()
    {
        CloseNpcDialogue();
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

    public void UseUniqueSkill(int row, int itemView = 0, int itemIndex = 0)
    {
        if (row < 0 || row >= Character.UniqueSkills.Count)
        {
            SystemMessage("You have not learned that special skill.");
            return;
        }
        UniqueSkillEntry entry = Character.UniqueSkills[row];
        UniqueSkillDefinition? definition = UniqueSkillCatalog.FirstOrDefault(candidate => candidate.Key == entry.Key);
        if (definition == null || Mover == null) return;
        if (!definition.Active)
        {
            SystemMessage("That special skill is passive: it works without being used.");
            return;
        }
        if (!StuntSceneAllows(definition))
        {
            SystemMessage(StuntSceneRefusal(definition));
            return;
        }
        if (definition.FightSkill.Length > 0 && FightingSkill() != definition.FightSkill)
        {
            SystemMessage("You are not holding the weapon that special skill needs.");
            return;
        }
        if (!StuntEquipmentFitted(definition))
        {
            SystemMessage(definition.Equipment == RamSlot
                ? "Your ship has no ram fitted."
                : "Your ship has no grappling hook fitted.");
            return;
        }
        if (_skillReady.TryGetValue(entry.Key, out DateTime ready) && ready > DateTime.UtcNow)
        {
            SystemMessage($"That special skill is ready again in {Math.Ceiling((ready - DateTime.UtcNow).TotalSeconds)} seconds.");
            return;
        }
        if (Sp < definition.SpCost)
        {
            SystemMessage($"That special skill needs {definition.SpCost} SP.");
            return;
        }
        if (!PerformStunt(definition, entry.Level, itemView, itemIndex)) return;

        SetSp(Sp - definition.SpCost);
        _skillReady[entry.Key] = DateTime.UtcNow.AddSeconds(definition.CooldownSeconds);
        Send(ChatPackets.Script(0, $"UniqueSkillReload {entry.Key} {definition.CooldownSeconds * 1000}"), "(special skill cooldown)");
        Log.Info(Tag, $"{Name} uses {entry.Key} (level {entry.Level})");
    }

    private bool StuntSceneAllows(UniqueSkillDefinition definition)
    {
        int flag = SceneType switch
        {
            0 => UniqueSkillDefinition.OnLand,
            1 => UniqueSkillDefinition.Offshore,
            _ => UniqueSkillDefinition.HighSea,
        };
        return (definition.Scenes & flag) != 0;
    }

    private static string StuntSceneRefusal(UniqueSkillDefinition definition)
    {
        if ((definition.Scenes & UniqueSkillDefinition.OnLand) != 0) return "That special skill can only be used on land.";
        if ((definition.Scenes & UniqueSkillDefinition.HighSea) != 0) return "That special skill can only be used at sea.";
        if ((definition.Scenes & UniqueSkillDefinition.Offshore) != 0) return "That special skill can only be used offshore, not on land or on the high sea.";
        return "That special skill can only be used in a boarding battle.";
    }

    private bool StuntEquipmentFitted(UniqueSkillDefinition definition)
    {
        if (definition.Equipment == 0) return true;
        if (definition.Equipment != AnyAuxiliarySlot) return ShipEquipmentArmType(definition.Equipment) >= 0;
        return Enumerable.Range(6, 3).Any(slot => ShipEquipmentArmType(slot) == ShipArmHook);
    }

    private bool PerformStunt(UniqueSkillDefinition definition, int level, int itemView, int itemIndex)
    {
        CombatRuleSet combat = Rule.Combat;
        switch (definition.Key)
        {
            case "US_SWORD1":
                return StrikeStunt(level, "OnUSSword1", "Hit");
            case "US_FALCHION1":
                return StrikeStunt(level, "OnUSFalchion1", "DF");
            case "US_AXE1":
                return StrikeStunt(level, "OnUSAxe1", "Dodge");
            case "US_SHOT1":
                return StrikeStunt(level, "OnUSShot1", "");
            case "US_SHOT3":
                return StrikeStunt(level, "", "", percent: 100 + combat.SnipingBonusPercentPerLevel * level);
            case "US_SWORD4":
                return StrikeStunt(level, "", "", hits: combat.SwordDanceHits);
            case "US_AXE4":
                return StrikeStunt(level, "", "", percent: combat.RageOfGodPercent, ignoreDefence: true);
            case "US_SWORD3":
                StartState("IsUSSword3", combat.StuntSeconds);
                return true;
            case "US_FALCHION3":
                StartState("IsUSFalchion3", combat.StuntSeconds);
                return true;
            case "US_FALCHION4":
                StartState("IsUSFalchion4", combat.StuntSeconds);
                return true;
            case "US_AXE3":
                StartState("IsUSAxe3", combat.StuntSeconds);
                return true;
            case "US_SHOT4":
                StartState("IsUSShot4", combat.HolyGunSeconds);
                return true;
            case "US_ATTACK":
                StartSeaStance(SeaStanceGuard, 30 + 5 * level);
                return true;
            case "US_CHASE":
                if (Sea.Find(SeaTarget, Scene, Room) is not { Dead: false })
                {
                    SystemMessage("Pursuit needs a target: attack a ship first.");
                    return false;
                }
                StartSeaStance(SeaStancePursuit, 10 + 2 * level);
                return true;
            case "US_SPEEDUP":
                int sailLimit = Sea.Data.Rules.FullSpeedSailPercent;
                if (sailLimit > 0 && 100 * ShipSail >= FleetSailMax * sailLimit)
                {
                    SystemMessage($"Full Speed can only be applied when Sail Durability is lower than {sailLimit}%.");
                    return false;
                }
                StartSeaStance(SeaStanceFullSpeed, 10 + 2 * level);
                return true;
            case "US_SEWING3":
                return RapidRepair(3, itemView, itemIndex);
            case "US_FOUNDRY3":
                return RapidRepair(0, itemView, itemIndex);
            case "US_PLANTING3":
                return HarvestNearby(level);
            default:
                SystemMessage("That special skill has no effect on this server yet.");
                return false;
        }
    }

    private bool StrikeStunt(int level, string targetState, string weakens, int hits = 1, int percent = 100, bool ignoreDefence = false)
    {
        MonsterDefinition? monster = SkillTarget();
        if (monster == null)
        {
            SystemMessage("There is no enemy within reach.");
            return false;
        }
        CombatRuleSet combat = Rule.Combat;
        MonsterState state = Context.World.Monster(monster);
        PrepareStuntStrike(monster);
        Context.World.Provoke(monster, this);
        if (weakens.Length > 0)
            state.Weakened[weakens] = (combat.DebuffPercentPerLevel * level, Environment.TickCount64 + combat.StuntSeconds * 1000L);
        if (targetState.Length > 0 && PropertyTable.Has(targetState))
        {
            byte[] marked = ScenePackets.ObjectProperties(monster.Id, new PropertyBag { [targetState] = 1 });
            byte[] cleared = ScenePackets.ObjectProperties(monster.Id, new PropertyBag { [targetState] = 0 });
            foreach (ClientSession session in Context.World.InScene(Scene)) session.Send(marked, "(stunt state)");
            int scene = Scene;
            Sea.Later(combat.StuntSeconds, () =>
            {
                foreach (ClientSession session in Context.World.InScene(scene)) session.Send(cleared, "(stunt state ends)");
            });
        }
        PlayAction(Context.Data.Actions.Attack, "(special skill)", attack: true);
        for (int hit = 0; hit < hits; hit++)
        {
            int dealt = CombatRules.Damage(combat, Properties.Int("AT"), ignoreDefence ? 0 : MonsterDefence(monster, state), percent);
            if (!Wound(monster, state, dealt, 0xff8000)) break;
        }
        return true;
    }

    private bool RapidRepair(int kind, int itemView, int itemIndex)
    {
        if (!Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn) || Character.Wear == null) return NothingToRepair();
        List<KeyValuePair<int, ItemStack>> damaged = worn
            .Where(pair => Character.Wear.ContainsKey(pair.Value.Name))
            .Where(pair => kind == 3 ? ItemCatalog.Number(pair.Value.Name, "Kind") == 3 : ItemCatalog.Number(pair.Value.Name, "Kind") is >= 5 and <= 8)
            .Where(pair => itemView != ViewId.Equipped || itemIndex == pair.Key)
            .ToList();
        if (damaged.Count == 0) return NothingToRepair();
        foreach ((int slot, ItemStack item) in damaged)
        {
            Character.Wear.Remove(item.Name);
            RefreshDurability(ViewId.Equipped, slot);
        }
        SystemMessage("Your equipment is as good as new.");
        Save();
        return true;
    }

    private bool NothingToRepair()
    {
        SystemMessage("Nothing you are wearing needs that repair.");
        return false;
    }

    private void SetKnownProperty(string name, object value)
    {
        if (PropertyTable.Has(name)) SetProperty(name, value);
        else Log.Debug(Tag, $"property {name} is not defined, not sent");
    }

    private IEnumerable<string> SkillKeys()
    {
        List<string> keys = Context.Data.Creation.Skills;
        return Character.Skills
            .Where(skill => skill.Id >= 0 && skill.Id < keys.Count)
            .Select(skill => keys[skill.Id])
            .Concat(Character.LearnedSkills.Keys);
    }

    private int SkillRowIndex(string key) => SkillKeys().ToList().IndexOf(key);

    private object[] SkillRow(string key, int level)
        => [key, level, Character.SkillExperience.GetValueOrDefault(key).ToString(), SkillExperienceMax(key).ToString(), SkillPoints(key)];

    private void SendTable(TableDefinition table, IEnumerable<object[]> rows, bool clear = true)
    {
        if (clear) Send(TablePackets.Clear(table), $"(clear {table.Name})");
        List<object[]> list = rows.ToList();
        if (list.Count > 0) Send(TablePackets.Rows(table, list), $"({table.Name})");
    }

    private string FightingSkill()
    {
        if (Character.Bags == null || !Character.Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn)) return BareHands;
        foreach (ItemStack item in worn.Values)
        {
            if (ItemCatalog.Text(item.Name, "NeedSkill") == "SKILL_BOW") return "SKILL_SHOT";
            switch (ItemCatalog.Number(item.Name, "Kind"))
            {
                case 5:
                    return "SKILL_SWORD";
                case 6:
                    return "SKILL_FALCHION";
                case 7:
                    return "SKILL_AXE";
                case 8:
                    return "SKILL_SHOT";
            }
        }
        return BareHands;
    }

    private void SendTutorLists()
    {
        string learn = string.Concat(Lessons.Where(lesson => SkillLevel(lesson.Key) <= 0).Select(lesson => $" {lesson.Key} {lesson.Price}"));
        string promote = string.Concat(SkillKeys().Where(key => !Character.AdvancedSkills.Contains(key)).Select(key => $" {key} {PromotionCost()}"));
        string unique = "";
        foreach (UniqueSkillDefinition definition in UniqueSkillCatalog.Where(definition => SkillLevel(definition.Skill) > 0).OrderBy(TutorOrder))
        {
            string entry = $" {definition.Skill} {definition.Key} {UniqueSkillLevel(definition.Key)} {UniqueSkillCost(definition.Key)}";
            if (unique.Length + entry.Length > TutorListLength) break;
            unique += entry;
        }
        string titles = string.Concat(SkillTitles.Select(title => $" {title.Key} {title.Value} {Rule.Skills.TitlePrice}"));
        Send(ChatPackets.Script(0, "laSkill" + learn), "(tutor skills)");
        Send(ChatPackets.Script(0, "laAdvSkill" + promote), "(tutor promotions)");
        Send(ChatPackets.Script(0, "laUSkill" + unique), "(tutor special skills)");
        Send(ChatPackets.Script(0, "laTitle" + titles), "(tutor titles)");
    }

    private int UniqueSkillCost(string key) => Rule.Skills.StuntPrice * (UniqueSkillLevel(key) + 1);

    private int TutorOrder(UniqueSkillDefinition definition)
    {
        if (UniqueSkillLevel(definition.Key) > 0) return 0;
        if (definition.Active) return 1;
        return LoadStunts.Contains(definition.Key) || MaxSpStunts.Contains(definition.Key) || OtherWorkingStunts.Contains(definition.Key) ? 2 : 3;
    }

    private bool Pay(int price)
    {
        if (Money < price)
        {
            SystemMessage("Not enough coins to learn the skill/promote!");
            return false;
        }
        SetMoney(Money - price);
        return true;
    }

    private bool PayPoints(int points)
    {
        if (Character.GamePoints < points)
        {
            SystemMessage($"That costs {points} points and you have {Character.GamePoints}.");
            return false;
        }
        Character.GamePoints -= points;
        return true;
    }

    private void TutorDone(string what)
    {
        Save();
        SendSkillState();
        SendTutorLists();
        RefreshStats();
        Log.Info(Tag, $"{Name} {what}");
    }

    private void LearnSkill(string key)
    {
        SkillLesson? lesson = Lessons.FirstOrDefault(candidate => candidate.Key == key);
        if (lesson == null || SkillLevel(key) > 0)
        {
            SystemMessage("You cannot learn that skill here.");
            return;
        }
        if (lesson.AnyBattleSkill && !BattleSkills.Any(skill => SkillLevel(skill) >= lesson.RequiredLevel))
        {
            SystemMessage($"Learning that skill requires one of the battle skills above Level {lesson.RequiredLevel - 1}. You can not learn this skill at present.");
            return;
        }
        if (lesson.Requires.Length > 0 && SkillLevel(lesson.Requires) < lesson.RequiredLevel)
        {
            SystemMessage($"You have not reached Level {lesson.RequiredLevel} in the skill this one builds on. Come and learn it when you meet the requirement.");
            return;
        }
        if (!Pay(lesson.Price)) return;
        if (Context.Data.Creation.Skills.IndexOf(key) is >= 0 and int id) Character.Skills.Add(new SkillEntry { Id = id, Level = 1 });
        else Character.LearnedSkills[key] = 1;
        if (Rule.Skills.GrantedStunts.TryGetValue(key, out string? stunt) && UniqueSkillLevel(stunt) == 0)
            Character.UniqueSkills.Add(new UniqueSkillEntry { Key = stunt, Level = 1 });
        if (Rule.Skills.GrantedTools.TryGetValue(key, out string? tool) && HasRoomFor(tool))
            GiveItem(new ItemStack { Name = tool, Photo = Context.Data.PhotoOf(tool), Amount = 1 }, "(tutor's tool)");
        SystemMessage($"You learned {ClientNames.Of(Context.Settings, key)}.", SystemMessageKind.Upgrade);
        Send(UiPackets.Custom($"scUpLevel {TotalSkillLevel}"), "(skill learned)");
        TutorDone($"learns {key}");
    }

    private void PromoteSkill(string key)
    {
        int level = SkillLevel(key);
        if (level <= 0)
        {
            SystemMessage("That skill cannot be promoted.");
            return;
        }
        if (Character.AdvancedSkills.Contains(key))
        {
            SystemMessage("This skill is already phase 2 skill, can not be promoted!");
            return;
        }
        SkillRules rules = Rule.Skills;
        if (level < rules.PhaseOneCap)
        {
            SystemMessage($"This skill has not reached Level {rules.PhaseOneCap}, cannot be promoted to phase 2 skill.");
            return;
        }
        if (Character.AdvancedSkills.Count >= rules.PromotedSkillLimit)
        {
            SystemMessage("Phase 2 skills have reached maximum amount. So this skill cannot be promoted to phase 2 skill!");
            return;
        }
        if (!Pay(PromotionCost())) return;
        Character.AdvancedSkills.Add(key);
        SystemMessage($"{ClientNames.Of(Context.Settings, key)} is now a phase 2 skill.", SystemMessageKind.Upgrade);
        TutorDone($"promotes {key}");
    }

    private void DemoteSkill(string key)
    {
        if (!Character.AdvancedSkills.Contains(key))
        {
            SystemMessage("That skill has not been promoted.");
            return;
        }
        if (!PayPoints(Rule.Skills.DemotePoints)) return;
        Character.AdvancedSkills.Remove(key);
        SetSkillLevel(key, Math.Min(SkillLevel(key), Rule.Skills.PhaseOneCap));
        Character.SkillExperience.Remove(key);
        Character.UniqueSkills.RemoveAll(entry => UniqueSkillCatalog.Any(definition => definition.Key == entry.Key && definition.Skill == key));
        TutorDone($"demotes {key}");
    }

    private void LearnUniqueSkill(string key)
    {
        UniqueSkillDefinition? definition = UniqueSkillCatalog.FirstOrDefault(candidate => candidate.Key == key);
        if (definition == null || SkillLevel(definition.Skill) <= 0)
        {
            SystemMessage("No special skill available for the current skill.");
            return;
        }
        UniqueSkillEntry? entry = Character.UniqueSkills.FirstOrDefault(candidate => candidate.Key == key);
        int next = (entry?.Level ?? 0) + 1;
        int tutorCap = Context.Data.Npcs.TryGetValue(TalkingTo, out NpcDefinition? tutor) ? tutor.StuntCap : Rule.Skills.StuntLevelMax;
        if (next > StuntLevelMax(key) || next > tutorCap)
        {
            SystemMessage("I have got no higher special skill to teach you now. You have to look for others.");
            return;
        }
        int needed = StuntSkillLevelFor(key, next);
        if (SkillLevel(definition.Skill) < needed)
        {
            SystemMessage(entry == null
                ? $"This stunt is not opened yet: the skill it belongs to must reach Level {needed}."
                : $"Insufficient level to upgrade the stunt: the skill it belongs to must reach Level {needed}.");
            return;
        }
        if (SkillPoints(definition.Skill) < 1)
        {
            SystemMessage("Not enough skill points to learn and upgrade special skill!");
            return;
        }
        if (Money < UniqueSkillCost(key))
        {
            SystemMessage("Not enough silver coins to learn the special skill / promote.");
            return;
        }
        SetMoney(Money - UniqueSkillCost(key));
        if (entry == null) Character.UniqueSkills.Add(new UniqueSkillEntry { Key = key, Level = 1 });
        else entry.Level++;
        SystemMessage(entry == null ? $"You learned {ClientNames.Of(Context.Settings, key)}." : $"{ClientNames.Of(Context.Settings, key)} reached level {UniqueSkillLevel(key)}.", SystemMessageKind.Upgrade);
        TutorDone($"raises {key} to {UniqueSkillLevel(key)}");
    }

    private void ForgetUniqueSkill(string key)
    {
        if (UniqueSkillLevel(key) <= 0)
        {
            SystemMessage("You have not learned that special skill.");
            return;
        }
        if (!PayPoints(Rule.Skills.ForgetStuntPoints)) return;
        Character.UniqueSkills.RemoveAll(entry => entry.Key == key);
        _skillReady.Remove(key);
        TutorDone($"forgets {key}");
    }

    private void ApplyForTitle(string key)
    {
        if (!SkillTitles.TryGetValue(key, out string? title) || SkillLevel(key) <= 0)
        {
            SystemMessage("No title available!");
            return;
        }
        if (Character.Titles.Contains(title))
        {
            SystemMessage("There is no other higher-Level Title that you can apply now.");
            return;
        }
        if (Money < Rule.Skills.TitlePrice)
        {
            SystemMessage("Not enough silver coins to upgrade the title.");
            return;
        }
        SetMoney(Money - Rule.Skills.TitlePrice);
        GrantTitle(title);
        SendTutorLists();
    }

    public void GrantTitle(string title)
    {
        if (Character.Titles.Contains(title)) return;
        Character.Titles.Add(title);
        Save();
        SendTable(GameTables.TitleList, Character.Titles.Select(name => new object[] { name }));
        SendBboTitles();
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
        float reach = AttackReach() + Rule.Combat.ReachSlack;
        List<MonsterDefinition> near = Context.Data.Monsters.Values
            .Where(monster => monster.Scene == Scene && !Context.World.Monster(monster).Dead)
            .Where(monster => Distance(monster, x, z) <= reach)
            .ToList();
        return near.FirstOrDefault(monster => monster.Id == CombatTarget) ?? near.MinBy(monster => Distance(monster, x, z));
    }

    private float Distance(MonsterDefinition monster, float x, float z)
    {
        MonsterState state = Context.World.Monster(monster);
        return MathF.Sqrt((state.X - x) * (state.X - x) + (state.Z - z) * (state.Z - z));
    }
}
