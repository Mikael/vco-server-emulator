using BountyBay.Core.Network;

namespace BountyBay.Core.Protocol.Packets;

public static class BattleTables
{
    public static readonly TableDefinition CopyLeftTimes = new(47, "CopyLeftTimes", Columns("Key", "Name", "Left", "Max"));

    public static readonly TableDefinition HonorLandReport = new(52, "HonorLandReport", Columns(11));

    public static readonly TableDefinition HonorSeaReport = new(53, "HonorSeaReport", Columns(12));

    public static readonly TableDefinition TowerLandList = new(69, "TowerList", Columns("Floor", "Name", "Value"));

    public static readonly TableDefinition TeamWarReport = new(71, "TeamWarReport", Columns(17));

    public static readonly TableDefinition PaleWarReport = new(81, "PaleWarReport", Columns(11));

    public static readonly TableDefinition TowerSeaList = new(92, "SeaTowerList", Columns("Floor", "Name", "Value"));

    public static readonly TableDefinition[] All = [CopyLeftTimes, HonorLandReport, HonorSeaReport, TowerLandList, TeamWarReport, PaleWarReport, TowerSeaList];

    public static TableDefinition? Report(string report) => report switch
    {
        "HonorLand" => HonorLandReport,
        "HonorSea" => HonorSeaReport,
        "TeamWar" => TeamWarReport,
        "PaleWar" => PaleWarReport,
        _ => null,
    };

    private static TableColumn[] Columns(params string[] names) => [.. names.Select(name => new TableColumn(name, PropertyType.String))];

    private static TableColumn[] Columns(int count) => [.. Enumerable.Range(0, count).Select(index => new TableColumn($"C{index}", PropertyType.String))];
}

public static class BattlePackets
{
    public static byte[] OpenBattle(IEnumerable<(int ActFlag, bool Open)> activities)
        => UiPackets.Custom("OpenBattle " + string.Join(' ', activities.Select(entry => $"{entry.ActFlag + 1} {(entry.Open ? 1 : 0)}")));

    public static byte[] BattleTimes(int type, IEnumerable<int> values) => UiPackets.Custom($"scBattleInfo {type} {string.Join(' ', values)}");

    public static byte[] AlreadyEntered(int type, int wait) => UiPackets.Custom($"scAlreadyEntryBattle {type} {wait}");

    public static byte[] NotEntered() => UiPackets.Custom("scUnAlreadyEntryBattle");

    public static byte[] BattleText(string text, bool lower = false) => UiPackets.Custom($"scBattleText 1 {(lower ? -1 : 0)} {UiPackets.Token(text)}");

    public static byte[] Winner(string report, int camp) => UiPackets.Custom(report switch
    {
        "HonorSea" => $"scHonorSeaWarWinner {camp}",
        "TeamWar" => $"csTeamWarWinner {camp}",
        "PaleWar" => $"csPaleWarWinner {camp}",
        _ => $"csHonorWarWinner {camp}",
    });

    public static byte[] ShowReport(string report) => UiPackets.Custom(report switch
    {
        "TeamWar" => "csShowTeamWarTable",
        "PaleWar" => "csShowPaleWarTable",
        _ => "csShowHonorWarTable",
    });

    public static byte[] AskLeave(int sceneKind) => UiPackets.Custom(sceneKind == BattleKinds.Pale ? "scLeavePaleWar 1" : "scLeaveHonorWar 1");

    public static byte[] ChooseCamp() => UiPackets.Custom("scChooseCampShow");

    public static byte[] StartRelive(int objectId, int seconds) => UiPackets.Custom($"StartRelive {Math.Max(1, seconds)}", objectId);

    public static byte[] StopRelive(int objectId) => UiPackets.Custom("StopRelive 0", objectId);

    public static byte[] SceneKind(int kind)
        => PropertyTable.WriteValues(new PacketWriter(ServerMsg.SceneState), new PropertyBag { ["SceneKind"] = kind }).ToArray();

    public static byte[] InQueue(bool sea, int slot, int group, int copy) => UiPackets.Custom($"scInQueueST {(sea ? 1 : 0)} {slot} {group} {copy}");

    public static byte[] CleanQueue() => UiPackets.Custom("scCleanQueue");

    public static byte[] CloseSelectRole() => UiPackets.Custom("scCloseSelectRole");

    public static byte[] RecruitPosted() => UiPackets.Custom("scSuccAddTeam");

    public static byte[] OpenTowerList() => UiPackets.Custom("OpenTttTowerList");
}

public static class BattleKinds
{
    public const int Honor = 0x1a;
    public const int Guild = 0x1c;
    public const int Team = 0x21;
    public const int Pale = 0x29;
}
