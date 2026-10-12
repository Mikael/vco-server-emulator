namespace VoyageCentury.Core.Protocol;

public static class ProgressTables
{
    public static readonly TableDefinition SkillAdv = new(17, "SkillAdv",
    [
        new("Name", PropertyType.String),
        new("Level", PropertyType.U32),
    ]);

    public static readonly TableDefinition UniqueSkill = new(18, "UniqueSkill",
    [
        new("Name", PropertyType.String),
        new("Level", PropertyType.U32),
        new("Value", PropertyType.U32),
        new("Skill", PropertyType.String),
    ]);

    public static readonly TableDefinition TaskAccepted = new(19, "Task_Accepted",
    [
        new("Id", PropertyType.U32),
        new("State", PropertyType.U32),
        new("Date", PropertyType.String),
    ]);

    public static readonly TableDefinition TaskCompleted = new(20, "Task_Completed",
    [
        new("Id", PropertyType.U32),
        new("Date", PropertyType.String),
    ]);

    public static readonly TableDefinition TaskRecord = new(21, "Task_Record",
    [
        new("Id", PropertyType.U32),
        new("Type", PropertyType.U32),
        new("Reserved1", PropertyType.U32),
        new("Reserved2", PropertyType.U32),
        new("Total", PropertyType.U32),
        new("Amount", PropertyType.U32),
        new("Reserved3", PropertyType.U32),
        new("Target", PropertyType.String),
    ]);

    public static readonly TableDefinition RwList = new(22, "RW_LIST", SevenNumbers());

    public static readonly TableDefinition Memory = new(23, "Memory", SevenNumbers());

    public static readonly TableDefinition TaskFinding = new(24, "task_finding", [new("Name", PropertyType.String)]);

    public static readonly TableDefinition[] All =
        [SkillAdv, UniqueSkill, TaskAccepted, TaskCompleted, TaskRecord, RwList, Memory, TaskFinding];

    private static TableColumn[] SevenNumbers() =>
    [
        new("Id", PropertyType.U32),
        new("Text", PropertyType.U32),
        new("State", PropertyType.U32),
        new("Year", PropertyType.U32),
        new("Month", PropertyType.U32),
        new("Day", PropertyType.U32),
        new("Flag", PropertyType.U32),
    ];
}
