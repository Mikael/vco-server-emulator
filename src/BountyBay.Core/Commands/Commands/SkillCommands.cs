using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class SkillCommand : ITextCommand
{
    public IEnumerable<string> Names => ["Skill"];

    public bool Execute(ClientSession session, string[] words)
        => words.Length == 3 && int.TryParse(words[1], out int kind) && session.TutorCommand(kind, words[2]);
}

public sealed class UseUniqueSkillCommand : ITextCommand
{
    public IEnumerable<string> Names => ["UseUniqueSkill"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length == 4 && int.TryParse(words[1], out int stunt) && int.TryParse(words[2], out int view) && int.TryParse(words[3], out int index))
        {
            session.UseUniqueSkill(stunt, view, index);
            return true;
        }
        if (words.Length != 2 || !int.TryParse(words[1], out int row)) return false;
        session.UseUniqueSkill(row);
        return true;
    }
}
