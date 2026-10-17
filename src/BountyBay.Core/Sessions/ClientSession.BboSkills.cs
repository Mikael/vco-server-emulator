using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class BboStuntData
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Skill { get; set; } = "";
    public string Photo { get; set; } = "";
    public bool Active { get; set; }
    public int Flags { get; set; }
    public string FightSkill { get; set; } = "";
    public int Equipment { get; set; }
    public int CooldownSeconds { get; set; }
    public int SpCost { get; set; }
    public string Buff { get; set; } = "";
    public string TargetState { get; set; } = "";
    public string SelfState { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetEffect { get; set; } = "";
    public List<string> Items { get; set; } = [];
}

public sealed class BboActionIndexes
{
    public Dictionary<string, int?> Named { get; set; } = [];
    public Dictionary<string, List<int?>> Lists { get; set; } = [];
}

public sealed class BboActionSetData
{
    public int ActionSet { get; set; }
    public string Weapon { get; set; } = "";
}

public sealed class BboToolData
{
    public string Skill { get; set; } = "";
    public int ActionSet { get; set; }
}

public sealed class BboSkillFile
{
    public List<int> PhaseCaps { get; set; } = [];
    public List<string> SkillWindowSlots { get; set; } = [];
    public List<BboStuntData> Stunts { get; set; } = [];
    public List<BboStuntData> TableStunts { get; set; } = [];
    public BboActionIndexes Actions { get; set; } = new();
    public List<BboActionSetData> ActionSets { get; set; } = [];
    public Dictionary<string, BboToolData> Tools { get; set; } = [];
    public int StuntResetPrice { get; set; }
}

public sealed class BboSkillBook
{
    private static readonly object Gate = new();
    private static BboSkillBook? _current;

    private readonly Dictionary<string, BboStuntData> _stunts;

    private BboSkillBook(BboSkillFile file)
    {
        File = file;
        _stunts = file.Stunts.Concat(file.TableStunts).Where(stunt => stunt.Key.Length > 0)
            .GroupBy(stunt => stunt.Key).ToDictionary(group => group.Key, group => group.First());
        Attacks = List("attack");
        Skills = List("skill");
    }

    public BboSkillFile File { get; }

    public IReadOnlyList<int> Attacks { get; }

    public IReadOnlyList<int> Skills { get; }

    public static BboSkillBook For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\bbo\\skills.json"));
    }

    public BboStuntData? Stunt(string key) => _stunts.GetValueOrDefault(key);

    public int Action(string name, int fallback)
        => File.Actions.Named.TryGetValue(name, out int? index) && index is int found ? found : fallback;

    public int SkillAction(int number, int fallback) => number >= 1 && number <= Skills.Count ? Skills[number - 1] : fallback;

    public int WeaponActionSet(string fightSkill)
        => File.ActionSets.FirstOrDefault(set => set.Weapon == fightSkill)?.ActionSet ?? 0;

    public BboToolData? Tool(string item) => File.Tools.GetValueOrDefault(item);

    private List<int> List(string name)
        => File.Actions.Lists.TryGetValue(name, out List<int?>? values) ? values.Where(value => value.HasValue).Select(value => value!.Value).ToList() : [];

    private static BboSkillBook Read(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            Log.Warn("BboSkills", $"game data file is missing: {path}");
            return new BboSkillBook(new BboSkillFile());
        }
        try
        {
            BboSkillFile file = JsonSerializer.Deserialize<BboSkillFile>(System.IO.File.ReadAllText(path), EmulatorSettings.Json) ?? new BboSkillFile();
            Log.Info("BboSkills", $"{file.Stunts.Count} stunt(s), {file.TableStunts.Count} table stunt(s), {file.Actions.Named.Count} action name(s) loaded");
            return new BboSkillBook(file);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("BboSkills", $"Could not read {path}: {exception.Message}");
            return new BboSkillBook(new BboSkillFile());
        }
    }
}

public sealed partial class ClientSession
{
    private const uint StuntHitColour = 0xff8000;

    private long _bboGatherUntil;

    private BboSkillBook BboSkills => BboSkillBook.For(Context.Settings);

