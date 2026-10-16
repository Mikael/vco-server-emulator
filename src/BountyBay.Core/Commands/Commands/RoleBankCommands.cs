using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class RoleBankCommands : ITextCommand
{
    public IEnumerable<string> Names =>
        ["csBankItemList", "csPutBankItem", "csRiBankItem", "csBuildBankIndex", "csDelBankIndex", "csChangeBankIndexName", "SaveItem"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csBankItemList" when words.Length == 2 && int.TryParse(words[1], out int id):
                session.RoleBankList(id);
                return true;
            case "csPutBankItem" when words.Length == 2 && int.TryParse(words[1], out int id):
                session.RoleBankPut(id);
                return true;
            case "csRiBankItem" when words.Length == 5 && int.TryParse(words[1], out int key) && int.TryParse(words[3], out int amount)
                && int.TryParse(words[4], out int id):
                session.RoleBankTake(key, words[2], amount, id);
                return true;
            case "csRiBankItem" when words.Length == 4 && int.TryParse(words[1], out int key) && int.TryParse(words[3], out int amount):
                session.RoleBankTake(key, words[2], amount, 0);
                return true;
            case "csBuildBankIndex" when words.Length >= 2:
                session.RoleBankBuild(string.Join(' ', words.Skip(1)));
                return true;
            case "csDelBankIndex" when words.Length == 2:
                session.RoleBankDelete(words[1]);
                return true;
            case "csChangeBankIndexName" when words.Length >= 3:
                session.RoleBankRename(words[1], string.Join(' ', words.Skip(2)));
                return true;
            case "SaveItem" when words.Length == 4 && int.TryParse(words[1], out int box) && int.TryParse(words[2], out int view)
                && int.TryParse(words[3], out int index):
                session.SaveItemToBox(box, view, index);
                return true;
            default:
                return false;
        }
    }
}
