using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class MeleeLoop
{
    private static readonly object Gate = new();
    private static readonly HashSet<ClientSession> Fighters = [];
    private static bool _running;

    public static void Watch(ClientSession session)
    {
        lock (Gate)
        {
            Fighters.Add(session);
            if (_running) return;
            _running = true;
        }
        int step = Math.Clamp(session.Context.Data.Rules.Combat.CombatStepMilliseconds, 20, 500);
        _ = Task.Run(() => Run(step));
    }

    private static async Task Run(int step)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(step));
        while (await timer.WaitForNextTickAsync())
        {
            ClientSession[] fighters;
            lock (Gate)
            {
                if (Fighters.Count == 0)
                {
                    _running = false;
                    return;
                }
                fighters = [.. Fighters];
            }
            foreach (ClientSession session in fighters)
            {
                lock (session.Context.World.Sync)
                {
                    bool going;
                    try
                    {
                        going = session.MeleeStep();
                    }
                    catch (Exception exception)
                    {
                        Log.Error(session.Tag, exception);
                        going = false;
                    }
                    if (!going)
                    {
                        lock (Gate) Fighters.Remove(session);
                    }
                }
            }
        }
    }
}

public sealed partial class ClientSession
{
    private MonsterDefinition? _meleeTarget;
    private MonsterDefinition? _meleePending;
    private long _meleeHitAt;
    private float _meleeGoalX;
    private float _meleeGoalZ;
    private bool _meleeSteered;

    public MonsterDefinition? MeleeTarget => _meleeTarget;

    public float AttackReach()
    {
        CombatRuleSet rules = Rule.Combat;
        return rules.AttackReach.TryGetValue(FightingSkill(), out float reach) && reach > 0f ? reach : rules.DefaultAttackReach;
    }

    public void StopMelee()
    {
        _meleeTarget = null;
        _meleeSteered = false;
    }

    public bool MeleeStep()
    {
        long now = Environment.TickCount64;
        ResolvePendingStrike(now);
        MonsterDefinition? monster = _meleeTarget;
        if (monster == null) return _meleePending != null;
        if (!CanFight(monster) || PlayerTookOver())
        {
            StopMelee();
            return _meleePending != null;
        }
        CombatRuleSet rules = Rule.Combat;
        MonsterState state = Context.World.Monster(monster);
        state.Advance(now);
        (float x, float z) = Mover!.Now();
        float dx = state.X - x;
        float dz = state.Z - z;
        float apart = MathF.Sqrt(dx * dx + dz * dz);
        float reach = AttackReach();
        if (apart > rules.MeleeChaseLimit)
        {
            StopMelee();
            return _meleePending != null;
        }
        if (apart > reach + rules.ReachSlack)
        {
            if (_meleePending != null) return true;
            float stand = reach * Math.Clamp(rules.ApproachPercent, 10, 100) / 100f;
            float aimX = state.X - dx / apart * stand;
            float aimZ = state.Z - dz / apart * stand;
            float drift = MathF.Sqrt((aimX - _meleeGoalX) * (aimX - _meleeGoalX) + (aimZ - _meleeGoalZ) * (aimZ - _meleeGoalZ));
            if (!_meleeSteered || drift > MathF.Max(0.5f, reach * 0.25f))
            {
                MoveTo(aimX, aimZ);
                RememberMeleeGoal();
            }
            return true;
        }
        if (_meleePending != null || now < _nextAttackAt)
        {
            if (Moving()) HaltFacing(state.X, state.Z);
            RememberMeleeGoal();
            return true;
        }
        Swing(monster, state, now);
        RememberMeleeGoal();
        return true;
    }

    private bool CanFight(MonsterDefinition monster)
        => InWorld && !AtSea && Hp > 0 && monster.Scene == Scene && !Context.World.Monster(monster).Dead
            && Context.World.Sessions.Contains(this);

    private void ResolvePendingStrike(long now)
    {
        if (_meleePending is not { } pending || now < _meleeHitAt) return;
        _meleePending = null;
        if (CanFight(pending)) Strike(pending);
    }

    private bool PlayerTookOver()
    {
        if (!_meleeSteered || Mover == null) return false;
        float gx = Mover.DestinationX - _meleeGoalX;
        float gz = Mover.DestinationZ - _meleeGoalZ;
        if (gx * gx + gz * gz < 0.0001f) return false;
        return Moving();
    }

    private bool Moving()
    {
        if (Mover == null) return false;
        (float x, float z) = Mover.Now();
        float dx = Mover.DestinationX - x;
        float dz = Mover.DestinationZ - z;
        return dx * dx + dz * dz > 0.0025f;
    }

    private void RememberMeleeGoal()
    {
        if (Mover == null) return;
        _meleeGoalX = Mover.DestinationX;
        _meleeGoalZ = Mover.DestinationZ;
        _meleeSteered = true;
    }

    private int AttackInterval(string fight)
    {
        CombatRuleSet rules = Rule.Combat;
        int interval = rules.AttackMilliseconds.TryGetValue(fight, out int found) && found > 0 ? found : 1000;
        int speed = WornSum("AddFightRate");
        if (StateActive("IsUSFalchion3")) speed += rules.BuffPercentPerLevel * UniqueSkillLevel("US_FALCHION3");
        if (StateActive("IsUSSword3")) speed -= rules.BuffPercentPerLevel * UniqueSkillLevel("US_SWORD3");
        return Math.Max(rules.MinimumAttackMilliseconds, interval * 100 / Math.Max(25, 100 + speed));
    }

    private void FaceTowards(float x, float z)
    {
        if (Mover == null) return;
        (float hereX, float hereZ) = Mover.Now();
        if ((x - hereX) * (x - hereX) + (z - hereZ) * (z - hereZ) < 0.0001f) return;
        Mover.MoveTo(x, z);
        Mover.MoveTo(hereX, hereZ);
    }

    private void HaltFacing(float x, float z)
    {
        if (Mover == null) return;
        FaceTowards(x, z);
        (float hereX, float hereZ) = Mover.Now();
        Mover.MoveTo(hereX, hereZ);
        int facing = Mover.HeadingNow();
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, hereX, Height, hereZ, orient: facing), "(stop in reach)");
        ToOthers(ScenePackets.PlaceObject(ObjectId, hereX, Height, hereZ, orient: facing), $"({Name} stops in reach)");
    }

    private void PrepareStuntStrike(MonsterDefinition monster)
    {
        MonsterState state = Context.World.Monster(monster);
        HaltFacing(state.X, state.Z);
        CombatTarget = monster.Id;
        _nextAttackAt = Math.Max(_nextAttackAt, Environment.TickCount64 + AttackInterval(FightingSkill()));
        if (_meleeTarget != monster) _meleeSteered = false;
        _meleeTarget = monster;
        MeleeLoop.Watch(this);
    }
}
