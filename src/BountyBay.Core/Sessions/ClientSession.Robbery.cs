using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class RobberyDeal
{
    public ClientSession? Robber { get; init; }
    public ClientSession? Victim { get; init; }
    public SeaNpcState? Pirate { get; init; }
    public SeaNpcState? Prey { get; init; }
    public int Money { get; init; }
    public bool Settled { get; set; }
}

public sealed partial class ClientSession
{
    private const int RobberySeconds = 15;
    private const double RobberyNpcAnswerSeconds = 3.0;
    private const float RobberyReach = 30f;
    private const int RobberyMinimum = 100;

    private RobberyDeal? _robbery;

    public void SendRobberyLevels()
    {
        if (PropertyTable.Has("RobLevel")) SetProperty("RobLevel", Character.RobberyLevel);
        if (PropertyTable.Has("ContrabandLv")) SetProperty("ContrabandLv", Character.ContrabandLevel);
    }

    public void OceanRob(int targetId)
    {
        if (!AtSea || Mover == null || SeaSunk)
        {
            SystemMessage("This Function can only be used in High Seas", SystemMessageKind.Prompt);
            return;
        }
        if (_robbery is { Settled: false })
        {
            SystemMessage("You are already extorting somebody.", SystemMessageKind.Prompt);
            return;
        }
        if (targetId == 0) targetId = SeaTarget;
        ClientSession? player = Context.World.Sessions.FirstOrDefault(other => other != this && other.ObjectId == targetId
            && other.InWorld && other.AtSea && other.Scene == Scene);
        SeaNpcState? ship = player == null ? Sea.Find(targetId, Scene) : null;
        if (player == null && (ship == null || ship.Dead || ship.Gone))
        {
            SystemMessage("Choose a ship to extort first.", SystemMessageKind.Prompt);
            return;
        }
        (float x, float z) = Mover.Now();
        (float tx, float tz) = player != null ? player.Mover!.Now() : (ship!.X, ship.Z);
        if ((tx - x) * (tx - x) + (tz - z) * (tz - z) > RobberyReach * RobberyReach)
            Send(BboSeaPackets.GoTo(ProtocolConstants.SelfObjectId, tx, tz), "(sail to the prey)");
        if (player != null) RobPlayer(player);
        else RobShip(ship!);
    }

    public void AnswerRobbery(int answer)
    {
        RobberyDeal? deal = _robbery;
        Send(RobberyPackets.Close(), "(robbery box closed)");
        if (deal == null || deal.Settled || deal.Victim != this) return;
        if (answer == RobberyPackets.Bribe && Money >= deal.Money) PayRobbery(deal);
        else RefuseRobbery(deal);
    }

    public bool PirateDemand(SeaNpcState pirate)
    {
        if (!AtSea || SeaSunk || _robbery is { Settled: false } || Properties.Int("SeaProtect") > 0 || Money < RobberyMinimum) return false;
        int money = Math.Clamp(pirate.Definition.Level * 100, RobberyMinimum, Money);
        var deal = new RobberyDeal { Victim = this, Pirate = pirate, Money = money };
        _robbery = deal;
        Send(RobberyPackets.VictimTimer(RobberySeconds, money), $"({pirate.Definition.Name} demands silver)");
        Sea.Later(RobberySeconds, () => { if (!deal.Settled) RefuseRobbery(deal); });
        return true;
    }

    private void RobPlayer(ClientSession victim)
    {
        if (victim._robbery is { Settled: false } || victim.Money <= 0)
        {
            SystemMessage($"{victim.Name} has nothing worth extorting right now.", SystemMessageKind.Prompt);
            return;
        }
        int money = Math.Min(victim.Money, Math.Max(RobberyMinimum, victim.Money * Math.Clamp(10 * Character.RobberyLevel, 10, 50) / 100));
        var deal = new RobberyDeal { Robber = this, Victim = victim, Money = money };
        _robbery = deal;
        victim._robbery = deal;
        Send(RobberyPackets.RobberTimer(RobberySeconds), "(waiting for the answer)");
        victim.Send(RobberyPackets.VictimTimer(RobberySeconds, money), $"({Name} demands silver)");
        Sea.Later(RobberySeconds, () => { if (!deal.Settled) RefuseRobbery(deal); });
        Log.Info(Tag, $"{Name} extorts {money} from {victim.Name}");
    }

    private void RobShip(SeaNpcState ship)
    {
        var deal = new RobberyDeal { Robber = this, Prey = ship };
        _robbery = deal;
        Send(RobberyPackets.RobberTimer(RobberySeconds), "(waiting for the answer)");
        Sea.Later(RobberyNpcAnswerSeconds, () =>
        {
            if (deal.Settled) return;
            deal.Settled = true;
            Send(RobberyPackets.Close(), "(robbery over)");
            if (!InWorld || ship.Dead || ship.Gone) return;
            SeaNpcDefinition definition = ship.Definition;
            int chance = Math.Clamp(40 + 15 * Character.RobberyLevel - 5 * definition.Level, 10, 90);
            if (Random.Shared.Next(100) < chance)
            {
                int money = Math.Max(1, definition.Gold * Math.Clamp(50 + 10 * Character.RobberyLevel, 50, 100) / 100);
                SetMoney(Money + money);
                Character.RobberyCount++;
                Save();
                SystemMessage($"The {definition.Name} hands over {money} silver coins.");
                Log.Info(Tag, $"{Name} extorts {money} from {definition.Name}");
                return;
            }
            Sea.Provoke(ship, this);
            SystemMessage($"The {definition.Name} refuses and opens fire!", SystemMessageKind.Prompt);
        });
    }

    private static void PayRobbery(RobberyDeal deal)
    {
        deal.Settled = true;
        ClientSession victim = deal.Victim!;
        victim.SetMoney(victim.Money - deal.Money);
        victim.Save();
        if (deal.Robber is { InWorld: true } robber)
        {
            robber.SetMoney(robber.Money + deal.Money);
            robber.Character.RobberyCount++;
            robber.Save();
            robber.Send(RobberyPackets.Close(), "(robbery over)");
            robber.SystemMessage($"{victim.Name} pays you {deal.Money} silver coins.");
        }
        if (deal.Pirate is { } pirate)
        {
            pirate.Target = null;
            victim.SystemMessage($"The {pirate.Definition.Name} takes your {deal.Money} silver coins and sails away.");
        }
        else victim.SystemMessage($"You paid {deal.Money} silver coins.");
        Log.Info(victim.Tag, $"{victim.Name} pays {deal.Money} to avoid a fight");
    }

    private static void RefuseRobbery(RobberyDeal deal)
    {
        deal.Settled = true;
        ClientSession? victim = deal.Victim;
        victim?.Send(RobberyPackets.Close(), "(robbery over)");
        if (deal.Pirate is { } pirate && victim is { InWorld: true })
        {
            SeaWorld.Of(victim.Context).Provoke(pirate, victim);
            victim.SystemMessage($"The {pirate.Definition.Name} attacks!", SystemMessageKind.Prompt);
        }
        if (deal.Robber is { InWorld: true } robber)
        {
            robber.Send(RobberyPackets.Close(), "(robbery over)");
            robber.SystemMessage($"{victim?.Name ?? "Your prey"} refuses to pay and prepares to fight!", SystemMessageKind.Prompt);
            victim?.SystemMessage($"You refuse to pay {robber.Name}. Prepare to fight!", SystemMessageKind.Prompt);
        }
    }
}
