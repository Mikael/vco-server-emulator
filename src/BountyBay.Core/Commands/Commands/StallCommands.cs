using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class StallCommands : ITextCommand
{
    public IEnumerable<string> Names => ["StartStall", "StopStall", "StallSell", "StallBuy", "CheckStallBuy", "CancelStallItem"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "StartStall" when words.Length >= 2 && int.TryParse(words[1], out int repairPrice):
                session.StartStall(repairPrice, string.Join(' ', words.Skip(2)));
                return true;
            case "StartStall":
                session.StartStall(0, string.Join(' ', words.Skip(1)));
                return true;
            case "StopStall":
                session.StopStall();
                return true;
            case "StallSell" when Arguments.Numbers(words, 4, out int[] sale):
                session.StallSell(sale[0], sale[1], sale[2], sale[3]);
                return true;
            case "StallBuy" when Arguments.Numbers(words, 3, out int[] wanted):
                session.StallBuy(wanted[0], wanted[1], wanted[2]);
                return true;
            case "CheckStallBuy" when words.Length >= 3 && int.TryParse(words[1], out int index):
                session.CheckStallBuy(index, string.Join(' ', words.Skip(2)));
                return true;
            case "CancelStallItem" when Arguments.Numbers(words, 1, out int[] cancelled):
                session.CancelStallItem(cancelled[0]);
                return true;
            default:
                return false;
        }
    }
}
