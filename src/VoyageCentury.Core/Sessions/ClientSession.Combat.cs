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
    }

    public void Attack(MonsterDefinition monster)
    {
        MonsterState state = Context.World.Monster(monster);
        if (state.Dead || Mover == null) return;

        state.Hp -= Random.Shared.Next(12, 23);
        bool died = state.Hp <= 0;
        if (died) state.Dead = true;

        PlayAction(Context.Data.Actions.Attack, "(attack swing)");
        List<ClientSession> everyone = [this, .. Others()];

        if (died)
        {
            foreach (ClientSession session in everyone)
                session.Send(ScenePackets.RemoveObject(monster.Id), "(monster dies)");
            SetMoney(Money + monster.Gold);
            Save();
            SystemMessage($"You defeated the {monster.Name} and found {monster.Gold} gold.");
            Log.Info(Tag, $"{Name} killed {monster.Name}");
            Context.World.ScheduleRespawn(monster);
            return;
        }

        foreach (ClientSession session in everyone)
            session.Send(ScenePackets.ObjectProperties(monster.Id, new PropertyBag { ["HP"] = state.Hp }), "(monster hp)");

        int taken = Random.Shared.Next(monster.HitMin, monster.HitMax + 1);
        Send(ChatPackets.Chat(monster.Id, $"{monster.Name} hits you for {taken}. ({state.Hp} left on it)"), "(fight)");
        if (Hp - taken > 0)
        {
            SetHp(Hp - taken);
            return;
        }

        SetHp(MaxHp);
        SystemMessage("You were knocked out and wake up where you started.");
        Log.Info(Tag, $"{Name} was knocked out by {monster.Name}");
        Mover = new Mover(StartPosition.X, StartPosition.Z, WorldSettings.RunSpeed);
        Height = StartPosition.Y;
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, StartPosition.X, StartPosition.Y, StartPosition.Z), "(back to start)");
        ToOthers(ScenePackets.PlaceObject(ObjectId, StartPosition.X, StartPosition.Y, StartPosition.Z), $"({Name} back to start)");
    }
}
