namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public int TemperDay { get; set; }
    public int TemperToday { get; set; }
    public DateTime? MutedUntil { get; set; }
    public bool CannonsRestored { get; set; }
    public int StallStyle { get; set; } = 1;
    public int HurricaneDay { get; set; }
    public int HurricaneTrips { get; set; }
    public int HurricaneExtraTrips { get; set; }
    public int HurricaneReturnScene { get; set; }
    public float[]? HurricaneReturnPosition { get; set; }
}
