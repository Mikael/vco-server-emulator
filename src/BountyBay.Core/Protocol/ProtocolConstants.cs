namespace BountyBay.Core.Protocol;

public enum ClientMsg : ushort
{
    Login = 0x00,
    LeaveCharacterScreen = 0x01,
    EnterWorld = 0x02,
    CreateCharacter = 0x03,
    DeleteCharacter = 0x04,
    RequestCreateInfo = 0x05,
    Hello = 0x06,
    HandshakeReply = 0x07,
    Interact = 0x08,
    DialogueOption = 0x09,
    Unknown0A = 0x0a,
    TextCommand = 0x0b,
    SceneLoaded = 0x0f,
    HashedCommand = 0x10,
    Move = 0x12,
    LoginFromWeb = 0x15,
    LoginAnyway = 0x16,
    VersionReport = 0x17,
    ServiceChat = 0x13,
}

public enum ServerMsg : ushort
{
    Result = 0x00,
    CharacterList = 0x01,
    EnterScene = 0x03,
    LeaveScene = 0x04,
    CreateObject = 0x07,
    RemoveObject = 0x08,
    PropertyTable = 0x09,
    OwnProperties = 0x0b,
    SceneState = 0x0c,
    ObjectProperties = 0x0d,
    PlayAction = 0x11,
    AttackAction = 0x12,
    MailAcknowledge = 0x17,
    SailState = 0x19,
    Script = 0x1b,
    Chat = 0x13,
    SystemMessage = 0x15,
    NpcDialogue = 0x16,
    CreateInfo = 0x1a,
    ViewCreate = 0x1c,
    ViewDelete = 0x1d,
    ViewProperties = 0x1e,
    ViewAddItem = 0x1f,
    ViewRemoveItem = 0x20,
    ViewUpdateItem = 0x21,
    TableCreate = 0x22,
    TableRows = 0x23,
    TableDeleteRow = 0x24,
    TableUpdateRow = 0x25,
    TimedAttackAction = 0x26,
    ShowObject = 0x27,
    ServerList = 0x28,
    TableClear = 0x29,
    PlaceObject = 0x2a,
    MoveObject = 0x2b,
    SetObjectPosition = 0x2c,
    ServiceChat = 0x2f,
    TimedAction = 0x30,
    VerifyPicture = 0x31,
    Handshake = 0x32,
}

public enum LoginResult : byte
{
    None = 0,
    WrongAccount = 1,
    NameInUse = 3,
    AccountInUse = 8,
    IllegalName = 9,
    WrongVersion = 10,
    DeleteProtected = 13,
    Kicked = 14,
    Banned = 34,
    CharacterDataError = 18,
    ServerOverload = 25,
}

public enum ObjectKind : byte
{
    Self = 1,
    Player = 2,
    Npc = 3,
    SeaSelf = 4,
    SeaPlayer = 5,
    SeaNpc = 6,
    Door = 7,
    Goods = 8,
    Castle = 0x0d,
    OceanSelf = 0x68,
    OceanPlayer = 0x69,
    OceanNpc = 0x6a,
}

public enum SystemMessageKind : byte
{
    System = 0,
    Prompt = 1,
    Information = 2,
    Upgrade = 3,

    // [Gain] / [Lose] (ini\window\systeminfo.ini); the client plays SE3004 / SE3005 for them itself.
    Gain = 5,
    Lose = 6,
}

public enum DialogueEntry : byte
{
    Text = 0,
    Option = 2,
    IniText = 5,
    IniOption = 6,
}

public enum DialogueAction
{
    None = 0,
    Trade = 1,
    Bank = 2,
    Bye = 3,
    Sail = 4,
    Deposit = 5,
    Withdraw = 6,
    SkillTutor = 7,
    Quests = 8,
    Pet = 9,
    Mail = 10,
    Collect = 11,
    Warehouse = 12,
    WarehouseExpand = 13,
    Nobility = 14,
    NobilityApply = 15,
    TavernHireNeeded = 16,
    TavernHireAll = 17,
    TavernHireSome = 18,
    TavernHeal = 19,
    TavernDismiss = 20,
    BattleTaskDeus = 21,
    BattleTaskPalenque = 22,
    Guild = 200,
}

public static class ViewId
{
    public const int BagEquip = 1;
    public const int BagArticle = 2;
    public const int BagItems = 3;
    public const int Equipped = 4;
    public const int Hand = 5;
    public const int Cargo = 8;
    public const int Ship = 11;
    public const int TradeMine = 12;
    public const int TradeTheirs = 13;
    public const int ShopEquip = 15;
    public const int Booth = 16;
    public const int ShopItem = 17;
    public const int Shipyard = 18;
    public const int TradeGoods = 21;
    public const int ShopMaterial = 23;
    public const int Provision = 24;
    public const int Warehouse = 26;
    public const int KeepingBox = 30;

    public const int BagSlots = 18;
    public const int EquippedSlots = 8;
    public const int ShopSlots = 18;
    public const int CargoSlots = 6;
}

public static class ProtocolConstants
{
    public const int SelfObjectId = 1;
    public const int FirstPlayerObjectId = 1000;
    public const uint SessionValue = 0x11111111;
    public const uint NoObfuscation = 0x400;
    public const uint HandshakeEcho = 0x22222222;
    public const int CharacterSlots = 4;
    public const int CharacterSlotSize = 0x60;
    public const int CharacterListSize = 0x2BB;
    public const int CharacterSlotsOffset = 0x13B;
    public const uint DynamicKey = 42;
    public const int CharacterNameLength = 16;
    public const int FreeGamePoints = -1;
    public const uint FreePlayDeadline = 2145830400;
    public const int RunAction = 14;
    public const int WalkAction = 12;
    public const float WalkSpeed = 1.6f;
    public const string AttackActionCode = "103";
    public const int AttackActionFallback = 6;
}
