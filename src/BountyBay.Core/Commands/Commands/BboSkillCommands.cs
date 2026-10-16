using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class BboSkillCommand : ITextCommand
{
    private const int ResetStunts = 6;

    public IEnumerable<string> Names => ["Skill"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 3 || !int.TryParse(words[1], out int kind)) return false;
        if (kind != ResetStunts) return session.TutorCommand(kind, words[2]);
        session.ResetBboStunts(words[2]);
        return true;
    }
}

public sealed class BboUseUniqueSkillCommand : ITextCommand
{
    public IEnumerable<string> Names => ["UseUniqueSkill"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length == 4 && int.TryParse(words[1], out int stunt) && int.TryParse(words[2], out int view) && int.TryParse(words[3], out int index))
        {
            session.UseBboUniqueSkill(stunt, view, index);
            return true;
        }
        if (words.Length != 2 || !int.TryParse(words[1], out int row)) return false;
        session.UseBboUniqueSkill(row);
        return true;
    }
}
