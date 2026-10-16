using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class BoothSearchCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "FindSellItem", "FindBuyItem", "FindSellItemNext", "FindSellItemPrev", "FindBuyItemNext", "FindBuyItemPrev", "FilterByName",
        "csAskEquipInfo",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "FindSellItem" or "FindBuyItem":
                session.BoothSearch(words[0] == "FindBuyItem", string.Join(' ', words.Skip(1)));
                return true;
            case "FindSellItemNext" or "FindBuyItemNext":
                session.BoothSearchStep(1);
                return true;
            case "FindSellItemPrev" or "FindBuyItemPrev":
                session.BoothSearchStep(-1);
                return true;
            case "FilterByName" when words.Length == 2 && int.TryParse(words[1], out int searchClass):
                session.BoothSearchFilter(searchClass);
                return true;
            case "csAskEquipInfo" when words.Length == 3 && int.TryParse(words[2], out int row):
                session.BoothSearchDetail(row);
                return true;
            default:
                return false;
        }
    }
}
