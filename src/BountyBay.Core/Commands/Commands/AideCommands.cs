using System.Globalization;
using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class AideCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csRecruitAide", "csRefreshAideList", "csTrainAide", "csTransferAide", "csSelectAide", "csUseAideSuit", "csUseAideSkill",
        "csAddAideSuit", "csDelAideSuit", "csAskAideSuit", "csOpenAideDialog", "csCloseAideDialog", "csSaveTrainResult",
        "csCancelTrainResult", "csUpdateAideProp", "csRecoverArmProp", "csRecoverArmPropS", "csSaveArmProp", "csSaveArmPropS",
        "csAideArmActive", "csAideArmRebuild", "csAideArmRebuildS", "csAideRebuildView", "csAideRebuildClose",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csRecruitAide" when Leading(words, 2, out int[] recruit):
                session.RecruitAide(recruit[0], recruit[1], string.Join(' ', words.Skip(3)));
                return true;
            case "csRefreshAideList":
                session.RefreshAideRecruit();
                return true;
            case "csTrainAide" when Arguments.Numbers(words, 2, out int[] train):
                session.TrainAide(train[0], train[1]);
                return true;
            case "csSaveTrainResult" when Arguments.Numbers(words, 1, out int[] saved):
                session.SaveAideTraining(saved[0]);
                return true;
            case "csCancelTrainResult":
                if (Arguments.Numbers(words, 1, out int[] cancelled)) session.CancelAideTraining(cancelled[0]);
                return true;
            case "csSelectAide" when Arguments.Numbers(words, 2, out int[] pair):
                session.PreviewAideTransfer(pair[0], pair[1]);
                return true;
            case "csTransferAide" when Arguments.Numbers(words, 2, out int[] transfer):
                session.TransferAide(transfer[0], transfer[1]);
                return true;
            case "csUseAideSuit" when Arguments.Numbers(words, 2, out int[] use):
                session.UseAideSuit(use[0], use[1]);
                return true;
            case "csAskAideSuit" when Arguments.Numbers(words, 2, out int[] ask):
                session.AskAideSuit(ask[0], ask[1]);
                return true;
            case "csAddAideSuit" when words.Length >= 5 && Leading(words, 3, out int[] add):
                session.AddAideSuit(add[0], add[1], add[2], string.Join(' ', words.Skip(4)).Replace('\u0003', ' '));
                return true;
            case "csDelAideSuit" when Arguments.Numbers(words, 3, out int[] delete):
                session.DeleteAideSuit(delete[0], delete[1], delete[2]);
                return true;
            case "csUseAideSkill" when words.Length >= 2:
                UseSkill(session, words);
                return true;
            case "csOpenAideDialog":
                session.OpenAideDialog();
                return true;
            case "csCloseAideDialog":
                session.CloseAideDialog();
                return true;
            case "csUpdateAideProp" when Arguments.Numbers(words, 1, out int[] refresh):
                session.RefreshAide(refresh[0]);
                return true;
            case "csAideRebuildView":
                session.OpenAideArmRebuild();
                return true;
            case "csAideRebuildClose":
                session.CloseAideArmRebuild();
                return true;
            case "csAideArmRebuild" when Leading(words, 2, out int[] rebuild):
                session.RebuildAideArm(rebuild[0], Locked(words, 3));
                return true;
            case "csSaveArmProp":
                session.SaveAideArm();
                return true;
            case "csRecoverArmProp":
                session.RecoverAideArm();
                return true;
            case "csAideArmActive" when Arguments.Numbers(words, 1, out int[] active):
                session.ActivateAideArmLine(active[0]);
                return true;
            case "csRecoverArmPropS" or "csSaveArmPropS" or "csAideArmRebuildS":
                session.AideRebuildNotice(string.Join(' ', words));
                return true;
            default:
                return false;
        }
    }

    private static void UseSkill(ClientSession session, string[] words)
    {
        if (words.Length == 3 && Number(words[1]) is { } loneX && Number(words[2]) is { } loneZ)
        {
            Log.Debug(session.Tag, $"{session.Name} targets a general skill at {loneX},{loneZ} without a skill name");
            return;
        }
        float? x = words.Length >= 4 ? Number(words[^2]) : null;
        float? z = words.Length >= 4 ? Number(words[^1]) : null;
        string skill = x != null && z != null ? string.Join(' ', words[1..^2]) : string.Join(' ', words.Skip(1));
        session.UseAideSkill(skill, x, z);
    }

    // csAideArmRebuild <mode> <n> <prop> <prop> ...: the attribute indices after the count are the locked lines.
    private static HashSet<int> Locked(string[] words, int first)
        => words.Skip(first).Select(word => int.TryParse(word, out int prop) ? prop : -1).Where(prop => prop >= 0).ToHashSet();

    private static bool Leading(string[] words, int count, out int[] values)
    {
        values = new int[count];
        if (words.Length <= count) return false;
        for (int i = 0; i < count; i++)
        {
            if (!int.TryParse(words[i + 1], out values[i])) return false;
        }
        return true;
    }

    private static float? Number(string text)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;
}
