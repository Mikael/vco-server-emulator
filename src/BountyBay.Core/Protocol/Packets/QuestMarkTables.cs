namespace BountyBay.Core.Protocol.Packets;

public enum QuestMark
{
    None = 0,
    InProgress = 2,
    Available = 3,
    Complete = 4,
    NoImage = 5,
    ClickHint = 6,
}

public static class QuestMarkTables
{
    public const byte TaskStateId = 71;

    public const string AcceptedPrefix = "125000";

    public static readonly TableDefinition TaskState = new(TaskStateId, "Task_State",
    [
        new("Key", PropertyType.String),
        new("Quest", PropertyType.U32),
        new("State", PropertyType.U32),
    ]);

    public static readonly TableDefinition[] All = [TaskState];

    public static string Key(int objectId, int serial = 0) => $"{objectId}.{serial}";

    public static int AcceptedState(int group)
        => group is > 0 and <= 9999 ? int.Parse(AcceptedPrefix + group) : 1;

    public static byte[] Clear() => TablePackets.Clear(TaskState);

    public static byte[] Rows(IReadOnlyList<object[]> rows) => TablePackets.Rows(TaskState, rows);
}
