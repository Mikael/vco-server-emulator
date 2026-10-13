using VoyageCentury.Core.Data;

namespace VoyageCentury.Core.Sessions;

public readonly record struct VolleyDamage(int Hull, int Sail, int Sailors);

public static class CombatRules
{
    public static bool Chance(int percent) => percent > 0 && Random.Shared.Next(100) < percent;

    public static int HitPercent(CombatRuleSet rules, int hit, int dodge)
        => Math.Clamp(hit - dodge, Math.Clamp(rules.HitFloorPercent, 0, 100), 100);

    public static bool Hits(CombatRuleSet rules, int hit, int dodge) => Chance(HitPercent(rules, hit, dodge));

    public static int Damage(CombatRuleSet rules, int attack, int defence, int percent = 100)
    {
        attack = Math.Max(1, attack);
        defence = Math.Max(0, defence);
        double raw = (double)attack * attack / (attack + defence);
        double spread = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * Math.Max(0, rules.DamageSpreadPercent) / 100.0;
        return Math.Max(1, (int)Math.Round(raw * spread * percent / 100.0));
    }

    public static int Reduced(int value, int percent) => Math.Max(0, value * Math.Max(0, 100 - percent) / 100);

    public static int MonsterHit(CombatRuleSet rules, MonsterDefinition monster)
        => monster.Hit > 0 ? monster.Hit : rules.MonsterHitBase + rules.MonsterRatingPerTier * monster.Level;

    public static int MonsterDodge(CombatRuleSet rules, MonsterDefinition monster)
        => monster.Dodge > 0 ? monster.Dodge : rules.MonsterDodgeBase + rules.MonsterRatingPerTier * monster.Level;

    public static int SeaHitPercent(ShipRules rules, int cannonFightLevel, int penalty)
        => Math.Clamp(rules.BaseHitPercent + rules.HitPercentPerCannonFightLevel * cannonFightLevel - penalty, 5, 100);

    public static int SeaHits(int balls, int percent)
    {
        int hits = 0;
        for (int i = 0; i < balls; i++)
        {
            if (Chance(percent)) hits++;
        }
        return hits;
    }

    public static VolleyDamage Volley(int hits, int cannonAttack, AmmunitionDefinition ball, int armourHull, int armourSail, int percent = 100)
    {
        if (hits <= 0) return new VolleyDamage(0, 0, 0);
        double power = (double)hits * Math.Max(0, cannonAttack) * percent / 100.0;
        int hull = ball.Hull <= 0 ? 0 : Math.Max(hits, (int)Math.Round(power * ball.Hull) - Math.Max(0, armourHull));
        int sail = ball.Sail <= 0 ? 0 : Math.Max(hits, (int)Math.Round(power * ball.Sail) - Math.Max(0, armourSail));
        double crew = power * Math.Max(0, ball.Sailor);
        int sailors = (int)Math.Floor(crew);
        if (Random.Shared.NextDouble() < crew - sailors) sailors++;
        return new VolleyDamage(hull, sail, sailors);
    }

    public static float ShipSpeed(ShipRules rules, float baseSpeed, int propulsion, float sailFraction, int sailHp, int sailMax,
        int healthySailors, int sailorsBase, int load, int loadMax, float stanceFactor)
    {
        float reference = Math.Max(1, rules.ReferencePropulsion);
        float drive = 1f + (propulsion - reference) / reference * rules.PropulsionWeight;
        float cloth = sailMax <= 0 ? 1f : rules.NoSailSpeedFactor + (1f - rules.NoSailSpeedFactor) * Math.Clamp((float)sailHp / sailMax, 0f, 1f);
        int needed = sailorsBase / 2 + 1;
        float crew = Math.Clamp((float)healthySailors / Math.Max(1, needed), rules.NoCrewSpeedFactor, 1f);
        float cargo = 1f;
        float limit = rules.OverloadPercent / 100f;
        float carried = loadMax <= 0 ? 0f : (float)load / loadMax;
        if (carried > limit && limit < 1f)
            cargo = Math.Clamp(1f - (1f - rules.FullLoadSpeedFactor) * (carried - limit) / (1f - limit), rules.FullLoadSpeedFactor, 1f);
        return MathF.Max(rules.MinimumSpeed, baseSpeed * MathF.Max(0.1f, drive) * sailFraction * cloth * crew * cargo * stanceFactor);
    }

    public static int TurnRate(ShipRules rules, int turning, int hull, int hullMax)
    {
        float damage = hullMax <= 0 ? 1f : Math.Clamp((float)hull / hullMax, 0f, 1f);
        float factor = rules.WreckedTurnFactor + (1f - rules.WreckedTurnFactor) * damage;
        return Math.Clamp((int)(turning * rules.TurnRatePerPoint * factor), 30, 1800);
    }
}
