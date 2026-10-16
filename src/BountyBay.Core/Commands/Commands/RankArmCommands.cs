using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class RankArmCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csOpenRankArm", "csOpenRankArmView", "csCloseRankArmView", "csOpenWakeArmView", "csCloseWakeArmView", "csDoWarkProp",
        "csDoLuckWarkProp", "csSaveWarkProp", "csSplitWarkProp", "csSplitSaveProp", "csUseWarkProp", "csGoWarkProp", "csOpenSaveBit",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        List<int> numbers = words.Skip(1).Select(word => int.TryParse(word, out int value) ? (int?)value : null)
            .Where(value => value != null).Select(value => value!.Value).ToList();
        switch (words[0])
        {
            case "csOpenRankArm":
                session.OpenRankArm();
                return true;
            case "csOpenRankArmView":
                session.OpenRankArmUpgrade();
                return true;
            case "csCloseRankArmView":
                session.CloseRankArmUpgrade();
                return true;
            case "csOpenWakeArmView":
                session.OpenTemper();
                return true;
            case "csCloseWakeArmView":
                session.CloseTemper();
                return true;
            case "csDoWarkProp":
                session.Temper(false);
                return true;
            case "csDoLuckWarkProp":
                session.Temper(true);
                return true;
            case "csSaveWarkProp" when numbers.Count >= 1:
                session.SaveTempered(numbers[0]);
                return true;
            case "csSplitWarkProp":
                session.SplitTempered(0);
                return true;
            case "csSplitSaveProp" when numbers.Count >= 1 && numbers[0] > 0:
                session.SplitTempered(numbers[0]);
                return true;
            case "csUseWarkProp" when numbers.Count >= 2:
                session.UseTempered(numbers[0], numbers[1]);
                return true;
            case "csGoWarkProp" when numbers.Count >= 1:
                session.UpgradeTempered(numbers[0]);
                return true;
            case "csOpenSaveBit" when numbers.Count >= 1:
                session.OpenTemperSave(numbers[0]);
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} sends {string.Join(' ', words)} with unexpected arguments");
                return true;
        }
    }
}
