using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed partial class ClientSession
{
    public void SetHp(int hp)
    {
        Hp = Math.Clamp(hp, 0, MaxHp);
        SetProperty("HP", Hp, visibleToOthers: true);
        SetProperty("RatioHP", 100 * Hp / Math.Max(1, MaxHp), visibleToOthers: true);
    }

    public int ExperienceForNextLevel => 100 * Character.Level;

    public void GainExperience(int amount)
    {
        Character.Experience += amount;
        SystemMessage($"You gain {amount} experience.");
        while (Character.Experience >= ExperienceForNextLevel)
        {
            Character.Experience -= ExperienceForNextLevel;
            Character.Level++;
            ApplyLevel();
            SetProperty("Level", Character.Level, visibleToOthers: true);
            SetProperty("MaxHP", MaxHp, visibleToOthers: true);
            SetProperty("AT", Properties["AT"]);
            SetProperty("DF", Properties["DF"]);
            SetHp(MaxHp);
            SystemMessage($"You have reached level {Character.Level}!", SystemMessageKind.Upgrade);
            Log.Info(Tag, $"{Name} reached level {Character.Level}");
        }
        Save();
    }

    public void Regenerate()
    {
        if (!InWorld || Hp >= MaxHp || Hp <= 0) return;
        SetHp(Hp + Math.Max(1, MaxHp / 20));
    }

    private void ApplyLevel()
    {
        MaxHp = 100 + 20 * (Character.Level - 1);
        Properties["MaxHP"] = MaxHp;
        Properties["AT"] = 15 + 3 * (Character.Level - 1);
        Properties["DF"] = 8 + 2 * (Character.Level - 1);
    }

    public void Attack(MonsterDefinition monster)
    {
        MonsterState state = Context.World.Monster(monster);
        if (state.Dead || Mover == null) return;

        int dealt = Random.Shared.Next(12, 23) + 3 * (Character.Level - 1);
        state.Hp -= dealt;
        bool died = state.Hp <= 0;
        if (died) state.Dead = true;

        PlayAction(Context.Data.Actions.Attack, "(attack swing)", attack: true);
        WearEquipment();
        List<ClientSession> everyone = [this, .. Others()];

        if (died)
        {
            foreach (ClientSession session in everyone)
                session.Send(ScenePackets.RemoveObject(monster.Id), "(monster dies)");
            SetMoney(Money + monster.Gold);
            Save();
            SystemMessage($"You defeated the {monster.Name} and found {monster.Gold} gold.");
            GainExperience(monster.Hp / 2);
            Log.Info(Tag, $"{Name} killed {monster.Name}");
            Context.World.ScheduleRespawn(monster);
            OnMonsterKilled(monster.Id);
            return;
        }

        (float myX, float myZ) = Mover.Now();
        byte[] monsterHit = ChatPackets.FloatingText(monster.Id, $"-{dealt}", 0xffff00, "wound", myX, Height, myZ);
        foreach (ClientSession session in everyone)
        {
            session.Send(ScenePackets.ObjectProperties(monster.Id, new PropertyBag
            {
                ["HP"] = state.Hp,
                ["RatioHP"] = 100 * state.Hp / Math.Max(1, monster.Hp),
            }), "(monster hp)");
            session.Send(monsterHit, "(damage number)");
        }

        int taken = Math.Max(1, Random.Shared.Next(monster.HitMin, monster.HitMax + 1) - (Character.Level - 1));
        foreach (ClientSession session in everyone)
            session.Send(ScenePackets.PlayAction(monster.Id, Context.Data.Actions.Attack, attack: true), "(monster swings)");
        Send(ChatPackets.FloatingText(ProtocolConstants.SelfObjectId, $"-{taken}", 0xff0000, "wound", monster.X, monster.Y, monster.Z), "(damage number)");
        ToOthers(ChatPackets.FloatingText(ObjectId, $"-{taken}", 0xff0000, "wound", monster.X, monster.Y, monster.Z), "(damage number)");
        if (Hp - taken > 0)
        {
            SetHp(Hp - taken);
            return;
        }

        SetHp(MaxHp);
        SystemMessage("You were knocked out and wake up where you started.");
        Log.Info(Tag, $"{Name} was knocked out by {monster.Name}");
        ForceLand = false;
        Travel(WorldSettings.StartScene);
    }
}
