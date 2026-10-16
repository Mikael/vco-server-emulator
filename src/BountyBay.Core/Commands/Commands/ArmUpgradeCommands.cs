using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ArmUpgradeCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csOpenEvolView", "csEvol", "csOpenStarView", "csMakeStar", "csSaveItemMemory", "csLoadItemMemory", "csOpenArmConsolidate",
        "csSelectUpdateType", "csNewArmUpdate", "csCloseNewArmUpdate", "csCloseNewUpdate",
        "OpenSubAssembly", "SelectAssemblyOrProbability", "EnsureSuit", "ConcelSuit",
        "csDoUpLevel", "csDoUpQuality",
        "csOpenSaveSkill", "csDoSaveSkill", "csRestoreSaveSkill", "csCleanSaveSkill",
        "csOpenSaveSoul", "csDoSaveSoul", "csRestoreSaveSoul", "csCleanSaveSoul",
        "csAddItemPropPoint", "exchange", "exclose",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csOpenEvolView" when Arguments.Numbers(words, 2, out int[] evolView):
                session.OpenEvolView(evolView[0], evolView[1]);
                return true;
            case "csEvol" when Arguments.Numbers(words, 2, out int[] evol):
                session.Evolve(evol[0], evol[1]);
                return true;
            case "csOpenStarView" when Arguments.Numbers(words, 2, out int[] starView):
                session.OpenStarView(starView[0], starView[1]);
                return true;
            case "csMakeStar" when Arguments.Numbers(words, 2, out int[] star):
                session.MakeStar(star[0], star[1]);
                return true;
            case "csSaveItemMemory" when words.Length >= 4 && int.TryParse(words[1], out int saveView)
                && int.TryParse(words[2], out int saveIndex):
                session.SaveItemMemory(saveView, saveIndex, string.Join(';', words.Skip(3)));
                return true;
            case "csLoadItemMemory" when words.Length >= 5 && int.TryParse(words[1], out int loadView)
                && int.TryParse(words[2], out int loadIndex) && int.TryParse(words[^1], out int row):
                session.LoadItemMemory(loadView, loadIndex, string.Join(';', words.Skip(3).Take(words.Length - 4)), row);
                return true;
            case "csOpenArmConsolidate":
                Log.Debug(session.Tag, $"{session.Name} opens the consolidation window");
                return true;
            case "csSelectUpdateType" when words.Length >= 3 && int.TryParse(words[1], out int type):
                session.SelectUpdateType(type, words[2], words.Length > 3 ? words[3] : "");
                return true;
            case "csNewArmUpdate" when words.Length >= 3 && int.TryParse(words[1], out int option):
                session.NewArmUpdate(option, words[2], words.Length > 3 ? words[3] : "");
                return true;
            case "csCloseNewArmUpdate" or "csCloseNewUpdate":
                session.CloseNewArmUpdate();
                return true;
            case "OpenSubAssembly" when words.Length >= 2 && int.TryParse(words[1], out int group):
                session.OpenSubAssembly(group, words.Length > 2 ? words[2] : "");
                return true;
            case "SelectAssemblyOrProbability" when words.Length >= 5 && int.TryParse(words[1], out int selectGroup)
                && int.TryParse(words[2], out int selectPiece) && int.TryParse(words[^1], out int selectChance):
                session.SelectAssembly(selectGroup, selectPiece, string.Join(' ', words[3..^1]), selectChance);
                return true;
            case "EnsureSuit" when words.Length >= 5 && int.TryParse(words[1], out int piece) && int.TryParse(words[2], out int chance)
                && int.TryParse(words[3], out int ensureGroup):
                session.EnsureSuit(piece, chance, ensureGroup, string.Join(' ', words.Skip(4)));
                return true;
            case "ConcelSuit":
                session.CloseSuitExchange();
                return true;
            case "csDoUpLevel":
                session.ArmRankUp(quality: false);
                return true;
            case "csDoUpQuality":
                session.ArmRankUp(quality: true);
                return true;
            case "csOpenSaveSkill":
                session.OpenSaveSkill();
                return true;
            case "csDoSaveSkill" when Arguments.Numbers(words, 1, out int[] saveSkill):
                session.SaveCharmSkill(saveSkill[0]);
                return true;
            case "csRestoreSaveSkill" when Arguments.Numbers(words, 1, out int[] restoreSkill):
                session.RestoreCharmSkill(restoreSkill[0]);
                return true;
            case "csCleanSaveSkill" when Arguments.Numbers(words, 1, out int[] cleanSkill):
                session.CleanCharmSkill(cleanSkill[0]);
                return true;
            case "csOpenSaveSoul" when Arguments.Numbers(words, 1, out int[] soulRow):
                session.OpenSaveSoul(soulRow[0]);
                return true;
            case "csDoSaveSoul" when Arguments.Numbers(words, 2, out int[] saveSoul):
                session.SaveCharmStar(saveSoul[0], saveSoul[1]);
                return true;
            case "csRestoreSaveSoul" when Arguments.Numbers(words, 2, out int[] restoreSoul):
                session.RestoreCharmStar(restoreSoul[0], restoreSoul[1]);
                return true;
            case "csCleanSaveSoul" when Arguments.Numbers(words, 2, out int[] cleanSoul):
                session.CleanCharmStar(cleanSoul[0], cleanSoul[1]);
                return true;
            case "csAddItemPropPoint":
                session.AddItemPropPoints(PropPointPairs(words));
                return true;
            case "exchange" when Arguments.Numbers(words, 2, out int[] exchanged):
                session.ArmExchange(exchanged[0], exchanged[1]);
                return true;
            case "exclose":
                session.CloseArmExchange();
                return true;
            default:
                return false;
        }
    }

    private static List<(int Index, int Points)> PropPointPairs(string[] words)
    {
        var pairs = new List<(int, int)>();
        for (int i = 1; i + 1 < words.Length; i += 2)
        {
            if (int.TryParse(words[i], out int index) && int.TryParse(words[i + 1], out int points)) pairs.Add((index, points));
        }
        return pairs;
    }
}
