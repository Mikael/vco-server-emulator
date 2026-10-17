namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public string OnlineRewardDay { get; set; } = "";
    public int OnlineRewardStage { get; set; }
    public int OnlineRewardSeconds { get; set; }
    public string DailyRewardDay { get; set; } = "";
}
