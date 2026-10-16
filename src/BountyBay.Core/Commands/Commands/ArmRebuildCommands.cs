using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ArmRebuildCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csArmRebuildStart", "csArmRebuildSelect", "csArmRebuildLock", "csArmRebuildClose", "csOpenArmRebuildHedge",
        "csArmRebuildCopy", "csArmRebuildHedge", "csRemoveArmRebuildProp", "csCloseArmRebuildHedge", "transprop", "transclose",
        "csTakeOffThing",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        List<int> numbers = words.Skip(1).Select(word => int.TryParse(word, out int value) ? (int?)value : null)
            .Where(value => value != null).Select(value => value!.Value).ToList();
        switch (words[0])
        {
            case "csArmRebuildStart":
                session.StartRecast();
                return true;
            case "csArmRebuildSelect" when numbers.Count >= 1:
                session.SelectRecastLevel(numbers[0]);
                return true;
            case "csArmRebuildLock":
                session.LockRecast(numbers);
                return true;
            case "csArmRebuildClose":
                session.CloseArmRebuild();
                return true;
            case "csOpenArmRebuildHedge":
                session.OpenArmRebuildRecord();
                return true;
            case "csArmRebuildCopy" when numbers.Count >= 1:
                session.CopyRecastRecord(numbers[0]);
                return true;
            case "csArmRebuildHedge" when numbers.Count >= 1:
                session.ApplyRecastRecord(numbers[0]);
                return true;
            case "csRemoveArmRebuildProp" when numbers.Count >= 1:
                session.RemoveRecastRecord(numbers[0]);
                return true;
            case "csCloseArmRebuildHedge":
                session.CloseArmRebuildRecord();
                return true;
            case "transprop":
                session.TransferAttributes();
                return true;
            case "transclose":
                session.CloseArmTransfer();
                return true;
            case "csTakeOffThing" when numbers.Count >= 2:
                session.ArmTakeOff(numbers[0], numbers[1]);
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} sends {string.Join(' ', words)} with unexpected arguments");
                return true;
        }
    }
}