    public void UseBboUniqueSkill(int row, int itemView = 0, int itemIndex = 0)
    {
        if (row < 0 || row >= Character.UniqueSkills.Count)
        {
            SystemMessage("You have not learned that special skill.");
            return;
        }
        UniqueSkillEntry entry = Character.UniqueSkills[row];
        BboStuntData? stunt = BboSkills.Stunt(entry.Key);
        if (stunt == null || Mover == null)
        {
            Log.Debug(Tag, $"{Name} uses unknown stunt {entry.Key}");
            return;
        }
        if (!stunt.Active || stunt.CooldownSeconds <= 0)
        {
            SystemMessage("That special skill is passive: it works without being used.");
            return;
        }
        var definition = new UniqueSkillDefinition(stunt.Key, stunt.Skill, stunt.Flags & 0x1f, stunt.FightSkill, stunt.Equipment,
            stunt.CooldownSeconds, stunt.SpCost);
        if (!StuntSceneAllows(definition))
        {
            SystemMessage(StuntSceneRefusal(definition));
            return;
        }
        if (stunt.FightSkill.Length > 0 && FightingSkill() != stunt.FightSkill)
        {
            SystemMessage("You are not holding the weapon that special skill needs.");
            return;
        }
        if (!StuntEquipmentFitted(definition))
        {
            SystemMessage(stunt.Equipment == RamSlot ? "Your ship has no ram fitted." : "Your ship has no grappling hook fitted.");
            return;
        }
        if (_skillReady.TryGetValue(stunt.Key, out DateTime ready) && ready > DateTime.UtcNow)
        {
            SystemMessage($"That special skill is ready again in {Math.Ceiling((ready - DateTime.UtcNow).TotalSeconds)} seconds.");
            return;
        }
        if (Sp < stunt.SpCost)
        {
            SystemMessage($"That special skill needs {stunt.SpCost} SP.");
            return;
        }
        if (!PerformBboStunt(stunt, definition, entry.Level, itemView, itemIndex)) return;

        SetSp(Sp - stunt.SpCost);
        _skillReady[stunt.Key] = DateTime.UtcNow.AddSeconds(stunt.CooldownSeconds);
        Send(BboSkillPackets.UniqueSkillReload(stunt.Key, stunt.CooldownSeconds * 1000), "(special skill cooldown)");
        Log.Info(Tag, $"{Name} uses {stunt.Key} (level {entry.Level}, {stunt.SpCost} SP, {stunt.CooldownSeconds} s)");
    }

    public void ResetBboStunts(string skill)
    {
        if (SkillLevel(skill) <= 0)
        {
            SystemMessage("You have not learned that skill.");
            return;
        }
        List<UniqueSkillEntry> related = Character.UniqueSkills
            .Where(entry => (BboSkills.Stunt(entry.Key)?.Skill ?? UniqueSkillCatalog.FirstOrDefault(definition => definition.Key == entry.Key)?.Skill) == skill)
            .ToList();
        if (related.Count == 0)
        {
            SystemMessage("You have no special skills of that skill to erase.");
            return;
        }
        int price = BboSkills.File.StuntResetPrice;
        if (Money < price)
        {
            SystemMessage("Not enough silver coins to erase the special skills.");
            return;
        }
        SetMoney(Money - price);
        Character.UniqueSkills.RemoveAll(related.Contains);
        foreach (UniqueSkillEntry entry in related) _skillReady.Remove(entry.Key);
        TutorDone($"erases {related.Count} stunt(s) of {skill} for {price}");
    }

    public void SendBboStuntCooldowns()
    {
        DateTime now = DateTime.UtcNow;
        foreach ((string key, DateTime ready) in _skillReady.Where(pair => pair.Value > now))
            Send(BboSkillPackets.UniqueSkillReload(key, (int)(ready - now).TotalMilliseconds), "(special skill cooldown)");
    }

