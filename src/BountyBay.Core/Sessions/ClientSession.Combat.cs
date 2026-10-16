using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private static readonly string[] StatProperties = ["MaxHP", "MaxSP", "AT", "DF", "HitRatio", "DodgeRatio", "Level"];

    private static readonly string[] StatStates = ["IsUSSword3", "IsUSFalchion3", "IsUSAxe3"];

    private long _nextAttackAt;

    private GameRules Rule => Context.Data.Rules;

    public int TotalSkillLevel => SkillKeys().Sum(SkillLevel);

    public void SetHp(int hp)
    {
        Hp = Math.Clamp(hp, 0, MaxHp);
        SetProperty("HP", Hp, visibleToOthers: true);
        SetProperty("RatioHP", 100 * Hp / Math.Max(1, MaxHp), visibleToOthers: true);
    }

    public void Regenerate()
    {
        if (!InWorld || Hp <= 0) return;
        CharacterRules rules = Rule.Character;
        if (Hp < MaxHp)
            SetHp(Hp + Math.Max(1, (int)((long)MaxHp * rules.HpRegenerationPermille * (100 + 25 * UniqueSkillLevel("US_HAND3")) / 100000)));
        if (Sp < MaxSp) SetSp(Sp + Math.Max(1, MaxSp * rules.SpRegenerationPermille / 1000));
    }

    public void SetReputation(long reputation)
    {
        Character.Reputation = (int)Math.Clamp(reputation, 0, int.MaxValue);
        SetProperty("Credit", Character.Reputation, visibleToOthers: true);
    }

    public void GainReputation(int amount)
    {
        if (amount <= 0) return;
        SetReputation((long)Character.Reputation + amount);
        SystemMessage($"Your reputation rises by {amount}.");
    }

    public void LoseReputation(int percent)
    {
        int lost = (int)((long)Character.Reputation * Math.Clamp(percent, 0, 100) / 100);
        if (lost <= 0) return;
        SetReputation(Character.Reputation - lost);
        SystemMessage($"Your reputation falls by {lost}.");
    }

    private int WornSum(string property)
    {
        if (Character.Bags == null || !Character.Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn)) return TotemBonus(property) + RankArmWornSum(property);
        return worn.Values
            .Where(item => DurabilityMax(item.Name) <= 0 || Durability(item.Name) > 0)
            .Sum(item => ItemCatalog.Number(item.Name, property) + ArmUpgradeBonus(item, property) + ArmRebuildBonus(item, property)) + TotemBonus(property) + RankArmWornSum(property);
    }

    private void ComputeStats()
    {
        CharacterRules body = Rule.Character;
        CombatRuleSet combat = Rule.Combat;
        string fight = FightingSkill();
        int skill = SkillLevel(fight);
        int bare = SkillLevel(BareHands);
        MaxHp = body.BaseHp + body.HpPerBareHandedLevel * bare + WornSum("AddMaxHP");
        MaxSp = body.BaseSp + body.SpPerMedicineLevel * SkillLevel(Medicine) + body.SpPerSpiritStuntLevel * StuntLevels(MaxSpStunts)
            + WornSum("AddMaxSP");

        int attack = combat.BaseAttack + skill + WornSum("AddAT");
        if (StateActive("IsUSSword3")) attack = attack * (100 + combat.BuffPercentPerLevel * UniqueSkillLevel("US_SWORD3")) / 100;

        int stancePenalty = combat.DefencePercent.GetValueOrDefault(fight);
        int percent = 100 + stancePenalty + combat.DefencePromotionPercentPerLevel * UniqueSkillLevel("US_HAND2");
        int flat = 0;
        if (StateActive("IsUSAxe3"))
        {
            percent -= Math.Min(0, stancePenalty);
            flat = combat.DefensiveStanceDefencePerLevel * Math.Max(0, UniqueSkillLevel("US_AXE3") - 1);
        }
        int hit = combat.BaseHitRatio + skill + combat.HitBonus.GetValueOrDefault(fight) + WornSum("AddHitRatio");
        if (StateActive("IsUSFalchion3"))
        {
            percent -= combat.DebuffPercentPerLevel * UniqueSkillLevel("US_FALCHION3");
            hit += combat.BuffPercentPerLevel * UniqueSkillLevel("US_FALCHION3");
        }
        int defence = WornSum("AddDF") + (fight == BareHands ? bare / 2 : 0);

        Properties["MaxHP"] = MaxHp;
        Properties["MaxSP"] = MaxSp;
        Properties["AT"] = Math.Max(1, attack);
        Properties["DF"] = Math.Max(0, defence * Math.Max(0, percent) / 100 + flat);
        Properties["HitRatio"] = Math.Max(0, hit);
        Properties["DodgeRatio"] = Math.Max(0, bare + WornSum("AddDodgeRatio"));
        Properties["Level"] = TotalSkillLevel;
        ComputeBboStats();
        RefreshBboActionSet();
    }

    private int MonsterDefence(MonsterDefinition monster, MonsterState state)
        => CombatRules.Reduced(monster.Defence, state.WeakenedBy("DF"));

    private int MonsterDodge(MonsterDefinition monster, MonsterState state)
        => CombatRules.Reduced(CombatRules.MonsterDodge(Rule.Combat, monster), state.WeakenedBy("Dodge"));

    private int MonsterHit(MonsterDefinition monster, MonsterState state)
        => CombatRules.Reduced(CombatRules.MonsterHit(Rule.Combat, monster), state.WeakenedBy("Hit"));

    public void Attack(MonsterDefinition monster)
    {
        MonsterState state = Context.World.Monster(monster);
        if (state.Dead || Mover == null || Hp <= 0) return;
        long now = Environment.TickCount64;
        if (now < _nextAttackAt) return;
        CombatRuleSet rules = Rule.Combat;
        string fight = FightingSkill();
        _nextAttackAt = now + rules.AttackMilliseconds.GetValueOrDefault(fight);
        EndState("IsUSAxe3");
        PlayAction(BboAttackAction(), "(attack swing)", attack: true);
        WearEquipment();
        Context.World.Provoke(monster, this);

        if (!StateActive("IsUSShot4") && !CombatRules.Hits(rules, Properties.Int("HitRatio"), MonsterDodge(monster, state)))
        {
            ShowBboMiss(monster.Id);
            return;
        }
        int dealt = CombatRules.Damage(rules, Properties.Int("AT"), MonsterDefence(monster, state));
        uint colour = 0xffff00;
        dealt = BboOutgoingDamage(dealt, ref colour);
        if (fight == "SKILL_SWORD" && !StateActive("IsUSSword3")
            && CombatRules.Chance(rules.PassiveChancePercentPerLevel * UniqueSkillLevel("US_SWORD2")))
        {
            dealt = Math.Max(1, dealt * rules.CriticalDamagePercent / 100);
            colour = 0xff8000;
        }
        if (!Wound(monster, state, dealt, colour)) return;
        TotemCharge(true);

        string second = fight switch
        {
            "SKILL_FALCHION" => "US_FALCHION2",
            "SKILL_SHOT" => "US_SHOT2",
            _ => "",
        };
        if (second.Length == 0 || !CombatRules.Chance(rules.PassiveChancePercentPerLevel * UniqueSkillLevel(second))) return;
        Wound(monster, state, CombatRules.Damage(rules, Properties.Int("AT"), MonsterDefence(monster, state), rules.SecondHitPercent), 0xff8000);
    }

    public void TakeMonsterHit(MonsterDefinition monster, MonsterState state)
    {
        if (Mover == null || Hp <= 0 || AtSea || Scene != monster.Scene) return;
        CombatRuleSet rules = Rule.Combat;
        MarkAttacked();
        byte[] swing = ScenePackets.PlayAction(monster.Id, BboMonsterAttackAction(), attack: true);
        Send(swing, "(monster swings)");
        ToOthers(swing, "(monster swings)");
        if (!CombatRules.Hits(rules, MonsterHit(monster, state), Properties.Int("DodgeRatio")))
        {
            ShowBboDodge(state.X, monster.Y, state.Z);
            return;
        }
        int taken = CombatRules.Damage(rules, monster.Attack, Properties.Int("DF"));
        TotemCharge(false);
        taken = BboIncomingDamage(taken);
        if (FightingSkill() == "SKILL_AXE" && CombatRules.Chance(rules.PassiveChancePercentPerLevel * UniqueSkillLevel("US_AXE2")))
            taken = Math.Max(1, taken * rules.BlockDamagePercent / 100);
        Send(ChatPackets.FloatingText(ProtocolConstants.SelfObjectId, $"-{taken}", 0xff0000, "wound", state.X, monster.Y, state.Z), "(damage number)");
        ToOthers(ChatPackets.FloatingText(ObjectId, $"-{taken}", 0xff0000, "wound", state.X, monster.Y, state.Z), "(damage number)");
        if (Hp - taken > 0)
        {
            SetHp(Hp - taken);
            if (StateActive("IsUSFalchion4") && !state.Dead)
                Wound(monster, state, CombatRules.Damage(rules, Properties.Int("AT"), MonsterDefence(monster, state), rules.CounterDamagePercent), 0xff8000);
            return;
        }

        if (BattleKnockedOut())
        {
            state.Target = null;
            return;
        }
        state.Target = null;
        LoseReputation(Rule.Character.DeathReputationLossPercent);
        SetHp(MaxHp);
        SystemMessage("You were knocked out and wake up in your home port.");
        Log.Info(Tag, $"{Name} was knocked out by {monster.Name}");
        Save();
        ForceLand = false;
        Travel(WorldSettings.StartScene);
    }

    private void ShowMiss(int objectId)
    {
        byte[] miss = ChatPackets.FloatingText(objectId, "Miss", 0xffffff);
        Send(miss, "(miss)");
        ToOthers(miss, "(miss)");
    }

    private void GainFightExperience(int points)
    {
        if (points <= 0) return;
        SkillRules rules = Rule.Skills;
        long amount = (long)points * rules.CombatExperiencePerDamage;
        if (Team is { } team && team.Members.Any(member => member != this && member.Scene == Scene))
            amount = amount * (100 + rules.PartyExperiencePercent) / 100;
        string fight = FightingSkill();
        GainSkillExperience(fight, amount);
        if (fight != BareHands) GainSkillExperience(BareHands, amount * rules.BareHandedSharePercent / 100);
    }

    private bool Wound(MonsterDefinition monster, MonsterState state, int dealt, uint colour)
    {
        if (state.Dead) return false;
        BattleDamageDealt(monster, Math.Min(dealt, Math.Max(0, state.Hp)));
        GainFightExperience(Math.Min(dealt, Math.Max(0, state.Hp)) * (monster.Level + 1));
        state.Hp -= dealt;
        List<ClientSession> everyone = [this, .. Others()];
        (float x, float z) = Mover!.Now();
        byte[] number = ChatPackets.FloatingText(monster.Id, $"-{dealt}", colour, "wound", x, Height, z);
        foreach (ClientSession session in everyone) session.Send(number, "(damage number)");

        if (state.Hp > 0)
        {
            byte[] update = ScenePackets.ObjectProperties(monster.Id, new PropertyBag
            {
                ["HP"] = state.Hp,
                ["RatioHP"] = 100 * state.Hp / Math.Max(1, monster.Hp),
            });
            foreach (ClientSession session in everyone) session.Send(update, "(monster hp)");
            return true;
        }

        state.Dead = true;
        state.Target = null;
        foreach (ClientSession session in everyone)
            session.Send(ScenePackets.RemoveObject(monster.Id), "(monster dies)");
        if (monster.Gold > 0)
        {
            SetMoney(Money + monster.Gold);
            SystemMessage($"You defeated the {monster.Name} and found {monster.Gold} silver coins.");
        }
        else
        {
            SystemMessage($"You defeated the {monster.Name}.");
        }
        Save();
        Log.Info(Tag, $"{Name} killed {monster.Name}");
        Context.World.ScheduleRespawn(monster);
        OnMonsterKilled(monster.Id);
        BattleMonsterKilled(monster);
        DropLoot(monster);
        return false;
    }
}
