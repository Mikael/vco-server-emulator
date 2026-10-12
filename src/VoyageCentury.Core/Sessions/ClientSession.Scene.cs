using System.Numerics;
using VoyageCentury.Core.Configuration;
using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed partial class ClientSession
{
    private static readonly string[] PublicProperties =
    [
        "Name", "Model", "ActionSet", "CollideRadius", "Face", "BodyType", "Hair", "HairColor",
        "TexColor", "Hat", "CapColor", "LeftHand", "RightHand", "HP", "MaxHP", "Level", "Title", "RatioHP", "RatioSP",
        "SelectState", "StallInfo", "TeamNum", "ConsName", "ConsFlag",
    ];

    private static readonly string[] PrivateShipProperties = ["Money", "Load", "LoadMax", "VisualRange"];

    private WorldSettings WorldSettings => Context.Settings.World;

    private Vector3 StartPosition => new(WorldSettings.StartX, WorldSettings.StartY, WorldSettings.StartZ);

    public void BeginWorld(CharacterRecord character, bool persistent)
    {
        Character = character;
        Persistent = persistent;
        foreach (ShortcutEntry shortcut in character.Shortcuts.Values.Where(entry => entry.Kind == 1 && entry.Name.StartsWith("NEW_", StringComparison.Ordinal)))
            shortcut.Kind = ViewId.BagItems;
        if (character.Money < 0) character.Money = WorldSettings.StartMoney;
        if (character.Skills.Count == 0)
        {
            for (int i = 0; i < Context.Data.Creation.Skills.Count; i++)
                character.Skills.Add(new SkillEntry { Id = i, Level = 1 });
        }
        EnsureInventory();
        foreach (string crafting in new[] { "SKILL_ALCHEMY", "SKILL_FOUNDRY", "SKILL_SEWING", "SKILL_SHIPBUILDING" })
            character.LearnedSkills.TryAdd(crafting, 1);

        Hp = MaxHp = 100;
        Properties = new PropertyBag
        {
            ["Name"] = character.Name,
            ["Model"] = character.Role,
            ["LandModel"] = character.Role,
            ["ActionSet"] = 0,
            ["CollideRadius"] = 0.5f,
            ["IsRun"] = 1,
            ["Face"] = character.Face,
            ["BodyType"] = character.Body,
            ["Hair"] = character.Hair,
            ["HairColor"] = character.HairColor,
            ["TexColor"] = 0,
            ["Hat"] = 0,
            ["CapColor"] = 0,
            ["LeftHand"] = 0,
            ["RightHand"] = 0,
            ["ArmToxicLevel"] = 0,
            ["WeaponEffect"] = 0,
            ["Money"] = character.Money,
            ["Load"] = 0f,
            ["LoadMax"] = 100f,
            ["VisualRange"] = 100f,
            ["HP"] = Hp,
            ["MaxHP"] = MaxHp,
            ["SP"] = 100,
            ["MaxSP"] = 100,
            ["Level"] = character.Level,
            ["Dead"] = 0,
            ["AT"] = 15,
            ["DF"] = 8,
            ["HitRatio"] = 80,
            ["DodgeRatio"] = 10,
            ["Rank"] = 0,
            ["CountryName"] = "Greece",
            ["TeamNum"] = 0,
            ["EnableExchange"] = 1,
            ["SelectState"] = 0,
            ["ShortcutPage"] = character.ShortcutPage,
            ["StallBuyMax"] = 4,
            ["StallSellMax"] = 4,
            ["RatioHP"] = 100,
            ["RatioSP"] = 100,
            ["NumSailor"] = character.Sailors,
            ["NumSailorBad"] = 0,
            ["SupplySum"] = 100,
            ["SupplyDay"] = 10,
            ["SupplyCost"] = 1,
            ["Credit"] = 0,
            ["Notoriety"] = 0,
            ["PosX"] = WorldSettings.StartX,
            ["PosY"] = WorldSettings.StartY,
            ["PosZ"] = WorldSettings.StartZ,
            ["Orient"] = 0f,
        };
        if (character.Title.Length > 0) Properties["Title"] = character.Title;

        ApplyLevel();
        Hp = MaxHp;
        Properties["HP"] = Hp;

        Send(LoginPackets.PropertyTableDefinition(), "(property table)");
        Send(LoginPackets.OwnProperties(Properties), "(own properties)");
        foreach (TableDefinition table in GameTables.All.Concat(ProgressTables.All).Concat(MailTables.All).Concat(CraftingTables.All).Concat(FleetTables.All))
            Send(LoginPackets.TableCreate(table), $"({table.Name} table)");

        bool legacyScene = character.Scene / 1000 == 1001;
        int scene = character.Scene != 0 && !legacyScene && Context.Data.Scenes.Exists(character.Scene) ? character.Scene : WorldSettings.StartScene;
        Vector3? position = character.Position is { Length: 3 } saved && scene == character.Scene
            ? new Vector3(saved[0], saved[1], saved[2])
            : null;
        EnterScene(scene, position);
        EnterWorldSocial();
        EnterWorldGuild();
        Log.Info(Tag, $"{Name} entered the world in scene {scene}{(persistent ? "" : " (not saved)")}");
    }

    public void EnterScene(int scene, Vector3? position = null)
    {
        Vector3 at = position ?? (scene == WorldSettings.StartScene ? StartPosition : Context.Data.Scenes.SpawnPoint(scene));
        _shop.Clear();
        CloseShipyard();
        CloseFleetViews();
        if (InWorld) LeaveScene();

        Scene = scene;
        Send(ScenePackets.EnterScene(scene), $"(enter scene {scene})");
        AtSea = Context.Data.Scenes.IsSea(scene) && !ForceLand;
        if (AtSea)
        {
            at = new Vector3(at.X, 0f, at.Z);
            Send(ScenePackets.CreateObject(ProtocolConstants.SelfObjectId, ObjectKind.SeaSelf, ActiveShipModel), "(spawn ship)");
            Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, ShipProperties()), "(ship properties)");
        }
        else
        {
            Send(ScenePackets.CreateObject(ProtocolConstants.SelfObjectId, ObjectKind.Self, Character.Role), "(spawn player)");
            Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, Properties), "(player properties)");
        }
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, at.X, at.Y, at.Z), "(place player)");
        Send(ScenePackets.ShowObject(ProtocolConstants.SelfObjectId), "(show player)");
        Send(ScenePackets.SceneState(WorldSettings.Hour, WorldSettings.Weather), "(time, weather, wind)");

        Mover = new Mover(at.X, at.Z, AtSea ? ShipSpeed : WorldSettings.RunSpeed);
        Height = at.Y;

        foreach (NpcDefinition npc in Context.Data.Npcs.Values.Where(npc => npc.Scene == scene))
            SendNpc(npc);
        foreach (MonsterDefinition monster in Context.Data.Monsters.Values.Where(monster => monster.Scene == scene))
        {
            if (!Context.World.Monster(monster).Dead) SendMonster(monster, "(monster)");
        }
        SendSceneLinks();

        if (!_inventorySent)
        {
            _inventorySent = true;
            Send(TablePackets.Rows(GameTables.Skill, SkillRows()), "(skills)");
            SendShortcuts(clear: false);
            SendSocialTables(clear: false);
            SendInventory();
            SendDurability();
            SendProgress();
            SendMail();
            SendRecipes();
        }

        Context.World.Add(this);
        foreach (ClientSession other in Others())
        {
            other.ShowTo(this);
            ShowTo(other);
        }
        TeamDirectory.Refresh(this);
        GuildDirectory.Refresh(this);
        ShowPet();
        SendSeaObjects();
    }

    public float ShipSpeed => MathF.Max(0.5f, WorldSettings.ShipSpeed * FleetSpeedFactor * Sail);

    public void LeaveScene()
    {
        CancelTradeAndStall();
        LeaveSeaCombat();
        ToOthers(ScenePackets.RemoveObject(ObjectId), $"(player {Name} leaves)");
        Context.World.Remove(this);
    }

    public void Travel(int scene)
    {
        Send(ScenePackets.LeaveScene(), "(leave scene)");
        EnterScene(scene);
    }

    public void SailOff()
    {
        if (SailByLink()) return;
        int target = Scene / 1000 * 1000 + (AtSea ? 1 : 3);
        ForceLand = false;
        if (!Context.Data.Scenes.Exists(target))
        {
            SystemMessage("There is nowhere to sail from here.");
            return;
        }
        Log.Info(Tag, $"{Name} {(AtSea ? "comes ashore in" : "sails off to")} scene {target}");
        Travel(target);
    }

    public void ShowTo(ClientSession other)
    {
        if (Mover == null) return;
        (float x, float z) = Mover.Now();
        if (AtSea)
        {
            other.Send(ScenePackets.CreateObject(ObjectId, ObjectKind.SeaPlayer, ActiveShipModel), $"(ship of {Name} appears)");
            PropertyBag ship = ShipProperties();
            foreach (string name in PrivateShipProperties) ship.Remove(name);
            other.Send(ScenePackets.ObjectProperties(ObjectId, ship), $"(ship of {Name} props)");
        }
        else
        {
            other.Send(ScenePackets.CreateObject(ObjectId, ObjectKind.Player, Character.Role), $"(player {Name} appears)");
            other.Send(ScenePackets.ObjectProperties(ObjectId, PublicBag()), $"(player {Name} props)");
        }
        other.Send(ScenePackets.PlaceObject(ObjectId, x, Height, z, Mover.DestinationX, Mover.DestinationZ, Mover.Speed,
            AtSea ? 0 : ProtocolConstants.RunAction, Mover.Heading, Mover.Heading), $"(player {Name} position)");
        other.Send(ScenePackets.ShowObject(ObjectId), $"(player {Name} visible)");
    }

    public void MoveTo(float x, float z)
    {
        if (Mover == null) return;
        float speed = AtSea ? ShipSpeed : Walking ? ProtocolConstants.WalkSpeed : WorldSettings.RunSpeed;
        int action = AtSea ? 0 : Walking ? ProtocolConstants.WalkAction : ProtocolConstants.RunAction;
        Mover.Speed = speed;
        int previous = Mover.Heading;
        (float fromX, float fromZ) = Mover.MoveTo(x, z);
        Log.Debug(Tag, $"MOVE ({fromX:F1}, {fromZ:F1}) -> ({x:F1}, {z:F1})");
        int heading = Mover.Heading;
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, fromX, Height, fromZ, x, z, speed, action, previous, heading), "(move)");
        ToOthers(ScenePackets.PlaceObject(ObjectId, fromX, Height, fromZ, x, z, speed, action, previous, heading), $"({Name} moves)");
    }

    public void TeleportTo(float x, float z)
    {
        if (Mover == null) return;
        int heading = Mover.Heading;
        Mover = new Mover(x, z, Mover.Speed);
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, x, Height, z, orient: heading), "(teleport)");
        ToOthers(ScenePackets.PlaceObject(ObjectId, x, Height, z, orient: heading), $"({Name} teleports)");
    }

    public bool Walking { get; private set; }

    public void SetWalking(bool walking)
    {
        Walking = walking;
        SetProperty("IsRun", walking ? 0 : 1);
        if (Mover != null && !AtSea) MoveTo(Mover.DestinationX, Mover.DestinationZ);
    }

    public void StopAt(float x, float z)
    {
        if (Mover == null) return;
        (float nowX, float nowZ) = Mover.Now();
        float dx = x - nowX;
        float dz = z - nowZ;
        if (dx * dx + dz * dz > 60f * 60f) (x, z) = (nowX, nowZ);
        int heading = Mover.Heading;
        Mover = new Mover(x, z, Mover.Speed);
        ToOthers(ScenePackets.PlaceObject(ObjectId, x, Height, z, orient: heading), $"({Name} stops)");
    }

    public void PlayAction(int action, string note, bool attack = false)
    {
        if (Mover == null) return;
        (float x, float z) = Mover.Now();
        Mover.MoveTo(x, z);
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, x, Height, z, orient: Mover.Heading), note);
        ToOthers(ScenePackets.PlaceObject(ObjectId, x, Height, z, orient: Mover.Heading), note);
        Send(ScenePackets.PlayAction(ProtocolConstants.SelfObjectId, action, attack), note);
        ToOthers(ScenePackets.PlayAction(ObjectId, action, attack), note);
    }

    public void SetSail(float fraction)
    {
        Sail = Math.Clamp(fraction, 0f, 1f);
        if (!AtSea || Mover == null) return;
        float destinationX = Mover.DestinationX;
        float destinationZ = Mover.DestinationZ;
        Mover.Speed = ShipSpeed;
        SetProperty("ShipSpeedAdj", Sail);
        (float x, float z) = Mover.MoveTo(destinationX, destinationZ);
        int heading = Mover.Heading;
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, x, Height, z, destinationX, destinationZ, ShipSpeed, 0, heading, heading), "(sail change)");
        ToOthers(ScenePackets.PlaceObject(ObjectId, x, Height, z, destinationX, destinationZ, ShipSpeed, 0, heading, heading), $"({Name} sail change)");
    }

    public void SetProperty(string name, object value, bool visibleToOthers = false)
    {
        Properties[name] = value;
        var update = new PropertyBag { [name] = value };
        Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, update), $"({name})");
        if (visibleToOthers) ToOthers(ScenePackets.ObjectProperties(ObjectId, update), $"({Name} {name})");
    }

    public void SendMonster(MonsterDefinition monster, string note)
    {
        Send(ScenePackets.CreateObject(monster.Id, ObjectKind.Npc, monster.Model), note);
        Send(ScenePackets.ObjectProperties(monster.Id, new PropertyBag
        {
            ["Name"] = monster.Name,
            ["Model"] = monster.Model,
            ["ActionSet"] = 0,
            ["CollideRadius"] = 0.5f,
            ["HP"] = Context.World.Monster(monster).Hp,
            ["MaxHP"] = monster.Hp,
            ["RatioHP"] = 100 * Context.World.Monster(monster).Hp / Math.Max(1, monster.Hp),
            ["RatioSP"] = 100,
            ["Level"] = 1,
        }), note);
        Send(ScenePackets.PlaceObject(monster.Id, monster.X, monster.Y, monster.Z), note);
        Send(ScenePackets.ShowObject(monster.Id), note);
    }

    private void SendNpc(NpcDefinition npc)
    {
        Send(ScenePackets.CreateObject(npc.Id, ObjectKind.Npc, npc.Model), $"(spawn {npc.Name})");
        Send(ScenePackets.ObjectProperties(npc.Id, new PropertyBag
        {
            ["Name"] = npc.Name,
            ["Model"] = npc.Model,
            ["ActionSet"] = 0,
            ["CollideRadius"] = 0.5f,
            ["HP"] = 100,
            ["MaxHP"] = 100,
            ["RatioHP"] = 100,
            ["RatioSP"] = 100,
            ["Level"] = 1,
        }), $"(props {npc.Name})");
        Send(ScenePackets.PlaceObject(npc.Id, npc.X, npc.Y, npc.Z), $"(place {npc.Name})");
        Send(ScenePackets.ShowObject(npc.Id), $"(show {npc.Name})");
    }

    private PropertyBag PublicBag()
    {
        var bag = new PropertyBag();
        foreach (string name in PublicProperties)
        {
            if (Properties.TryGetValue(name, out object? value)) bag[name] = value;
        }
        return bag;
    }

    private PropertyBag ShipProperties()
    {
        var ship = new PropertyBag(Properties)
        {
            ["Model"] = WorldSettings.ShipModel,
            ["ShipModel"] = WorldSettings.ShipModel,
            ["Dead"] = 0,
            ["AutoDrive"] = Properties.Int("AutoDrive", 0),
            ["RatioSailHP"] = 100,
            ["RatioShipHP"] = 100,
            ["RatioSailorHP"] = 100,
            ["RatioHP"] = 100,
            ["ShipSpeedAdj"] = Sail,
            ["S/Name"] = $"{Name}'s ship",
            ["SailState"] = 0,
            ["Bumping"] = 0,
            ["MarineStrategy"] = 0,
            ["S/ModelCannonL"] = 0,
            ["S/ModelCannonR"] = 0,
            ["S/NumCannonL"] = 0,
            ["S/NumCannonR"] = 0,
            ["S/SailTex"] = Properties.Int("S/SailTex", 2900000),
            ["S/Flag"] = 0,
            ["S/RandSeed"] = 1,
            ["S/FishNet"] = 0,
            ["S/ShipDamage"] = 0,
            ["S/Level"] = 0,
            ["S/MastFront"] = 2211101,
            ["S/MastMiddle"] = 2211401,
            ["S/MastBack"] = 2211101,
            ["HaveConsTask"] = 0,
            ["SDamageReduce"] = 0,
            ["SDamageAdd"] = 0,
            ["FireAnchor"] = 0,
            ["RotSpeedAdd"] = 0,
        };
        ApplySeaShip(ship);
        ApplyFleetShip(ship);
        return ship;
    }

    private List<object[]> SkillRows() => ProgressSkillRows();
}
