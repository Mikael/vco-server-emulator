using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class MatchRecordCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csMatchHistryNextPage", "csMatchHistryPrevPage", "csMatchPlayersNextPage", "csMatchPlayersPrevPage", "csQueryMatchLay",
        "csSetMatchLeader", "scQueryMatchPlayerHonor", "csAskPlayerInfo",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        Log.Debug(session.Tag, $"{session.Name} match record request: {string.Join(' ', words)}");
        if (words[0] is not ("csMatchHistryNextPage" or "csMatchHistryPrevPage" or "scQueryMatchPlayerHonor")) return true;
        CharacterRecord character = session.Character;
        List<MatchRecordEntry> recent = character.MatchRecords.TakeLast(5).ToList();
        session.SystemMessage($"Battles won {character.BattleWins}, lost {character.BattleLosses}, arena rating {character.ArenaRating}.");
        foreach (MatchRecordEntry entry in recent)
            session.SystemMessage($"{entry.Date}: battle {entry.Type} {(entry.Won ? "won" : "lost")}, score {entry.Score}, kills {entry.Kills}.");
        return true;
    }
}
