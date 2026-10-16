using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class TotemCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csOpenTotemBoxView", "csAddTotemExp", "csAddItemPropPoint", "csUseTotemSkill", "csCleanChargePower",
        "csGrowItem", "csStartBonTotem", "csQualityItem", "csAddQualityLevel", "csQueryCleanItemList", "csStartCleanToem",
        "csShowTotemReset", "csUseTotemReset", "csSaveTotemReset", "csCancelTotemReset", "csBonTotemBoxClose",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        Log.Debug(session.Tag, $"{session.Name} imprint command: {string.Join(' ', words)}");
        switch (words[0])
        {
            case "csOpenTotemBoxView":
                session.OpenTotemBox();
                return true;
            case "csAddTotemExp":
                session.AddTotemExp();
                return true;
            case "csAddItemPropPoint":
                session.AllocateTotemPoints(words.Skip(1).Select(word => int.TryParse(word, out int value) ? value : -1).ToList());
                return true;
            case "csUseTotemSkill" when words.Length >= 2:
                session.UseTotemSkill(words[1]);
                return true;
            case "csCleanChargePower":
                session.ReleaseTotemCharge();
                return true;
            case "csGrowItem":
                session.TotemAppraiseCost();
                return true;
            case "csStartBonTotem":
                session.AppraiseTotem();
                return true;
            case "csQualityItem":
                session.TotemAdvanceNeeds();
                return true;
            case "csAddQualityLevel":
                session.AdvanceTotem();
                return true;
            case "csQueryCleanItemList":
                session.TotemDismantleReturn();
                return true;
            case "csStartCleanToem":
                session.DismantleTotem();
                return true;
            case "csShowTotemReset":
                session.ShowTotemRemodel();
                return true;
            case "csUseTotemReset":
                session.RemodelTotem();
                return true;
            case "csSaveTotemReset":
                session.SaveTotemRemodel();
                return true;
            case "csCancelTotemReset":
                session.CancelTotemRemodel();
                return true;
            case "csBonTotemBoxClose":
                session.CloseTotemOperation();
                return true;
            default:
                return false;
        }
    }
}
