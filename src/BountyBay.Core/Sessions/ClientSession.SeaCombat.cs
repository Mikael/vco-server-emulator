using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int SeaNoticeGap = 2500;

    private readonly long[] _seaReadyAt = new long[2];
    private readonly HashSet<int> _ammunitionShown = [];
    private readonly HashSet<int> _cannonSlotsShown = [];
    private bool _shipViewsSent;
    private long _seaNoticeAt;

    public bool SeaSunk { get; private set; }

    private SeaWorld Sea => SeaWorld.Of(Context);

    public int ShipHullMax => FleetHullMax;

    public int ShipHull => Character.ShipHull < 0 ? ShipHullMax : Math.Min(Character.ShipHull, ShipHullMax);

    public int ShipSail => Character.ShipSail < 0 ? FleetSailMax : Math.Min(Character.ShipSail, FleetSailMax);

    public AmmunitionDefinition ShipAmmunition(int side)
        => Sea.Data.FindAmmunition(side == 0 ? Character.AmmunitionLeft : Character.AmmunitionRight) ?? Sea.Data.DefaultAmmunition;

    public string ShipName => Character.ShipName.Length > 0 ? Character.ShipName : $"{Name}'s ship";

    public CannonDefinition ShipCannon => Sea.Data.FindCannon(Character.CannonName) ?? Sea.Data.DefaultCannon;

    public void EnsureShipState()
    {
        SeaData data = Sea.Data;
        ShipRules rules = data.Rules;
        if (Character.CannonsLeft < 0) Character.CannonsLeft = Math.Min(rules.StartCannonsPerSide, rules.CannonsPerSide);
        if (Character.CannonsRight < 0) Character.CannonsRight = Math.Min(rules.StartCannonsPerSide, rules.CannonsPerSide);
        if (!Character.CannonsRestored)
        {
            Character.CannonsRestored = true;
            if (Character.CannonsLeft == 0 && Character.CannonsRight == 0)
                Character.CannonsLeft = Character.CannonsRight = Math.Min(rules.StartCannonsPerSide, rules.CannonsPerSide);
        }
        if (Character.CannonName.Length == 0) Character.CannonName = data.DefaultCannon.Name;
        Character.Ammunition ??= new Dictionary<string, int> { [data.DefaultAmmunition.Name] = rules.StartAmmunition };
        if (!HasAmmunition(Character.AmmunitionLeft)) Character.AmmunitionLeft = FirstAmmunition();
        if (!HasAmmunition(Character.AmmunitionRight)) Character.AmmunitionRight = FirstAmmunition();
        if (Character.ShipHull < 0) Character.ShipHull = FleetHullMax;
        if (Character.ShipSail < 0) Character.ShipSail = FleetSailMax;
        Character.SailorsWounded = Math.Clamp(Character.SailorsWounded, 0, FleetSailorsMax);
        Character.Sailors = Math.Clamp(Character.Sailors, 0, FleetSailorsMax - Character.SailorsWounded);
    }

    public void ApplySeaShip(PropertyBag ship)
    {
        EnsureShipState();
        CannonDefinition cannon = ShipCannon;
        var values = new PropertyBag
        {
            ["S/Name"] = ShipName,
            ["S/SailTex"] = Character.SailCloth,
            ["S/ModelCannonL"] = cannon.Model,
            ["S/ModelCannonR"] = cannon.Model,
            ["S/NumCannonL"] = Character.CannonsLeft,
            ["S/NumCannonR"] = Character.CannonsRight,
            ["RatioShipHP"] = 100 * ShipHull / Math.Max(1, ShipHullMax),
            ["RatioSailHP"] = 100 * ShipSail / Math.Max(1, FleetSailMax),
            ["S/SailHP"] = ShipSail,
            ["S/MaxSailHP"] = FleetSailMax,
            ["RatioSailorHP"] = SailorRatio,
            ["NumSailor"] = Character.Sailors,
            ["NumSailorBad"] = Character.SailorsWounded,
            ["AmmoIndexL"] = AmmunitionIndex(Character.AmmunitionLeft),
            ["AmmoIndexR"] = AmmunitionIndex(Character.AmmunitionRight),
        };
        foreach ((string name, object value) in BboFiringArc()) values[name] = value;
        foreach ((string name, object value) in SeaPackets.Known(values)) ship[name] = value;
    }

    public void SendSeaObjects()
    {
        EnsureShipState();
        SendShipViews();
        SeaSunk = false;
        if (!AtSea) return;

        Sea.EnsureTicking();
        foreach (SeaNpcState ship in Sea.InScene(Scene))
        {
            if (!ship.Gone) SendSeaNpc(ship);
        }
        Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, SeaPackets.Known(new PropertyBag
        {
            ["RatioShipHP"] = 100 * ShipHull / Math.Max(1, ShipHullMax),
        })), "(hull)");
        SendAmmunitionChoice();
    }

    public void LeaveSeaCombat()
    {
        Sea.Forget(this);
        SeaSunk = false;
        _seaReadyAt[0] = _seaReadyAt[1] = 0;
    }

    public void SendSeaNpc(SeaNpcState ship)
    {
        SeaNpcDefinition definition = ship.Definition;
        string note = $"(sea npc {definition.Name})";
        Send(ScenePackets.CreateObject(definition.Id, BboOceanScene(definition.Scene) ? ObjectKind.OceanNpc : ObjectKind.SeaNpc, definition.Model), note);
        PropertyBag shipProperties = ship.Bars();
        foreach ((string name, object value) in new PropertyBag
        {
            ["Name"] = definition.Name,
            ["S/Name"] = definition.Name,
            ["Model"] = definition.Model,
            ["ShipModel"] = definition.Model,
            ["Level"] = definition.Level,
            ["Photo"] = Yard.Find(definition.Model)?.Photo ?? new ShipDefinition().Photo,
            ["Dead"] = ship.Dead ? 1 : 0,
            ["S/ModelCannonL"] = Sea.CannonOf(definition).Model,
            ["S/ModelCannonR"] = Sea.CannonOf(definition).Model,
            ["S/NumCannonL"] = definition.Cannons,
            ["S/NumCannonR"] = definition.Cannons,
            ["S/SailTex"] = definition.SailCloth,
            ["S/Flag"] = 0,
            ["S/RandSeed"] = 1,
            ["S/Level"] = 0,
            ["S/MastFront"] = 2211101,
            ["S/MastMiddle"] = 2211401,
            ["S/MastBack"] = 2211101,
            ["S/ShipDamage"] = 0,
            ["S/FishNet"] = 0,
            ["SailState"] = 0,
            ["MarineStrategy"] = 0,
            ["Bumping"] = 0,
        })
            shipProperties[name] = value;
        Send(ScenePackets.ObjectProperties(definition.Id, SeaPackets.Known(shipProperties)), note);
        BboSendNpcFiringArc(ship);
        Send(ScenePackets.PlaceObject(definition.Id, ship.X, 0f, ship.Z, ship.Moving ? ship.DestX : null, ship.Moving ? ship.DestZ : null, ship.Speed, orient: ship.Heading), note);
        Send(ScenePackets.ShowObject(definition.Id), note);
    }

    public bool OnSeaObjectClicked(int objectId)
    {
        if (!AtSea || Mover == null) return false;
        SeaNpcState? ship = Sea.Find(objectId, Scene);
        if (ship == null) return false;
        SeaTarget = objectId;
        if (ship.Definition.Friendly)
        {
            SeaNotice($"The {ship.Definition.Name} is a friendly ship.");
            return true;
        }
        if (!ship.Dead && !ship.Gone && !SeaSunk)
        {
            FireBroadside(ship);
            EngageSeaTarget(objectId);
        }
        return true;
    }

    private int _engageSerial;

    private void EngageSeaTarget(int objectId)
    {
        int serial = ++_engageSerial;
        int scene = Scene;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(500);
                lock (Context.World.Sync)
                {
                    if (serial != _engageSerial || !InWorld || !AtSea || SeaSunk || Scene != scene || SeaTarget != objectId || Mover == null) return;
                    SeaNpcState? ship = Sea.Find(objectId, Scene);
                    if (ship == null || ship.Dead || ship.Gone || ship.Definition.Friendly) return;
                    try
                    {
                        FireBroadside(ship, quiet: true);
                    }
                    catch (Exception exception)
                    {
                        Log.Error(Tag, exception);
                        return;
                    }
                }
            }
        });
    }

    public bool SelectAmmunition(int index)
    {
        EnsureShipState();
        List<AmmunitionDefinition> kinds = Sea.Data.Ammunition;
        if (index < 1 || index > kinds.Count || !HasAmmunition(kinds[index - 1].Name)) return false;
        Character.AmmunitionLeft = Character.AmmunitionRight = kinds[index - 1].Name;
        SendAmmunitionChoice();
        Save();
        return true;
    }

    public void AddAmmunition(string name, int amount)
    {
        EnsureShipState();
        Character.Ammunition![name] = Math.Max(0, Character.Ammunition.GetValueOrDefault(name) + amount);
        SendShipViews();
        RefreshAmmunition(name);
        if (!HasAmmunition(Character.AmmunitionLeft)) Character.AmmunitionLeft = FirstAmmunition();
        if (!HasAmmunition(Character.AmmunitionRight)) Character.AmmunitionRight = FirstAmmunition();
        SendAmmunitionChoice();
    }

    public void SetShipCannons(int left, int right)
    {
        EnsureShipState();
        int most = FleetCannonsPerSide;
        Character.CannonsLeft = Math.Clamp(left, 0, most);
        Character.CannonsRight = Math.Clamp(right, 0, most);
        SendShipViews();
        RefreshCannons(SeaPackets.LeftCannonSlot);
        RefreshCannons(SeaPackets.RightCannonSlot);
        BboSendFiringArc();
    }

    public void SetShipHull(int hull)
    {
        Character.ShipHull = Math.Clamp(hull, 0, ShipHullMax);
        var ratio = SeaPackets.Known(new PropertyBag { ["RatioShipHP"] = 100 * ShipHull / Math.Max(1, ShipHullMax) });
        if (AtSea)
        {
            Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, ratio), "(hull)");
            ToOthers(ScenePackets.ObjectProperties(ObjectId, ratio), $"({Name} hull)");
        }
        Send(ViewPackets.UpdateItem(ViewId.Ship, 1, SeaPackets.Known(new PropertyBag
        {
            ["S/HP"] = ShipHull,
            ["S/MaxHP"] = ShipHullMax,
        })), "(ship hull)");
    }

    public int SailorRatio => 100 * Character.Sailors / Math.Max(1, Character.Sailors + Character.SailorsWounded);

    public void SetShipSail(int sail)
    {
        Character.ShipSail = Math.Clamp(sail, 0, FleetSailMax);
        var ratio = SeaPackets.Known(new PropertyBag
        {
            ["RatioSailHP"] = 100 * ShipSail / Math.Max(1, FleetSailMax),
            ["S/SailHP"] = ShipSail,
            ["S/MaxSailHP"] = FleetSailMax,
        });
        if (AtSea)
        {
            Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, ratio), "(sail)");
            ToOthers(ScenePackets.ObjectProperties(ObjectId, ratio), $"({Name} sail)");
            ReissueShipMove("sail damage");
        }
        Send(ViewPackets.UpdateItem(ViewId.Ship, 1, SeaPackets.Known(new PropertyBag
        {
            ["S/SailHP"] = ShipSail,
            ["S/MaxSailHP"] = FleetSailMax,
            ["RatioSailHP"] = 100 * ShipSail / Math.Max(1, FleetSailMax),
        })), "(ship sail)");
    }

    public void SetSailors(int healthy, int wounded)
    {
        wounded = Math.Clamp(wounded, 0, FleetSailorsMax);
        healthy = Math.Clamp(healthy, 0, FleetSailorsMax - wounded);
        Character.Sailors = healthy;
        Character.SailorsWounded = wounded;
        Send(ViewPackets.UpdateItem(ViewId.Ship, 1, SeaPackets.Known(new PropertyBag
        {
            ["NumSailor"] = healthy,
            ["NumSailorBad"] = wounded,
        })), "(sailors)");
        SetProperty("NumSailor", healthy);
        SetProperty("NumSailorBad", wounded);
        if (!AtSea) return;
        var ratio = SeaPackets.Known(new PropertyBag { ["RatioSailorHP"] = SailorRatio });
        if (ratio.Count == 0) return;
        Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, ratio), "(sailor ratio)");
        ToOthers(ScenePackets.ObjectProperties(ObjectId, ratio), $"({Name} sailor ratio)");
        ReissueShipMove("crew change");
    }

    public void TakeSeaVolley(SeaNpcState ship)
    {
        if (Mover == null || !AtSea || SeaSunk || ship.Dead) return;
        SeaNpcDefinition definition = ship.Definition;
        CannonDefinition cannon = Sea.CannonOf(definition);
        AmmunitionDefinition ammunition = Sea.Data.FindAmmunition(definition.Ammunition) ?? Sea.Data.DefaultAmmunition;
        ShipRules rules = Sea.Data.Rules;
        (float x, float z) = Mover.Now();
        float distance = SeaDistance(ship.X, ship.Z, x, z);
        if (distance > cannon.Range * 1.2f) return;

        MarkAttacked();
        int side = BroadsideSide(ship.Heading, x - ship.X, z - ship.Z);
        int balls = Math.Max(1, definition.Cannons);
        int hits = CombatRules.SeaHits(balls, definition.HitPercent);
        VolleyDamage damage = CombatRules.Volley(hits, cannon.Attack, ammunition, 0, 0, Math.Max(1, rules.NpcDamagePercent));
        int hullPercent = SeaStance switch
        {
            SeaStanceGuard => rules.GuardHullTakenPercent,
            SeaStancePursuit => rules.PursuitHullTakenPercent,
            _ => 100,
        };
        int hull = damage.Hull <= 0 ? 0 : Math.Max(1, damage.Hull * hullPercent / 100);
        if (rules.NpcVolleyMaxHullPercent > 0) hull = Math.Min(hull, Math.Max(1, ShipHullMax * rules.NpcVolleyMaxHullPercent / 100));

        float scatter = rules.BroadsideScatter;
        int seed = Random.Shared.Next(1, 30000);
        Send(SeaPackets.Broadside(definition.Id, ProtocolConstants.SelfObjectId, side, ammunition.ShellType, balls, x, z, scatter, cannon.BallSpeed, seed),
            $"({definition.Name} fires)");
        ToOthers(SeaPackets.Broadside(definition.Id, ObjectId, side, ammunition.ShellType, balls, x, z, scatter, cannon.BallSpeed, seed),
            $"({definition.Name} fires at {Name})");

        Sea.Later(Math.Min(4.0, distance / Math.Max(1f, cannon.BallSpeed)), () =>
        {
            if (!InWorld || !AtSea || SeaSunk || Scene != definition.Scene) return;
            string text = hits > 0 ? $"{hull}" : "Miss";
            float fraction = SeaPackets.HitChance(hits, balls);
            Send(SeaPackets.Volley(ProtocolConstants.SelfObjectId, ammunition.ShellType, balls, fraction, ship.X, ship.Z, text), "(volley lands)");
            ToOthers(SeaPackets.Volley(ObjectId, ammunition.ShellType, balls, fraction, ship.X, ship.Z, text), $"(volley lands on {Name})");
            if (hits <= 0) return;
            if (damage.Sail > 0 && ShipSail > 0) SetShipSail(ShipSail - damage.Sail);
            int struck = Math.Min(damage.Sailors, Character.Sailors);
            if (struck > 0) SetSailors(Character.Sailors - struck, Character.SailorsWounded + struck);
            if (ShipHull - hull > 0)
            {
                if (hull > 0) SetShipHull(ShipHull - hull);
                return;
            }
            LoseShip(definition.Name);
        });
    }

    private void FireBroadside(SeaNpcState ship, bool quiet = false)
    {
        EnsureShipState();
        SeaNpcDefinition definition = ship.Definition;
        ShipRules rules = Sea.Data.Rules;
        (float x, float z) = Mover!.Now();
        float distance = SeaDistance(x, z, ship.X, ship.Z);
        double relative = RelativeBearing(Mover.HeadingNow(), ship.X - x, ship.Z - z);
        int side = relative >= 0 ? 1 : 0;
        long now = SeaWorld.Now;
        if (now < _seaReadyAt[side]) return;
        if (quiet && SeaStance == SeaStanceFullSpeed) return;
        if (FiringEndsFullSpeed()) return;

        CannonDefinition cannon = ShipCannon;
        int cannons = side == 0 ? Character.CannonsLeft : Character.CannonsRight;
        string ammunitionName = side == 0 ? Character.AmmunitionLeft : Character.AmmunitionRight;
        int stock = Character.Ammunition!.GetValueOrDefault(ammunitionName);
        if (cannons <= 0)
        {
            if (!quiet) SeaNotice($"You have no cannons on the {(side == 0 ? "port" : "starboard")} side.");
            return;
        }
        if (stock <= 0)
        {
            if (!quiet) SeaNotice("You are out of cannonballs.");
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
        int chance = CombatRules.SeaHitPercent(rules, UniqueSkillLevel("US_CANNON"), SeaStance == SeaStancePursuit ? rules.PursuitHitPenalty : 0);
        int hits = CombatRules.SeaHits(balls, chance);
        VolleyDamage damage = CombatRules.Volley(hits, cannon.Attack, ammunition, definition.ArmourHull, definition.ArmourSail,
            SeaStance == SeaStanceGuard ? rules.GuardDamagePercent : 100);

        int reload = (int)(Math.Max(0.5f, cannon.ReloadSeconds) * 1000);
        _seaReadyAt[side] = now + reload;
        Character.Ammunition![ammunitionName] = stock - balls;
        RefreshAmmunition(ammunitionName);
        if (stock - balls <= 0)
        {
            if (side == 0) Character.AmmunitionLeft = FirstAmmunition();
            else Character.AmmunitionRight = FirstAmmunition();
            SendAmmunitionChoice();
        }

        float scatter = rules.BroadsideScatter;
        int seed = Random.Shared.Next(1, 30000);
        Send(SeaPackets.Broadside(ProtocolConstants.SelfObjectId, definition.Id, side, ammunition.ShellType, balls,
            ship.X, ship.Z, scatter, cannon.BallSpeed, seed), "(broadside)");
        ToOthers(SeaPackets.Broadside(ObjectId, definition.Id, side, ammunition.ShellType, balls,
            ship.X, ship.Z, scatter, cannon.BallSpeed, seed), $"({Name} fires a broadside)");
        Send(SeaPackets.CannonReload(side, reload), "(reload)");
        Sea.Provoke(ship, this);
        Log.Debug(Tag, $"{Name} fires {balls} x {ammunitionName} at {definition.Name}: {hits} hit(s), hull {damage.Hull}, sail {damage.Sail}, sailors {damage.Sailors}");

        Sea.Later(Math.Min(4.0, distance / Math.Max(1f, cannon.BallSpeed)), () => LandBroadside(ship, ammunition.ShellType, balls, hits, damage, x, z));
    }

    private void LandBroadside(SeaNpcState ship, int shellType, int balls, int hits, VolleyDamage damage, float fromX, float fromZ)
    {
        if (ship.Dead) return;
        SeaNpcDefinition definition = ship.Definition;
        byte[] volley = SeaPackets.Volley(definition.Id, shellType, balls, SeaPackets.HitChance(hits, balls), fromX, fromZ,
            hits > 0 ? $"{damage.Hull}" : "Miss", 0xffff00);
        List<ClientSession> watchers = Sea.Sailors(definition.Scene).ToList();
        foreach (ClientSession session in watchers) session.Send(volley, "(broadside lands)");
        if (hits <= 0) return;

        int counted = Math.Min(damage.Hull, Math.Max(0, ship.Hull)) + Math.Min(damage.Sail, Math.Max(0, ship.Sail));
        if (InWorld) GainSkillExperience("SKILL_SEABATTLE", (long)counted * Rule.Skills.SeaBattleExperiencePerDamage);
        ship.Hull -= damage.Hull;
        ship.Sail = Math.Max(0, ship.Sail - damage.Sail);
        ship.Sailors = Math.Max(0, ship.Sailors - damage.Sailors);
        if (ship.Hull > 0 && ship.Sailors > 0)
        {
            byte[] state = ScenePackets.ObjectProperties(definition.Id, SeaPackets.Known(ship.Bars()));
            foreach (ClientSession session in watchers) session.Send(state, $"({definition.Name} damage)");
            return;
        }

        Sea.Sink(ship);
        if (!InWorld) return;
        Character.ShipsSunk++;
        SetMoney(Money + definition.Gold);
        SystemMessage($"You sank the {definition.Name} and salvaged {definition.Gold} silver coins.");
        GainReputation(definition.Reputation > 0 ? definition.Reputation : Rule.Economy.PirateReputationPerLevel * definition.Level);
        OnSeaNpcSunk(definition.Name);
        SeaInstanceShipSunk(definition);
        Save();
        Log.Info(Tag, $"{Name} sank {definition.Name}");
    }

    private void WarnShipCondition()
    {
        if (ShipHull * 4 < ShipHullMax)
            SystemMessage($"Your ship's durability is only {ShipHull}/{ShipHullMax}. Have her repaired at a shipyard before you fight.", SystemMessageKind.System);
        if (Character.Sailors <= 0)
            SystemMessage("You have no healthy sailors aboard. Hire sailors at a shipyard.", SystemMessageKind.System);
    }

    private void LoseShip(string sunkBy)
    {
        ShipRules rules = Sea.Data.Rules;
        SeaSunk = true;
        Sea.Forget(this);
        Character.ShipHull = Math.Clamp(rules.SunkDurability, 0, ShipHullMax);
        if (rules.LoseCannonsOnSinking)
        {
            Character.CannonsLeft = 0;
            Character.CannonsRight = 0;
        }
        Save();
        var sinking = SeaPackets.Known(new PropertyBag { ["Dead"] = 1, ["RatioShipHP"] = 0 });
        Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, sinking), "(own ship sinks)");
        ToOthers(ScenePackets.ObjectProperties(ObjectId, sinking), $"({Name} sinks)");
        SystemMessage($"Your ship was sunk by the {sunkBy}.", SystemMessageKind.System);
        Log.Info(Tag, $"{Name} was sunk by {sunkBy}");

        int sunkIn = Scene;
        Sea.Later(4, () =>
        {
            if (!InWorld || !SeaSunk || Scene != sunkIn) return;
            SeaSunk = false;
            int port = Character.LastPort != 0 ? Character.LastPort : rules.PortScene;
            if (!Context.Data.Scenes.Exists(port)) port = WorldSettings.StartScene;
            ForceLand = false;
            Travel(port);
            SetShipHull(Character.ShipHull);
            SetShipCannons(Character.CannonsLeft, Character.CannonsRight);
            LoseReputation(rules.SinkReputationLossPercent);
            SystemMessage(rules.LoseCannonsOnSinking
                ? "You were brought back to the last port you visited. Your ship was saved but her durability dropped to 1 and her mounted cannons were lost. Have her repaired before you sail off."
                : "You were brought back to the last port you visited. Your ship was saved but her durability dropped to 1. Have her repaired before you sail off.");
            Save();
        });
    }

    private void SendShipViews()
    {
        if (_shipViewsSent) return;
        _shipViewsSent = true;
        Send(ViewPackets.Create(SeaPackets.AmmunitionView, Math.Max(8, Sea.Data.Ammunition.Count)), "(ammunition view)");
        Send(ViewPackets.Create(SeaPackets.ShipEquipmentView, 12), "(ship equipment view)");
        foreach (string name in Character.Ammunition!.Keys.ToList()) RefreshAmmunition(name);
        RefreshCannons(SeaPackets.LeftCannonSlot);
        RefreshCannons(SeaPackets.RightCannonSlot);
        SendShipDetails();
    }

    private void SendShipDetails() => SendFleetShip();

    private void SendAmmunitionChoice()
    {
        foreach ((string property, string ammunition) in new[] { ("AmmoIndexL", Character.AmmunitionLeft), ("AmmoIndexR", Character.AmmunitionRight) })
        {
            if (PropertyTable.Has(property)) SetProperty(property, AmmunitionIndex(ammunition));
        }
    }

    private void RefreshAmmunition(string name)
    {
        int index = AmmunitionIndex(name);
        if (index == 0) return;
        int amount = Character.Ammunition!.GetValueOrDefault(name);
        if (amount <= 0)
        {
            if (_ammunitionShown.Remove(index)) Send(ViewPackets.RemoveItem(SeaPackets.AmmunitionView, index), "(ammunition used up)");
            return;
        }
        if (_ammunitionShown.Add(index))
        {
            var item = new PropertyBag
            {
                ["Name"] = name,
                ["Index"] = index,
                ["Amount"] = amount,
                ["Photo"] = Sea.Data.FindAmmunition(name)?.Photo ?? Sea.Data.DefaultAmmunition.Photo,
                ["Kind"] = 0,
                ["ItemType"] = SeaPackets.ItemTypeAmmunition,
            };
            Send(ViewPackets.AddItem(SeaPackets.AmmunitionView, index, SeaPackets.Known(ItemCatalog.Extend(item, name))), "(ammunition)");
        }
        else
        {
            Send(ViewPackets.UpdateItem(SeaPackets.AmmunitionView, index, new PropertyBag { ["Amount"] = amount }), "(ammunition)");
        }
    }

    private void RefreshCannons(int slot)
    {
        int count = slot == SeaPackets.LeftCannonSlot ? Character.CannonsLeft : Character.CannonsRight;
        if (count <= 0)
        {
            if (_cannonSlotsShown.Remove(slot)) Send(ViewPackets.RemoveItem(SeaPackets.ShipEquipmentView, slot), "(cannons removed)");
            return;
        }
        if (!_cannonSlotsShown.Add(slot))
        {
            Send(ViewPackets.UpdateItem(SeaPackets.ShipEquipmentView, slot, new PropertyBag { ["Amount"] = count }), "(cannons)");
            return;
        }
        CannonDefinition cannon = ShipCannon;
        var item = new PropertyBag
        {
            ["Name"] = cannon.Name,
            ["Index"] = slot,
            ["Amount"] = count,
            ["Photo"] = cannon.Photo,
            ["Kind"] = 0,
            ["ItemType"] = SeaPackets.ItemTypeShipEquipment,
            ["AT"] = cannon.Attack,
            ["DFG"] = 100,
            ["DFGMax"] = 100,
            ["CoolingKind"] = 0,
            ["ShipArmType"] = SeaPackets.CannonArmType,
        };
        ItemCatalog.Extend(item, cannon.Name);
        item["ShotTime"] = (int)(cannon.ReloadSeconds * 1000);
        item["FireAngle"] = cannon.FireAngle;
        item["Flight"] = cannon.Range;
        Send(ViewPackets.AddItem(SeaPackets.ShipEquipmentView, slot, SeaPackets.Known(item)), "(cannons)");
    }

    private bool HasAmmunition(string name)
        => name.Length > 0 && Character.Ammunition != null && Character.Ammunition.GetValueOrDefault(name) > 0 && AmmunitionIndex(name) > 0;

    private string FirstAmmunition()
        => Sea.Data.Ammunition.FirstOrDefault(kind => Character.Ammunition!.GetValueOrDefault(kind.Name) > 0)?.Name
            ?? Sea.Data.DefaultAmmunition.Name;

    private int AmmunitionIndex(string name) => Sea.Data.Ammunition.FindIndex(kind => kind.Name == name) + 1;

    private void SeaNotice(string text)
    {
        long now = SeaWorld.Now;
        if (now - _seaNoticeAt < SeaNoticeGap) return;
        _seaNoticeAt = now;
        SystemMessage(text, SystemMessageKind.Prompt);
    }

    private static float SeaDistance(float fromX, float fromZ, float toX, float toZ)
        => MathF.Sqrt((toX - fromX) * (toX - fromX) + (toZ - fromZ) * (toZ - fromZ));

    private const double FireArcSlackTenths = 10.0;

    private static int BroadsideSide(int headingTenths, float dx, float dz) => RelativeBearing(headingTenths, dx, dz) >= 0 ? 1 : 0;

    private static double RelativeBearing(int headingTenths, float dx, float dz)
    {
        double bearing = Math.Atan2(dx, dz) * 1800.0 / Math.PI;
        double relative = (bearing - headingTenths) % 3600.0;
        if (relative > 1800.0) relative -= 3600.0;
        if (relative <= -1800.0) relative += 3600.0;
        return relative;
    }
}
