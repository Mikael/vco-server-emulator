using System.Numerics;
using BountyBay.Core.Configuration;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private static readonly string[] PublicProperties =
    [
        "Name", "Model", "ActionSet", "CollideRadius", "Face", "BodyType", "Hair", "HairColor",
        "Photo", "TexColor", "Hat", "CapColor", "LeftHand", "RightHand", "HP", "MaxHP", "Level", "Title", "RatioHP", "RatioSP",
        "SelectState", "StallInfo", "TeamNum", "ConsName", "ConsFlag", "Notoriety", "Credit", "UnionName", "StartEffect", "EvolColor",
        "WorkType", "WorkLevel", "WorkName",
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
            {
                if (Context.Data.Rules.Skills.InitialSkills.Contains(Context.Data.Creation.Skills[i]))
                    character.Skills.Add(new SkillEntry { Id = i, Level = 1 });
            }
        }
        EnsureInventory();
        MigrateSkills();

        Hp = MaxHp = 100;
        Sp = MaxSp = 100;
        Properties = new PropertyBag
        {
            ["Name"] = character.Name,
            ["ConsName"] = "",
            ["ConsFlag"] = 0,
            ["UnionName"] = "",
            ["Model"] = character.Role,
            ["LandModel"] = character.Role,
            ["ActionSet"] = 0,
            ["CollideRadius"] = 0.5f,
            ["IsRun"] = 1,
            ["Face"] = character.Face,
            ["Photo"] = character.Face,
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
            ["Level"] = 1,
            ["Dead"] = 0,
            ["AT"] = 15,
            ["DF"] = 8,
            ["HitRatio"] = 80,
            ["DodgeRatio"] = 10,
            ["Rank"] = character.Rank,
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
            ["NumSailorBad"] = character.SailorsWounded,
            ["SupplySum"] = 100,
            ["SupplyDay"] = 10,
            ["SupplyCost"] = 1,
            ["Credit"] = character.Reputation,
            ["Notoriety"] = 0,
            ["PosX"] = WorldSettings.StartX,
            ["PosY"] = WorldSettings.StartY,
            ["PosZ"] = WorldSettings.StartZ,
            ["Orient"] = 0f,
        };
        if (character.Title.Length > 0) Properties["Title"] = character.Title;
        AddShipWindowProperties();
        RefreshAppearance();

        ComputeStats();
        Hp = MaxHp;
        Sp = MaxSp;
        Properties["HP"] = Hp;
        Properties["SP"] = Sp;
        Properties["RatioSP"] = 100;
        if (PropertyTable.Has("FightSkill")) Properties["FightSkill"] = FightingSkill();
        if (PropertyTable.Has("BankMoney")) Properties["BankMoney"] = character.Bank;
        PrepareHudProperties();

        ApplyBboSocialProperties();
        ApplyColonyProperties();
        ApplyBboCharacterProperties();
        ApplyLeisureProperties();
        Send(LoginPackets.PropertyTableDefinition(), "(property table)");
        ApplyTotemProperties();
        Send(LoginPackets.OwnProperties(Properties), "(own properties)");
        foreach (TableDefinition table in GameTables.All.Concat(ProgressTables.All).Concat(MailTables.All).Concat(CraftingTables.All).Concat(FleetTables.All).Concat(BboSocialTables.All).Concat(QuestMarkTables.All).Concat(AideTables.All).Concat(BattleTables.All))
            Send(LoginPackets.TableCreate(table), $"({table.Name} table)");

        bool legacyScene = character.Scene / 1000 == 1001;
        int scene = character.Scene != 0 && !legacyScene && Context.Data.Scenes.Exists(character.Scene) ? character.Scene : WorldSettings.StartScene;
        scene = GuildWarLoginScene(scene);
        Vector3? position = character.Position is { Length: 3 } saved && scene == character.Scene
            ? new Vector3(saved[0], saved[1], saved[2])
            : null;
        EnterScene(scene, position);
        EnterWorldSocial();
        EnterWorldGuild();
        EnterWorldGuildBuild();
        EnterWorldBbo();
        EnterWorldBboCharacter();
        SendRobberyLevels();
        EnterWorldAides();
        EnterWorldTotems();
        StartOnlineRewards();
        EnterWorldLeisure();
        BattleEnterWorld();
        AnnounceOverflow();
        CollectConsignment();
        AuctionLoginNotice();
        Log.Info(Tag, $"{Name} entered the world in scene {scene}{(persistent ? "" : " (not saved)")}");
    }

    private bool _ownPropertiesSent;

    public void EnterScene(int scene, Vector3? position = null)
    {
        Vector3 at = position ?? (scene == WorldSettings.StartScene ? StartPosition : Context.Data.Scenes.SpawnPoint(scene));
        _shop.Clear();
        if (TalkingTo != 0)
        {
            TalkingTo = 0;
            CloseRebuild();
            CloseNpcDialogue();
        }
        CloseShipyard();
        CloseFleetViews();
        CloseLoot();
        CloseCompound();
        ResetSceneState();
        if (InWorld) LeaveScene();

        Scene = scene;
        Send(ScenePackets.EnterScene(scene), $"(enter scene {scene})");
        if (!_ownPropertiesSent)
        {
            _ownPropertiesSent = true;
            Send(LoginPackets.OwnProperties(Properties), "(own properties after entering)");
        }
        AtSea = (Context.Data.Scenes.IsSea(scene) || BboOceanScene(scene) || BattleRules.CopyInScene(scene) is { Sea: true }) && !ForceLand;
        if (AtSea)
        {
            at = new Vector3(at.X, 0f, at.Z);
            Send(ScenePackets.CreateObject(ProtocolConstants.SelfObjectId, BboOwnShipKind, ActiveShipModel), "(spawn ship)");
            Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, ShipProperties()), "(ship properties)");
        }
        else
        {
            Send(ScenePackets.CreateObject(ProtocolConstants.SelfObjectId, ObjectKind.Self, Character.Role), "(spawn player)");
            Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, Properties), "(player properties)");
        }
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, at.X, at.Y, at.Z), "(place player)");
        Send(ScenePackets.ShowObject(ProtocolConstants.SelfObjectId), "(show player)");
        Send(ScenePackets.SceneState(Context.World.Time, WorldSettings.Weather, CityName, SceneName, scene: Scene, sceneType: SceneType), "(time, weather, wind)");

        Mover = new Mover(at.X, at.Z, AtSea ? ShipSpeed : WorldSettings.RunSpeed, 0, AtSea ? ShipTurnRate : 0);
        Height = at.Y;

        foreach (NpcDefinition npc in Context.Data.Npcs.Values.Where(npc => npc.Scene == scene))
            SendNpc(npc);
        SendQuestMarks();
        SendBboStuntCooldowns();
        foreach (MonsterDefinition monster in Context.Data.Monsters.Values.Where(monster => monster.Scene == scene))
        {
            if (!Context.World.Monster(monster).Dead) SendMonster(monster, "(monster)");
        }
        SendSceneLinks();
        BboSendOceanDocks();
        if (AtSea) WarnShipCondition();

        if (!_inventorySent)
        {
            _inventorySent = true;
            Send(TablePackets.Rows(GameTables.Skill, SkillRows()), "(skills)");
            SendShortcuts(clear: false);
            SendSocialTables(clear: false);
            SendInventory();
            TakeOffWrongSexClothes();
            SendDurability();
            SendProgress();
            SendMail();
            SendRecipes();
        }
        else
        {
            Send(TablePackets.Clear(GameTables.Shortcut), "(clear shortcuts)");
            SendShortcuts(clear: false);
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
        RefreshLoad();
        RefreshSupplies();
    }

    public float ShipSpeed => CombatRules.ShipSpeed(Sea.Data.Rules, WorldSettings.ShipSpeed, ActiveShipType.Triangular + ActiveShipType.Square,
        EffectiveSail, ShipSail, FleetSailMax, Character.Sailors, FleetSailorsMax, ShipLoad, ShipLoadMax, SeaStanceSpeedFactor);

    public int ShipTurnRate => CombatRules.TurnRate(Sea.Data.Rules, ActiveShipType.Turning, ShipHull, ShipHullMax);

    public int SceneType => !AtSea ? 0 : Scene / 1000 == 1000 ? 2 : 1;

    public void LeaveScene()
    {
        CancelTradeAndStall();
        LeaveSeaCombat();
        ToOthers(ScenePackets.RemoveObject(ObjectId), $"(player {Name} leaves)");
        Context.World.Remove(this);
    }

    public void Travel(int scene)
    {
        SceneLink? harbour = ForceLand ? null : Links.Links.FirstOrDefault(link => link.Sail && link.TargetScene == scene);
        Send(ScenePackets.LeaveScene(), "(leave scene)");
        EnterScene(scene, harbour == null ? null : new Vector3(harbour.TargetX, 0f, harbour.TargetZ));
    }

    public void SailOff()
    {
        if (BboAtOcean) { BboComeAshoreFromOcean(); return; }
        if (SceneLocked()) return;
        if (!AtSea)
        {
            if (!ReadyToSail()) return;
            Character.LastPort = Scene;
        }
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
        if (Mover == null || GmHidden && !other.IsGameMaster) return;
        (float x, float z) = Mover.Now();
        if (AtSea)
        {
            other.Send(ScenePackets.CreateObject(ObjectId, BboOtherShipKind, ActiveShipModel), $"(ship of {Name} appears)");
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
            AtSea ? 0 : ProtocolConstants.RunAction, Mover.HeadingNow(), Mover.Heading, Mover.TurnRate), $"(player {Name} position)");
        other.Send(ScenePackets.ShowObject(ObjectId), $"(player {Name} visible)");
    }

    public void MoveTo(float x, float z)
    {
        if (Mover == null) return;
        _sitting = false;
        bool walking = Walking || Overloaded;
        float speed = AtSea ? ShipSpeed : walking ? ProtocolConstants.WalkSpeed : WorldSettings.RunSpeed;
        int action = AtSea ? 0 : walking ? ProtocolConstants.WalkAction : ProtocolConstants.RunAction;
        int course = Mover.Heading;
        int previous = Mover.HeadingNow();
        (float fromX, float fromZ) = Mover.MoveTo(x, z);
        if (OnCourseChanged(course, Mover.Heading)) speed = ShipSpeed;
        Mover.Speed = speed;
        Mover.TurnRate = AtSea ? ShipTurnRate : 0;
        Log.Debug(Tag, $"MOVE ({fromX:F1}, {fromZ:F1}) -> ({x:F1}, {z:F1})");
        int heading = Mover.Heading;
        int turn = Mover.TurnRate;
        Send(ScenePackets.MoveObject(ProtocolConstants.SelfObjectId, x, z, speed, heading, action, turn), "(move)");
        ToOthers(ScenePackets.MoveObject(ObjectId, x, z, speed, heading, action, turn), $"({Name} moves)");
        PetFollow(x, z, speed);
    }

    public void TeleportTo(float x, float z)
    {
        if (Mover == null) return;
        int heading = Mover.HeadingNow();
        Mover = new Mover(x, z, Mover.Speed, heading, Mover.TurnRate);
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, x, Height, z, orient: heading), "(teleport)");
        ToOthers(ScenePackets.PlaceObject(ObjectId, x, Height, z, orient: heading), $"({Name} teleports)");
    }

    public bool Walking { get; private set; }

    public bool Overloaded
        => Convert.ToSingle(Properties.GetValueOrDefault("Load", 0f)) * 100f
            > Convert.ToSingle(Properties.GetValueOrDefault("LoadMax", 100f)) * Context.Data.Rules.Character.RunLoadPercent;

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
        int heading = Mover.HeadingNow();
        Mover = new Mover(x, z, Mover.Speed, heading, Mover.TurnRate);
        ToOthers(ScenePackets.PlaceObject(ObjectId, x, Height, z, orient: heading), $"({Name} stops)");
    }

    private bool _sitting;

    public void ToggleSit()
    {
        if (AtSea || Mover == null || Hp <= 0) return;
        int action = Context.Data.Actions.Gesture(_sitting ? "standing" : "sitting");
        if (action < 0) return;
        _sitting = !_sitting;
        PlayAction(action, _sitting ? "(sits down)" : "(stands up)");
    }

    public void PlayAction(int action, string note, bool attack = false)
    {
        if (Mover == null) return;
        (float x, float z) = Mover.Now();
        Mover.MoveTo(x, z);
        int facing = Mover.HeadingNow();
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, x, Height, z, orient: facing), note);
        ToOthers(ScenePackets.PlaceObject(ObjectId, x, Height, z, orient: facing), note);
        Send(ScenePackets.PlayAction(ProtocolConstants.SelfObjectId, action, attack), note);
        ToOthers(ScenePackets.PlayAction(ObjectId, action, attack), note);
    }

    public const float SailLowest = 0.3f;
    public const float SailStep = 0.0875f;
    public const int SailLevels = 8;

    public static float SailFraction(float requested)
    {
        int level = (int)MathF.Round((Math.Clamp(requested, SailLowest, 1f) - SailLowest) / SailStep);
        return MathF.Min(1f, SailLowest + Math.Clamp(level, 0, SailLevels) * SailStep);
    }

    public void SetSail(float fraction)
    {
        Sail = SailFraction(fraction);
        if (!AtSea || Mover == null) return;
        SetProperty("ShipSpeedAdj", EffectiveSail);
        ReissueShipMove("sail change");
    }

    public void ReissueShipMove(string what)
    {
        if (!AtSea || Mover == null) return;
        float destinationX = Mover.DestinationX;
        float destinationZ = Mover.DestinationZ;
        float speed = ShipSpeed;
        int previous = Mover.HeadingNow();
        (float x, float z) = Mover.MoveTo(destinationX, destinationZ);
        Mover.Speed = speed;
        Mover.TurnRate = ShipTurnRate;
        int heading = Mover.Heading;
        int turn = Mover.TurnRate;
        Send(ScenePackets.MoveObject(ProtocolConstants.SelfObjectId, destinationX, destinationZ, speed, heading, 0, turn), $"({what})");
        ToOthers(ScenePackets.MoveObject(ObjectId, destinationX, destinationZ, speed, heading, 0, turn), $"({Name} {what})");
    }

    public void RefreshAppearance()
    {
        foreach ((string name, int value) in WornLook.Of(Character).Properties())
        {
            if (Properties.Int(name, -1) == value) continue;
            if (InWorld && !AtSea) SetProperty(name, value, visibleToOthers: true);
            else Properties[name] = value;
        }
        ApplyLeisureLook();
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
            ["ConsName"] = "",
            ["Model"] = monster.Model,
            ["Photo"] = monster.Model,
            ["ActionSet"] = 0,
            ["CollideRadius"] = monster.CollideRadius,
            ["HP"] = Context.World.Monster(monster).Hp,
            ["MaxHP"] = monster.Hp,
            ["RatioHP"] = 100 * Context.World.Monster(monster).Hp / Math.Max(1, monster.Hp),
            ["RatioSP"] = 100,
            ["Level"] = monster.Level,
            ["Dead"] = Context.World.Monster(monster).Dead ? 1 : 0,
            ["SelectState"] = 1,
        }), note);
        Send(ScenePackets.PlaceObject(monster.Id, Context.World.Monster(monster).X, monster.Y, Context.World.Monster(monster).Z), note);
        Send(ScenePackets.ShowObject(monster.Id), note);
    }

    private void SendNpc(NpcDefinition npc)
    {
        Send(ScenePackets.CreateObject(npc.Id, ObjectKind.Npc, npc.Model), $"(spawn {npc.Name})");
        Send(ScenePackets.ObjectProperties(npc.Id, new PropertyBag
        {
            ["Name"] = npc.Name,
            ["ConsName"] = "",
            ["ChatSet"] = npc.ChatSet,
            ["Model"] = npc.Model,
            ["Photo"] = npc.Portrait,
            ["ActionSet"] = 0,
            ["CollideRadius"] = 0.5f,
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
            ["RatioSailorHP"] = SailorRatio,
            ["RatioHP"] = 100,
            ["ShipSpeedAdj"] = EffectiveSail,
            ["S/Name"] = $"{Name}'s ship",
            ["SailState"] = 0,
            ["Bumping"] = 0,
            ["MarineStrategy"] = SeaStance,
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
