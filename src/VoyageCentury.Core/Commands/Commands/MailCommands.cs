using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands.Commands;

public sealed class MailCommands : ITextCommand
{
    public IEnumerable<string> Names => ["SendSM", "SaveSM", "RemoveSM", "RecvSM", "SendItem", "GetRecvItem"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "RecvSM":
                session.ReceiveMail();
                return true;
            case "SaveSM" when words.Length == 2 && int.TryParse(words[1], out int kept):
                session.KeepLetter(kept);
                return true;
            case "RemoveSM" when words.Length == 2 && int.TryParse(words[1], out int removed):
                session.DiscardLetter(removed);
                return true;
            case "SendSM" when words.Length >= 4 && int.TryParse(words[1], out int view) && int.TryParse(words[2], out int index):
                session.SendLetter(view, index, words[3], string.Join(' ', words.Skip(4)));
                return true;
            case "SendItem" when words.Length >= 5 && int.TryParse(words[1], out int view) && int.TryParse(words[2], out int index)
                && int.TryParse(words[3], out int amount):
                session.SendParcel(view, index, amount, words[4], string.Join(' ', words.Skip(5)));
                return true;
            case "GetRecvItem" when words.Length == 3 && int.TryParse(words[1], out int slot) && int.TryParse(words[2], out int destination):
                session.TakeParcel(slot, destination);
                return true;
            default:
                return false;
        }
    }
}
