using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class SoulCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csOpenFightSoulView", "csCloseFightSoulView", "csViewFightSoul", "csDownItem", "csFightSoulDevour", "csFightSoulDown",
        "csViewFSUpQuality", "csFightSoulUpQuality", "csViewChangeFightSoul", "csStartChangeFightSoul", "csSaveChangeFightSoul",
        "csCancleChangeFightSoul", "csCloseChangeFightSoul", "csStartCFightSoulList", "csSaveCFightSoulList", "csCancleCFightSoulList",
        "csCloseCFightSoulList", "csFreshStartMap", "csSaveStartMap", "csCancleStartMap", "csUseSoulSkill", "csJoinLuckSoul",
        "csSelectLuckSoul", "csSelectLuckHap", "csUpgradeLuckSoul", "csCloseLuckSoul",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        int slot = words.Length > 1 && int.TryParse(words[^1], out int last) ? last : -1;
        switch (words[0])
        {
            case "csOpenFightSoulView":
                session.OpenSoulView();
                return true;
            case "csCloseFightSoulView":
                session.CloseSoulView();
                return true;
            case "csViewFightSoul":
                session.SelectSoulSlot(slot);
                return true;
            case "csDownItem":
                session.PreviewSoulDown(slot);
                return true;
            case "csFightSoulDevour":
                session.UpgradeSoul(slot, words.Length > 2 && int.TryParse(words[1], out int count) ? count : 1);
                return true;
            case "csFightSoulDown":
                session.DisassembleSoul(slot);
                return true;
            case "csViewFSUpQuality":
                session.PreviewSoulAdvance(slot);
                return true;
            case "csFightSoulUpQuality":
                session.AdvanceSoul(slot);
                return true;
            case "csViewChangeFightSoul":
                session.PreviewSoulChange(slot);
                return true;
            case "csStartChangeFightSoul":
                session.StartSoulChange(slot);
                return true;
            case "csSaveChangeFightSoul":
                session.SaveSoulChange(slot);
                return true;
            case "csCancleChangeFightSoul":
                session.CancelSoulChange(slot);
                return true;
            case "csCloseChangeFightSoul":
                session.CloseSoulChange(list: false);
                return true;
            case "csCloseCFightSoulList":
                session.CloseSoulChange(list: true);
                return true;
            case "csStartCFightSoulList" or "csSaveCFightSoulList" or "csCancleCFightSoulList":
                session.BatchSoulChange(words);
                return true;
            case "csFreshStartMap":
                session.FreshSoulChart();
                return true;
            case "csSaveStartMap":
                session.SaveSoulChart(keep: true);
                return true;
            case "csCancleStartMap":
                session.SaveSoulChart(keep: false);
                return true;
            case "csUseSoulSkill":
                session.UseSoulSkill(words.Length > 1 ? string.Join(' ', words.Skip(1)) : "");
                return true;
            case "csJoinLuckSoul" or "csSelectLuckSoul" or "csSelectLuckHap" or "csUpgradeLuckSoul" or "csCloseLuckSoul":
                session.LuckSoulCommand(words);
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} star soul command: {string.Join(' ', words)}");
                return true;
        }
    }
}
