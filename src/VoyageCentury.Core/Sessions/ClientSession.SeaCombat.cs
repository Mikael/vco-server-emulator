using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed partial class ClientSession
{
    private const int CannonPhoto = 160101;
    private const int AmmunitionPhoto = 190104;
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

    public string ShipName => Character.ShipName.Length > 0 ? Character.ShipName : $"{Name}'s ship";

    public CannonDefinition ShipCannon => Sea.Data.FindCannon(Character.CannonName) ?? Sea.Data.DefaultCannon;

    public void EnsureShipState()
    {
        SeaData data = Sea.Data;
        ShipRules rules = data.Rules;
        if (Character.CannonsLeft < 0) Character.CannonsLeft = Math.Min(rules.StartCannonsPerSide, rules.CannonsPerSide);
        if (Character.CannonsRight < 0) Character.CannonsRight = Math.Min(rules.StartCannonsPerSide, rules.CannonsPerSide);
        if (Character.CannonName.Length == 0) Character.CannonName = data.DefaultCannon.Name;
        Character.Ammunition ??= new Dictionary<string, int> { [data.DefaultAmmunition.Name] = rules.StartAmmunition };
        if (!HasAmmunition(Character.AmmunitionLeft)) Character.AmmunitionLeft = FirstAmmunition();
        if (!HasAmmunition(Character.AmmunitionRight)) Character.AmmunitionRight = FirstAmmunition();
        if (Character.ShipHull < 0) Character.ShipHull = FleetHullMax;
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
            ["AmmoIndexL"] = AmmunitionIndex(Character.AmmunitionLeft),
            ["AmmoIndexR"] = AmmunitionIndex(Character.AmmunitionRight),
        };
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
        Send(ScenePackets.CreateObject(definition.Id, ObjectKind.SeaNpc, definition.Model), note);
        Send(ScenePackets.ObjectProperties(definition.Id, SeaPackets.Known(new PropertyBag
        {
            ["Name"] = definition.Name,
            ["S/Name"] = definition.Name,
            ["Model"] = definition.Model,
            ["ShipModel"] = definition.Model,
            ["Level"] = definition.Level,
            ["Dead"] = ship.Dead ? 1 : 0,
            ["RatioShipHP"] = ship.RatioHull,
            ["RatioSailHP"] = 100,
            ["RatioSailorHP"] = 100,
            ["RatioHP"] = ship.RatioHull,
            ["S/ModelCannonL"] = definition.CannonModel,
            ["S/ModelCannonR"] = definition.CannonModel,
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
        })), note);
        Send(ScenePackets.PlaceObject(definition.Id, definition.X, 0f, definition.Z, orient: definition.Heading), note);
        Send(ScenePackets.ShowObject(definition.Id), note);
    }

    public bool OnSeaObjectClicked(int objectId)
    {
        if (!AtSea || Mover == null) return false;
        SeaNpcState? ship = Sea.Find(objectId, Scene);
        if (ship == null) return false;
        if (!ship.Dead && !ship.Gone && !SeaSunk) FireBroadside(ship);
        return true;
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

    public void TakeSeaVolley(SeaNpcState ship)
    {
        if (Mover == null || !AtSea || SeaSunk || ship.Dead) return;
        SeaNpcDefinition definition = ship.Definition;
        (float x, float z) = Mover.Now();
        float distance = SeaDistance(definition.X, definition.Z, x, z);
        if (distance > definition.Range * 1.2f) return;

        int side = BroadsideSide(definition.Heading, x - definition.X, z - definition.Z);
        int balls = Math.Max(1, definition.Cannons);
        int hits = 0;
        int damage = 0;
        for (int i = 0; i < balls; i++)
        {
            if (Random.Shared.NextSingle() > definition.Accuracy) continue;
            hits++;
            damage += Random.Shared.Next(definition.DamageMin, Math.Max(definition.DamageMin, definition.DamageMax) + 1);
        }

        float scatter = Sea.Data.Rules.BroadsideScatter;
        int seed = Random.Shared.Next(1, 30000);
        const float ballSpeed = 60f;
        Send(SeaPackets.Broadside(definition.Id, ProtocolConstants.SelfObjectId, side, definition.ShellType, balls, x, z, scatter, ballSpeed, seed),
            $"({definition.Name} fires)");
        ToOthers(SeaPackets.Broadside(definition.Id, ObjectId, side, definition.ShellType, balls, x, z, scatter, ballSpeed, seed),
            $"({definition.Name} fires at {Name})");

        Sea.Later(Math.Min(4.0, distance / ballSpeed), () =>
        {
            if (!InWorld || !AtSea || SeaSunk || Scene != definition.Scene) return;
            string text = damage > 0 ? $"-{damage}" : "Miss";
            float fraction = (float)hits / balls;
            Send(SeaPackets.Volley(ProtocolConstants.SelfObjectId, definition.ShellType, balls, fraction, definition.X, definition.Z, text), "(volley lands)");
            ToOthers(SeaPackets.Volley(ObjectId, definition.ShellType, balls, fraction, definition.X, definition.Z, text), $"(volley lands on {Name})");
            if (damage <= 0) return;
            if (ShipHull - damage > 0)
            {
                SetShipHull(ShipHull - damage);
                return;
            }
            LoseShip(definition.Name);
        });
    }

    private void FireBroadside(SeaNpcState ship)
    {
        EnsureShipState();
        SeaNpcDefinition definition = ship.Definition;
        (float x, float z) = Mover!.Now();
        float distance = SeaDistance(x, z, definition.X, definition.Z);
        int side = BroadsideSide(Mover.Heading, definition.X - x, definition.Z - z);
        long now = SeaWorld.Now;
        if (now < _seaReadyAt[side]) return;

        CannonDefinition cannon = ShipCannon;
        int cannons = side == 0 ? Character.CannonsLeft : Character.CannonsRight;
        string ammunitionName = side == 0 ? Character.AmmunitionLeft : Character.AmmunitionRight;
        int stock = Character.Ammunition!.GetValueOrDefault(ammunitionName);
        if (cannons <= 0)
        {
            SeaNotice($"You have no cannons on the {(side == 0 ? "port" : "starboard")} side.");
            return;
        }
        if (stock <= 0)
        {
            SeaNotice("You are out of cannonballs.");
            return;
        }
        if (distance > cannon.Range * 1.1f)
        {
            SeaNotice("The target is out of range.");
            return;
        }

        AmmunitionDefinition ammunition = Sea.Data.FindAmmunition(ammunitionName) ?? Sea.Data.DefaultAmmunition;
        int balls = Math.Min(cannons, stock);
        int hits = 0;
        int damage = 0;
        for (int i = 0; i < balls; i++)
        {
            if (Random.Shared.NextSingle() > cannon.Accuracy) continue;
            hits++;
            damage += Random.Shared.Next(ammunition.DamageMin, Math.Max(ammunition.DamageMin, ammunition.DamageMax) + 1);
        }

        int reload = (int)(Math.Max(0.5f, cannon.ReloadSeconds) * 1000);
        _seaReadyAt[side] = now + reload;
        Character.Ammunition[ammunitionName] = stock - balls;
        RefreshAmmunition(ammunitionName);
        if (stock - balls <= 0)
        {
            if (side == 0) Character.AmmunitionLeft = FirstAmmunition();
            else Character.AmmunitionRight = FirstAmmunition();
            SendAmmunitionChoice();
        }

        float scatter = Sea.Data.Rules.BroadsideScatter;
        int seed = Random.Shared.Next(1, 30000);
        Send(SeaPackets.Broadside(ProtocolConstants.SelfObjectId, definition.Id, side, ammunition.ShellType, balls,
            definition.X, definition.Z, scatter, cannon.BallSpeed, seed), "(broadside)");
        ToOthers(SeaPackets.Broadside(ObjectId, definition.Id, side, ammunition.ShellType, balls,
            definition.X, definition.Z, scatter, cannon.BallSpeed, seed), $"({Name} fires a broadside)");
        Send(SeaPackets.CannonReload(side, reload), "(reload)");
        Sea.Provoke(ship, this);
        Log.Debug(Tag, $"{Name} fires {balls} x {ammunitionName} at {definition.Name}: {hits} hit(s), {damage} damage");

        Sea.Later(Math.Min(4.0, distance / Math.Max(1f, cannon.BallSpeed)), () => LandBroadside(ship, ammunition.ShellType, balls, hits, damage, x, z));
    }

    private void LandBroadside(SeaNpcState ship, int shellType, int balls, int hits, int damage, float fromX, float fromZ)
    {
        if (ship.Dead) return;
        SeaNpcDefinition definition = ship.Definition;
        byte[] volley = SeaPackets.Volley(definition.Id, shellType, balls, (float)hits / balls, fromX, fromZ,
            damage > 0 ? $"-{damage}" : "Miss", 0xffff00);
        List<ClientSession> watchers = Sea.Sailors(definition.Scene).ToList();
        foreach (ClientSession session in watchers) session.Send(volley, "(broadside lands)");
        if (damage <= 0) return;

        ship.Hull -= damage;
        if (ship.Hull > 0)
        {
            byte[] hull = ScenePackets.ObjectProperties(definition.Id, SeaPackets.Known(new PropertyBag
            {
                ["RatioShipHP"] = ship.RatioHull,
                ["RatioHP"] = ship.RatioHull,
            }));
            foreach (ClientSession session in watchers) session.Send(hull, $"({definition.Name} hull)");
            return;
        }

        Sea.Sink(ship);
        if (!InWorld) return;
        Character.ShipsSunk++;
        SetMoney(Money + definition.Gold);
        SystemMessage($"You sank the {definition.Name} and salvaged {definition.Gold} gold.");
        GainExperience(definition.Experience);
        Save();
        Log.Info(Tag, $"{Name} sank {definition.Name}");
    }

    private void LoseShip(string sunkBy)
    {
        SeaSunk = true;
        Sea.Forget(this);
        Character.ShipHull = ShipHullMax;
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
            int port = Sea.Data.Rules.PortScene;
            if (!Context.Data.Scenes.Exists(port)) port = WorldSettings.StartScene;
            ForceLand = false;
            Travel(port);
            SetShipHull(ShipHullMax);
            SystemMessage("You were pulled from the water and brought back to port. Your ship has been repaired.");
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
                ["Photo"] = AmmunitionPhoto,
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
            ["Photo"] = CannonPhoto,
            ["Kind"] = 0,
            ["ItemType"] = SeaPackets.ItemTypeShipEquipment,
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

    private static int BroadsideSide(int headingTenths, float dx, float dz)
    {
        double bearing = Math.Atan2(dx, dz) * 1800.0 / Math.PI;
        double relative = (bearing - headingTenths) % 3600.0;
        if (relative > 1800.0) relative -= 3600.0;
        if (relative <= -1800.0) relative += 3600.0;
        return relative >= 0 ? 1 : 0;
    }
}
