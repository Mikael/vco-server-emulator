using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands.Commands;

public sealed class QuestLogCommands : ITextCommand
{
    public IEnumerable<string> Names => ["SeeRw_ListNum", "SeeMemoryNum", "SeeRw_List", "SeeMemory"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "SeeRw_ListNum" or "SeeMemoryNum":
                int id = 0;
                if (words.Length > 2 || (words.Length == 2 && !int.TryParse(words[1], out id))) return false;
                session.WatchTask(words[0], id);
                return true;
            default:
                return words.Length == 3;
        }
    }
}

public sealed class NoviceCommands : ITextCommand
{
    public IEnumerable<string> Names => ["NoviceToNext", "ShowRewardInfo"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.SystemMessage(words[0] == "NoviceToNext"
            ? "The beginner tutorial is not available on this server."
            : "There are no rewards to collect.");
        return true;
    }
}

public sealed class PetCommands : ITextCommand
{
    public IEnumerable<string> Names => ["ChangePetName", "PetCmd", "PetSkill"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 2) return false;
        switch (words[0])
        {
            case "ChangePetName":
                session.RenamePet(words[1]);
                return true;
            case "PetCmd":
                session.CommandPet(words[1]);
                return true;
            default:
                session.SystemMessage("Your pet has not learned any skills.");
                return true;
        }
    }
}
