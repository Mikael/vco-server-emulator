namespace BountyBay.Core.Persistence;

public sealed class GuildTaskRecord
{
    public int Kind { get; set; }
    public string Item { get; set; } = "";
    public int Count { get; set; }
    public int Submitted { get; set; }
}

public sealed partial class CharacterRecord
{
    // The guild construction quest in progress, if any.
    public GuildTaskRecord? GuildTask { get; set; }
}
