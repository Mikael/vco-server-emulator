using BountyBay.Core.Protocol.Packets;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ImpeachCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csImpeach", "csImpeachPlayer", GuildWarPackets.ImpeachVoteCommand];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csImpeach" or "csImpeachPlayer" when words.Length >= 2:
                ImpeachDirectory.Start(session, BboArguments.Rest(words, 1).Replace('\u0003', ' '));
                return true;
            case GuildWarPackets.ImpeachVoteCommand when BboArguments.Last(words, out int choice):
                ImpeachDirectory.Answer(session, choice);
                return true;
            default:
                return false;
        }
    }
}
