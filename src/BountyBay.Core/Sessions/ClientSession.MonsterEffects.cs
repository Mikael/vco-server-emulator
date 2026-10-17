using BountyBay.Core.Data;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const string LevelUpEffect = "uplevel0";
    public const string QuestCompleteEffect = "uplevel0";
    public const string ReviveEffect = "fuhuo";
    public const int MonsterCorpseMilliseconds = 3000;
    public const int MonsterDieMillisecondsCap = 6000;

    public int MonsterAttackAction(MonsterDefinition monster)
        => monster.Attacks.Count > 0 ? monster.Attacks[Random.Shared.Next(monster.Attacks.Count)] : BboMonsterAttackAction();

    public void ShowMonsterDeath(MonsterDefinition monster, MonsterState state, IEnumerable<ClientSession> watchers)
    {
        List<ClientSession> seen = watchers.ToList();
        byte[] dead = ScenePackets.ObjectProperties(monster.Id, new PropertyBag { ["Dead"] = 1, ["HP"] = 0, ["RatioHP"] = 0 });
        foreach (ClientSession session in seen) session.Send(dead, "(monster dead)");
        int die = monster.DieAction >= 0 ? monster.DieAction : Context.Data.Actions.IndexOf("403");
        if (die < 0)
        {
            foreach (ClientSession session in seen) session.Send(ScenePackets.RemoveObject(monster.Id), "(monster dies)");
            return;
        }
        byte[] fall = ScenePackets.PlayAction(monster.Id, die);
        foreach (ClientSession session in seen) session.Send(fall, "(monster dies)");
        int falling = Math.Clamp(monster.DieMilliseconds, 0, MonsterDieMillisecondsCap);
        int death = monster.DeathAction >= 0 ? monster.DeathAction : Context.Data.Actions.IndexOf("101");
        World world = Context.World;
        _ = Task.Run(async () =>
        {
            await Task.Delay(falling);
            lock (world.Sync)
            {
                if (!state.Dead) return;
                if (death >= 0)
                {
                    byte[] lie = ScenePackets.PlayAction(monster.Id, death);
                    foreach (ClientSession session in world.InScene(monster.Scene)) session.Send(lie, "(monster lies dead)");
                }
            }
            await Task.Delay(MonsterCorpseMilliseconds);
            lock (world.Sync)
            {
                if (!state.Dead) return;
                foreach (ClientSession session in world.InScene(monster.Scene))
                    session.Send(ScenePackets.RemoveObject(monster.Id), "(monster corpse fades)");
            }
        });
    }

    public void PlayEffect(string effect, string note)
    {
        float x = 0f;
        float z = 0f;
        if (Mover != null) (x, z) = Mover.Now();
        Send(BboSkillPackets.Effect(ProtocolConstants.SelfObjectId, effect, x, Height, z), note);
        ToOthers(BboSkillPackets.Effect(ObjectId, effect, x, Height, z), note);
    }

    public void PlayLevelUpEffect() => PlayEffect(LevelUpEffect, "(level up light)");

    public void PlayQuestCompleteEffect() => PlayEffect(QuestCompleteEffect, "(quest complete light)");

    public void PlayReviveEffect() => PlayEffect(ReviveEffect, "(revive light)");
}
