namespace VoyageCentury.Core.Protocol;

public sealed record TableColumn(string Name, PropertyType Type);

public sealed record TableDefinition(byte Id, string Name, TableColumn[] Columns);

public static class GameTables
{
    public static readonly TableDefinition Skill = new(1, "Skill",
    [
        new("Name", PropertyType.String),
        new("Level", PropertyType.U32),
        new("Exp", PropertyType.String),
        new("ExpMax", PropertyType.String),
    ]);

    public static readonly TableDefinition Shortcut = new(2, "Shortcut",
    [
        new("Index", PropertyType.U16),
        new("Kind", PropertyType.U32),
        new("Name", PropertyType.String),
    ]);

    public static readonly TableDefinition[] All = [Skill, Shortcut];
}