    public int BboActionSet()
    {
        string fight = FightingSkill();
        if (fight != BareHands) return BboSkills.WeaponActionSet(fight);
        if (Character.Bags == null || !Character.Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn)) return 0;
        return worn.Values.Select(item => BboSkills.Tool(item.Name)).FirstOrDefault(tool => tool is { ActionSet: > 0 })?.ActionSet ?? 0;
    }

    public void RefreshBboActionSet()
    {
        if (!PropertyTable.Has("ActionSet")) return;
        int set = BboActionSet();
        if (Properties.Int("ActionSet", -1) == set) return;
        if (InWorld) SetProperty("ActionSet", set, visibleToOthers: true);
        else Properties["ActionSet"] = set;
    }

    public int BboAttackAction()
    {
        IReadOnlyList<int> attacks = BboSkills.Attacks;
        return attacks.Count > 0 ? attacks[Random.Shared.Next(attacks.Count)] : Context.Data.Actions.Attack;
    }

    public int BboMonsterAttackAction()
    {
        IReadOnlyList<int> attacks = BboSkills.Attacks;
        return attacks.Count >= 2 ? attacks[Random.Shared.Next(2)] : Context.Data.Actions.Attack;
    }

    public void ShowBboMiss(int objectId)
    {
        (float x, float z) = Mover?.Now() ?? (0f, 0f);
        byte[] miss = BboSkillPackets.Hit(objectId, "Miss", 0xffffff, BboSkillPackets.DodgeEffect, "", x, Height, z);
        Send(miss, "(miss)");
        ToOthers(miss, "(miss)");
    }

    public void ShowBboDodge(float fromX, float fromY, float fromZ)
    {
        Send(BboSkillPackets.Hit(ProtocolConstants.SelfObjectId, "Miss", 0xffffff, BboSkillPackets.DodgeEffect, "", fromX, fromY, fromZ), "(dodge)");
        ToOthers(BboSkillPackets.Hit(ObjectId, "Miss", 0xffffff, BboSkillPackets.DodgeEffect, "", fromX, fromY, fromZ), "(dodge)");
    }

    public bool BeginBboGathering(string skill, int seconds, Action finished)
    {
        if (Mover == null || AtSea) return false;
        long now = Environment.TickCount64;
        if (_bboGatherUntil > now)
        {
            SystemMessage("You are already busy gathering.");
            return false;
        }
        if (SkillLevel(skill) <= 0)
        {
            SystemMessage("You have not learned the skill needed for that.");
            return false;
        }
        BboToolData? tool = EquippedBboTool(skill);
        if (tool == null)
        {
            SystemMessage("You need to equip the right tool first.");
            return false;
        }
        int action = BboSkills.Action(skill == "SKILL_PLANTING" ? "plant" : "gather", Context.Data.Actions.Attack);
        int milliseconds = Math.Max(1, seconds) * 1000;
        if (PropertyTable.Has("ActionSet") && tool.ActionSet > 0 && Properties.Int("ActionSet", -1) != tool.ActionSet)
            SetProperty("ActionSet", tool.ActionSet, visibleToOthers: true);
        (float x, float z) = Mover.Now();
        Mover.MoveTo(x, z);
        Send(BboSkillPackets.TimedAction(ProtocolConstants.SelfObjectId, action, milliseconds), "(gathering)");
        ToOthers(BboSkillPackets.TimedAction(ObjectId, action, milliseconds), "(gathering)");
        _bboGatherUntil = now + milliseconds;
        Sea.Later(seconds, () =>
        {
            _bboGatherUntil = 0;
            RefreshBboActionSet();
            if (InWorld) finished();
        });
        Log.Info(Tag, $"{Name} gathers with {skill} for {seconds} s (action {action})");
        return true;
    }

    private BboToolData? EquippedBboTool(string skill)
    {
        if (Character.Bags == null || !Character.Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn)) return null;
        return worn.Values.Select(item => BboSkills.Tool(item.Name)).FirstOrDefault(tool => tool != null && tool.Skill == skill);
    }

    private bool PerformBboStunt(BboStuntData stunt, UniqueSkillDefinition definition, int level, int itemView, int itemIndex)
    {
        CombatRuleSet combat = Rule.Combat;
        bool done = stunt.Key switch
        {
            "US_SWORD1" => BboStrike(stunt, level, "Hit"),
            "US_FALCHION1" => BboStrike(stunt, level, "DF"),
            "US_AXE1" => BboStrike(stunt, level, "Dodge"),
            "US_SHOT1" => BboStrike(stunt, level, ""),
            "US_SHOT3" => BboStrike(stunt, level, "", percent: 100 + combat.SnipingBonusPercentPerLevel * level),
            "US_SWORD4" => BboStrike(stunt, level, "", hits: combat.SwordDanceHits),
            "US_AXE4" => BboStrike(stunt, level, "", percent: combat.RageOfGodPercent, ignoreDefence: true),
            _ => PerformStunt(definition, level, itemView, itemIndex),
        };
        if (done && stunt.Key is not ("US_SWORD1" or "US_FALCHION1" or "US_AXE1" or "US_SHOT1" or "US_SHOT3" or "US_SWORD4" or "US_AXE4"))
            PlayBboStuntAction(stunt);
        return done;
    }

    private void PlayBboStuntAction(BboStuntData stunt)
    {
        if (stunt.Action.Length == 0 || Mover == null) return;
        int number = int.TryParse(stunt.Action.AsSpan(stunt.Action.LastIndexOf('_') + 1), out int parsed) ? parsed : 0;
        int action = BboSkills.SkillAction(number, -1);
        if (action >= 0) PlayAction(action, $"(stunt {stunt.Key})");
    }

    private bool BboStrike(BboStuntData stunt, int level, string weakens, int hits = 1, int percent = 100, bool ignoreDefence = false)
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
        PlayBboStuntAction(stunt);
        if (stunt.TargetEffect.Length > 0)
        {
            (float x, float z) = Mover!.Now();
            byte[] effect = BboSkillPackets.Effect(monster.Id, stunt.TargetEffect, x, Height, z);
            foreach (ClientSession session in Context.World.InScene(Scene)) session.Send(effect, "(stunt effect)");
        }
        for (int hit = 0; hit < hits; hit++)
        {
            int dealt = CombatRules.Damage(combat, Properties.Int("AT"), ignoreDefence ? 0 : MonsterDefence(monster, state), percent);
            if (!Wound(monster, state, dealt, StuntHitColour)) break;
        }
        return true;
    }
}
