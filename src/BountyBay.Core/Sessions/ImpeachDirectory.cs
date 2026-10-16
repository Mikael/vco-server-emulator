using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class ImpeachVote
{
    public ImpeachVote(Team team, ClientSession target, ClientSession initiator, long deadline)
    {
        Team = team;
        Target = target;
        Initiator = initiator;
        Deadline = deadline;
    }

    public Team Team { get; }
    public ClientSession Target { get; }
    public ClientSession Initiator { get; }
    public long Deadline { get; }
    public Dictionary<ClientSession, bool?> Ballots { get; } = [];
}

public static class ImpeachDirectory
{
    private static readonly List<ImpeachVote> Votes = [];

    public static void Start(ClientSession session, string name)
    {
        Tick();
        Team? team = session.Team;
        ClientSession? target = team?.Members.FirstOrDefault(member => GuildDirectory.Same(member.Name, name));
        if (team == null || target == null || target == session)
        {
            session.SystemMessage("You can only vote to kick a member of your own team.");
            return;
        }
        if (target.Scene != session.Scene)
        {
            session.SystemMessage($"{target.Name} is not on this battlefield.");
            return;
        }
        if (Votes.Any(vote => vote.Team == team))
        {
            session.SystemMessage("Your team is already voting.");
            return;
        }
        int seconds = GuildWarDirectory.Rules(session.Context).ImpeachSeconds;
        var vote = new ImpeachVote(team, target, session, Environment.TickCount64 + seconds * 1000L);
        vote.Ballots[session] = true;
        foreach (ClientSession member in team.Members.Where(member => member != session && member != target && member.InWorld && member.Scene == session.Scene))
        {
            vote.Ballots[member] = null;
            member.Send(GuildWarPackets.TeamVote(seconds, GuildWarPackets.ImpeachVoteCommand, ["Yes", "No"],
                $"{session.Name} votes to kick {target.Name} from the battle. Do you agree?"), "(kick vote)");
        }
        Votes.Add(vote);
        session.SystemMessage($"You started a vote to kick {target.Name} from the battle.");
        Log.Info(session.Tag, $"{session.Name} starts a kick vote against {target.Name}");
        Decide(vote, false);
    }

    public static void Answer(ClientSession session, int choice)
    {
        Tick();
        ImpeachVote? vote = Votes.FirstOrDefault(entry => entry.Ballots.TryGetValue(session, out bool? ballot) && ballot == null);
        if (vote == null) return;
        vote.Ballots[session] = choice == 1;
        Decide(vote, false);
    }

    public static void Forget(ClientSession session)
    {
        foreach (ImpeachVote vote in Votes.ToList())
        {
            if (vote.Target == session || vote.Initiator == session) Votes.Remove(vote);
            else vote.Ballots.Remove(session);
        }
    }

    public static void Tick()
    {
        long now = Environment.TickCount64;
        foreach (ImpeachVote vote in Votes.Where(vote => now >= vote.Deadline).ToList()) Decide(vote, true);
    }

    private static void Decide(ImpeachVote vote, bool expired)
    {
        int voters = vote.Ballots.Count;
        int yes = vote.Ballots.Values.Count(ballot => ballot == true);
        int no = vote.Ballots.Values.Count(ballot => ballot == false);
        bool passed = yes * 2 > voters;
        bool failed = no * 2 >= voters || (expired && !passed);
        if (!passed && !failed) return;
        Votes.Remove(vote);
        string result = passed ? $"{vote.Target.Name} was voted out of the team." : $"The vote to kick {vote.Target.Name} failed ({yes} of {voters}).";
        foreach (ClientSession member in vote.Ballots.Keys.Where(member => member.InWorld)) member.SystemMessage(result);
        if (!passed) return;
        if (vote.Target.InWorld) vote.Target.SystemMessage("Your team voted to kick you from the battle.");
        if (vote.Target.Team == vote.Team) TeamDirectory.Leave(vote.Target);
        if (GuildWarDirectory.BattleOf(vote.Target) != null) GuildWarDirectory.Leave(vote.Target, 1);
        Log.Info("Impeach", $"{vote.Target.Name} kicked by vote ({yes}/{voters})");
    }
}
