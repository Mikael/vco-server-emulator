using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Ship against player ship. CLIENT: the ship command panel's "Force Attack at Sea" (IDName00006825, enabled at sea with a
// target) turns the next click on a ship into an attack; it reaches the server as an interact with the force flag set. A
// ship that was attacked may answer with plain clicks for a while. On the high sea (no broadsides there) the fight moves to
// a battle field (StartOceanDuel); in coastal waters and on battle fields the broadsides fly where you are.
// GUESS: the retaliation window and the reputation for sinking a player.
public sealed partial class ClientSession
{
    private const int SeaFoeSeconds = 120;
    private const int PlayerSinkReputation = 30;

    private readonly Dictionary<ClientSession, long> _seaFoes = [];
    private int _playerEngageSerial;

    private bool IsSeaFoe(ClientSession other)
        => (_seaFoes.TryGetValue(other, out long until) && until > Environment.TickCount64)
           || (_oceanBattle is { Foe: null } room && room.Sides.TryGetValue(other, out int side) && side != room.Sides.GetValueOrDefault(this));

    public bool OnSeaPlayerClicked(ClientSession victim, int modifier)
    {
        if (victim == this || !AtSea || !victim.AtSea || victim.Scene != Scene || victim.Room != Room || Mover == null) return false;
        if (modifier == 0 && !IsSeaFoe(victim)) return false;
        if (Team != null && Team.Members.Contains(victim))
        {
            SeaNotice("You cannot attack a member of your own team.");
            return true;
        }
        if (SeaSunk || victim.SeaSunk) return true;
        if (BboAtOcean && Room == 0)
        {
            StartOceanDuel(victim);
            return true;
        }
        SeaTarget = victim.ObjectId;
        FireAtPlayer(victim);
        EngagePlayer(victim);
        return true;
    }

    private void EngagePlayer(ClientSession victim)
    {
        int serial = ++_playerEngageSerial;
        _engageSerial++;
        int scene = Scene, room = Room;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(500);
                lock (Context.World.Sync)
                {
                    if (serial != _playerEngageSerial || !InWorld || !AtSea || SeaSunk || Scene != scene || Room != room || SeaTarget != victim.ObjectId) return;
                    if (!victim.InWorld || victim.Scene != scene || victim.Room != room || victim.SeaSunk) return;
                    FireAtPlayer(victim, quiet: true);
                }
            }
        });
    }

    private void FireAtPlayer(ClientSession victim, bool quiet = false)
    {
        EnsureShipState();
        if (Mover == null || victim.Mover == null) return;
        ShipRules rules = Sea.Data.Rules;
        (float x, float z) = Mover.Now();
        (float vx, float vz) = victim.Mover.Now();
        float distance = SeaDistance(x, z, vx, vz);
        double relative = RelativeBearing(Mover.HeadingNow(), vx - x, vz - z);
        int side = relative >= 0 ? 1 : 0;
        long now = SeaWorld.Now;
        if (now < _seaReadyAt[side]) return;
        CannonDefinition cannon = ShipCannon;
        int cannons = side == 0 ? Character.CannonsLeft : Character.CannonsRight;
        string ammunitionName = side == 0 ? Character.AmmunitionLeft : Character.AmmunitionRight;
        int stock = Character.Ammunition!.GetValueOrDefault(ammunitionName);
        if (cannons <= 0 || stock <= 0)
        {
            if (!quiet) SeaNotice(cannons <= 0 ? $"You have no cannons on the {(side == 0 ? "port" : "starboard")} side." : "You are out of cannonballs.");
            return;
        }
        if (distance >= cannon.Range)
        {
            if (!quiet) SeaNotice("The target is out of range.");
            return;
        }
        double offBeam = Math.Abs(side == 0 ? relative + 900.0 : relative - 900.0);
        if (offBeam > cannon.FireAngle * 5.0 + FireArcSlackTenths)
        {
            if (!quiet) SeaNotice($"The target is outside the firing arc of your {(side == 0 ? "port" : "starboard")} battery.");
            return;
        }
        AmmunitionDefinition ammunition = Sea.Data.FindAmmunition(ammunitionName) ?? Sea.Data.DefaultAmmunition;
        int balls = Math.Min(cannons, stock);
        int hits = CombatRules.SeaHits(balls, CombatRules.SeaHitPercent(rules, UniqueSkillLevel("US_CANNON"), 0));
        VolleyDamage damage = CombatRules.Volley(hits, cannon.Attack, ammunition, 0, 0, 100);
        int reload = (int)(Math.Max(0.5f, cannon.ReloadSeconds) * 1000);
        _seaReadyAt[side] = now + reload;
        Character.Ammunition![ammunitionName] = stock - balls;
        RefreshAmmunition(ammunitionName);
        float scatter = rules.BroadsideScatter;
        int seed = Random.Shared.Next(1, 30000);
        Send(SeaPackets.Broadside(ProtocolConstants.SelfObjectId, victim.ObjectId, side, ammunition.ShellType, balls, vx, vz, scatter, cannon.BallSpeed, seed), "(broadside)");
        ToOthers(SeaPackets.Broadside(ObjectId, victim.ObjectId, side, ammunition.ShellType, balls, vx, vz, scatter, cannon.BallSpeed, seed), $"({Name} fires at {victim.Name})");
        Send(SeaPackets.CannonReload(side, reload), "(reload)");
        victim._seaFoes[this] = Environment.TickCount64 + SeaFoeSeconds * 1000L;
        _seaFoes[victim] = Environment.TickCount64 + SeaFoeSeconds * 1000L;
        victim.MarkAttacked();
        Sea.Later(Math.Min(4.0, distance / Math.Max(1f, cannon.BallSpeed)), () => victim.TakePlayerVolley(this, ammunition.ShellType, balls, hits, damage, x, z));
    }

    private void TakePlayerVolley(ClientSession attacker, int shellType, int balls, int hits, VolleyDamage damage, float fromX, float fromZ)
    {
        if (!InWorld || !AtSea || SeaSunk || attacker.Scene != Scene || attacker.Room != Room) return;
        string text = hits > 0 ? $"{damage.Hull}" : "Miss";
        float fraction = SeaPackets.HitChance(hits, balls);
        Send(SeaPackets.Volley(ProtocolConstants.SelfObjectId, shellType, balls, fraction, fromX, fromZ, text), "(volley lands)");
        ToOthers(SeaPackets.Volley(ObjectId, shellType, balls, fraction, fromX, fromZ, text), $"(volley lands on {Name})");
        if (hits <= 0) return;
        if (attacker.InWorld) attacker.GainSkillExperience("SKILL_SEABATTLE", (long)(damage.Hull + damage.Sail) * Rule.Skills.SeaBattleExperiencePerDamage);
        if (damage.Sail > 0 && ShipSail > 0) SetShipSail(ShipSail - damage.Sail);
        int struck = Math.Min(damage.Sailors, Character.Sailors);
        if (struck > 0) SetSailors(Character.Sailors - struck, Character.SailorsWounded + struck);
        if (ShipHull - damage.Hull > 0)
        {
            if (damage.Hull > 0) SetShipHull(ShipHull - damage.Hull);
            return;
        }
        LoseShip(attacker.ShipName);
        if (!attacker.InWorld) return;
        attacker.SystemMessage($"You sank {Name}'s ship!", SystemMessageKind.System);
        attacker.GainReputation(PlayerSinkReputation);
        attacker.Character.ShipsSunk++;
        attacker.Save();
        Log.Info(attacker.Tag, $"{attacker.Name} sinks {Name}'s ship in scene {Scene}");
    }
}
