using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class BboCharacterCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "CMD_SHOWNOUSE", "Suit", "AskSuit", "SuitOne", "_SuitOne", "SuitOneRank", "_SuitOneRank", "SuitOneTotem", "_SuitOneTotem",
        "SelectWork", "WorkUSKill",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "CMD_SHOWNOUSE" when Number(words, 1, out int shown):
                session.ChangeLookMode(shown);
                return true;
            case "Suit" when Number(words, 1, out int worn):
                session.WearOutfit(worn);
                return true;
            case "AskSuit" when Number(words, 1, out int asked):
                session.SendOutfit(asked);
                return true;
            case "SuitOne" when words.Length >= 4 && Number(words, 1, out int suit) && Number(words, 2, out int position):
                session.SetOutfitPiece(suit, position, words[3]);
                return true;
            case "_SuitOne" when Number(words, 1, out int suit) && Number(words, 2, out int position):
                session.ClearOutfitPiece(suit, position);
                return true;
            case "SuitOneRank" or "_SuitOneRank" or "SuitOneTotem" or "_SuitOneTotem":
                session.SystemMessage("Rank equipment and totems cannot be put into outfits yet.", SystemMessageKind.Prompt);
                Log.Debug(session.Tag, $"{session.Name} outfit command: {string.Join(' ', words)}");
                return true;
            case "SelectWork" when Number(words, 1, out int work):
                session.SelectWork(work);
                return true;
            case "WorkUSKill" when words.Length >= 2:
                session.LearnWorkSkill(words[1], Number(words, 2, out int level) ? level : 0);
                return true;
            default:
                return false;
        }
    }

    private static bool Number(string[] words, int index, out int value)
    {
        value = 0;
        return words.Length > index && int.TryParse(words[index], out value);
    }
}
