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
    public GuildTaskRecord? GuildTask { get; set; }
}
