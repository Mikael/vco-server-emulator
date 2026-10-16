namespace BountyBay.Core.Protocol;

public sealed record TableColumn(string Name, PropertyType Type);

public sealed record TableDefinition(byte Id, string Name, TableColumn[] Columns);

public static class GameTables
{
    public static readonly TableDefinition Skill = new(27, "Skill",
    [
        new("Name", PropertyType.String),
        new("Level", PropertyType.U32),
        new("Exp", PropertyType.U32),
        new("ExpMax", PropertyType.U32),
        new("Point", PropertyType.U32),
    ]);

    public static readonly TableDefinition Shortcut = new(28, "Shortcut",
    [
        new("Index", PropertyType.U16),
        new("Kind", PropertyType.U32),
        new("Name", PropertyType.String),
    ]);

    public static readonly TableDefinition Friends = new(29, "Friends",
    [
        new("Name", PropertyType.String),
        new("State", PropertyType.U32),
    ]);

    public static readonly TableDefinition Blacklist = new(30, "Blacklist", [new("Name", PropertyType.String)]);

    public static readonly TableDefinition TitleList = new(25, "TitleList", [new("Name", PropertyType.String)]);

    public static readonly TableDefinition Team = new(26, "Team",
    [
        new("Name", PropertyType.String),
        new("Photo", PropertyType.U32),
        new("Id", PropertyType.String),
    ]);

    public static readonly TableDefinition[] All = [Skill, Shortcut, Friends, Blacklist, TitleList, Team];
}
