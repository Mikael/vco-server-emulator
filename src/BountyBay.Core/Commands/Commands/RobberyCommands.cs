using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class RobberyCommands : ITextCommand
{
    public IEnumerable<string> Names => ["ROBBT_ID", "OceanRob"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "ROBBT_ID" when words.Length >= 2 && int.TryParse(words[1], out int answer):
                session.AnswerRobbery(answer);
                return true;
            case "OceanRob":
                session.OceanRob(words.Length >= 2 && int.TryParse(words[1], out int target) ? target : session.ExchangeTargetId);
                return true;
            default:
                return false;
        }
    }
}
