namespace BountyBay.Core.Protocol.Packets;

public static class BboSocialTables
{
    public static readonly TableDefinition TaskAttached = new(60, "Task_Attached", [new("Id", PropertyType.U32)]);

    public static readonly TableDefinition[] All = [TaskAttached];
}

public enum RankingList
{
    Guilds,
    Land,
    Sea,
    Treasure,
    Business,
    HonorLevel,
    PkTop,
    Battle,
    Refinement,
}

public static class BboSocialPackets
{
    public const int RankingTable = 59;
    public const int NoviceSteps = 12;

    public static byte[] NewTaskFlash(int questId) => UiPackets.Custom($"NewTaskFlash {questId}");

    public static byte[] NewTaskPop(int questId) => UiPackets.Custom($"NewTaskPop {questId}");

    public static byte[] TaskFinished(int questId) => UiPackets.Custom($"scTaskFinished {questId}");

    public static byte[] FlashTask(int questId) => UiPackets.Custom($"Flash Task {questId}");

    public static byte[] NoviceGuide(int step) => UiPackets.Custom($"NewTask {step}");

    public static string RankingKeyword(RankingList list) => list switch
    {
        RankingList.Guilds => "_AskConsList",
        RankingList.Land => "_AskLandList",
        RankingList.Sea => "_AskSeaList",
        RankingList.Treasure => "_AskTreasureList",
        RankingList.Business => "_AskBusinessList",
        RankingList.HonorLevel => "_AskHonorLevelList",
        RankingList.PkTop => "_AskPKTopList",
        RankingList.Battle => "_AskBattleList",
        _ => "_AskRefineMentList",
    };

    public static RankingList? RankingRequest(string command) => command switch
    {
        "AskConsList" => RankingList.Guilds,
        "AskLandList" => RankingList.Land,
        "AskSeaList" => RankingList.Sea,
        "AskTreasureList" => RankingList.Treasure,
        "AskBusinessList" => RankingList.Business,
        "AskHonorLevelList" => RankingList.HonorLevel,
        "AskPKTopList" => RankingList.PkTop,
        "AskBattleList" => RankingList.Battle,
        "AskRefineMentList" => RankingList.Refinement,
        _ => null,
    };

    public static byte[] OpenRanking(RankingList list) => UiPackets.Custom(RankingKeyword(list));

    public static byte[] ClearRanking() => GuildPackets.Clear(RankingTable);

    public static byte[] RankingRow(int id, int rank, string name, long value, string? high = null)
    {
        value = Math.Max(0, value);
        string low = (value % int.MaxValue).ToString();
        return GuildPackets.Rows(RankingTable, [[id.ToString(), rank.ToString(), name, low, high ?? (value / int.MaxValue).ToString()]]).First();
    }
}
