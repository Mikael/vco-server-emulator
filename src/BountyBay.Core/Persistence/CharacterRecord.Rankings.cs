namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public int TemperDay { get; set; }
    public int TemperToday { get; set; }
    public DateTime? MutedUntil { get; set; }
    public bool CannonsRestored { get; set; }
}
