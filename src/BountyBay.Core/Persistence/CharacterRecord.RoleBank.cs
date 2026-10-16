namespace BountyBay.Core.Persistence;

public sealed class RoleBankEntry
{
    public int Key { get; set; }
    public string Name { get; set; } = "";
    public int Photo { get; set; }
    public int Amount { get; set; }
}

public sealed class RoleBankIndex
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<RoleBankEntry> Entries { get; set; } = [];
}

public sealed partial class CharacterRecord
{
    public List<RoleBankIndex> RoleBank { get; set; } = [];
    public int NextRoleBankId { get; set; } = 1;
    public int NextRoleBankKey { get; set; } = 1;
}
