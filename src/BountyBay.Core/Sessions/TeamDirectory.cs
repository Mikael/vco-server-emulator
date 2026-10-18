using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class Team
{
    public Team(int id, ClientSession leader)
    {
        Id = id;
        Members.Add(leader);
    }

    public int Id { get; }

    public List<ClientSession> Members { get; } = [];

    public int OceanMode { get; set; }

    // AI companions from the client's VirtualTeam.ini (team window "virtual member" button).
    public List<(string Name, int Photo)> Virtual { get; } = [];

    public int Size => Members.Count + Virtual.Count;

    public ClientSession Leader => Members[0];
}

public static class TeamDirectory
{
    public const int MaxMembers = 5;
    public const int InviteSeconds = 60;
    public const int OceanModes = 2;
    public const string RequestKind = "Team";
    public const string AcceptCommand = RequestKind + " 1";
    public const string DeclineCommand = RequestKind + " 0";

    private static int _nextId;

    public static bool UseRequestIcon { get; set; }

    public static void Invite(ClientSession inviter, string name)
    {
        ClientSession? target = inviter.Context.World.FindPlayer(name);
        if (target == null || !target.InWorld)
        {
            inviter.SystemMessage($"{name} is not online.");
            return;
        }
        if (target == inviter)
        {
            inviter.SystemMessage("You cannot invite yourself.");
            return;
        }
        Team? team = inviter.Team;
        if (team != null && team.Leader != inviter)
        {
            inviter.SystemMessage("Only the team leader can invite players.");
            return;
        }
        if (team != null && team.Size >= MaxMembers)
        {
            inviter.SystemMessage("The team is full.");
            return;
        }
        if (target.Team != null)
        {
            inviter.SystemMessage($"{target.Name} is already in a team.");
            return;
        }
        if (!target.TeamEnabled)
        {
            inviter.SystemMessage($"{target.Name} refuses team invitations.");
            return;
        }
        ClientSession? pending = PendingInviter(target);
        if (pending != null && pending != inviter)
        {
            inviter.SystemMessage($"{target.Name} is answering another invitation.");
            return;
        }

        target.TeamInviter = inviter;
        target.TeamInvitedAt = Environment.TickCount64;
        target.BboSendTeamInvite(inviter);
        inviter.SystemMessage($"You invited {target.Name} to your team.");
        Log.Info(inviter.Tag, $"{inviter.Name} invites {target.Name} to a team");
    }

    public static void Answer(ClientSession invitee, bool accept)
    {
        ClientSession? inviter = PendingInviter(invitee);
        invitee.TeamInviter = null;
        if (inviter == null)
        {
            if (accept) invitee.SystemMessage("The invitation is no longer valid.");
            return;
        }
        if (!accept)
        {
            inviter.SystemMessage($"{invitee.Name} declined your invitation.");
            return;
        }
        if (invitee.Team != null)
        {
            invitee.SystemMessage("You are already in a team.");
            return;
        }
        Team? team = inviter.Team;
        if (team != null && (team.Leader != inviter || team.Size >= MaxMembers))
        {
            invitee.SystemMessage(team.Leader != inviter ? "The invitation is no longer valid." : "The team is full.");
            return;
        }
        if (team == null)
        {
            team = new Team(Interlocked.Increment(ref _nextId), inviter);
            inviter.Team = team;
            Join(inviter, team);
        }
        team.Members.Add(invitee);
        invitee.Team = team;
        Join(invitee, team);
        Publish(team);
        Tell(team, $"{invitee.Name} joined the team.");
        Log.Info(invitee.Tag, $"{invitee.Name} joins team {team.Id} of {team.Leader.Name}");
    }

    public static void Leave(ClientSession member)
    {
        Team? team = member.Team;
        if (team == null)
        {
            member.SystemMessage("You are not in a team.");
            return;
        }
        Remove(team, member, connected: true);
        member.SystemMessage("You left the team.");
        Tell(team, $"{member.Name} left the team.");
    }

    public static void Kick(ClientSession leader, string name)
    {
        Team? team = leader.Team;
        if (team == null)
        {
            leader.SystemMessage("You are not in a team.");
            return;
        }
        if (leader.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            Leave(leader);
            return;
        }
        if (team.Leader != leader)
        {
            leader.SystemMessage("Only the team leader can remove members.");
            return;
        }
        int companion = team.Virtual.FindIndex(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (companion >= 0)
        {
            team.Virtual.RemoveAt(companion);
            Remove(team, null, connected: true);
            Tell(team, $"{name} left the team.");
            return;
        }
        ClientSession? member = team.Members.FirstOrDefault(other => other.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (member == null)
        {
            leader.SystemMessage($"{name} is not in your team.");
            return;
        }
        Remove(team, member, connected: true);
        member.SystemMessage("You were removed from the team.");
        Tell(team, $"{member.Name} was removed from the team.");
    }

    public static void AddVirtual(ClientSession leader, string name, int photo)
    {
        Team? team = leader.Team;
        if (team != null && team.Leader != leader)
        {
            leader.SystemMessage("Only the team leader can add members.");
            return;
        }
        if (team != null && team.Size >= MaxMembers)
        {
            leader.SystemMessage("The team is full.");
            return;
        }
        if (team == null)
        {
            team = new Team(Interlocked.Increment(ref _nextId), leader);
            leader.Team = team;
            Join(leader, team);
        }
        if (team.Virtual.Any(entry => entry.Name == name)) return;
        team.Virtual.Add((name, photo));
        Publish(team);
        Tell(team, $"{name} joined the team.");
        Log.Info(leader.Tag, $"{leader.Name} adds virtual teammate {name}");
    }

    public static void SetOceanMode(ClientSession leader, int mode)
    {
        Team? team = leader.Team;
        if (team == null || mode < 0 || mode >= OceanModes) return;
        if (team.Leader != leader)
        {
            leader.SystemMessage("Only the team leader can change the sailing mode.");
            leader.SetProperty("TeamOceanMode", team.OceanMode);
            return;
        }
        team.OceanMode = mode;
        foreach (ClientSession member in team.Members) member.SetProperty("TeamOceanMode", mode);
    }

    public static void Chat(ClientSession from, string text)
    {
        Team? team = from.Team;
        if (team == null)
        {
            from.SystemMessage("You are not in a team.");
            return;
        }
        foreach (ClientSession member in team.Members) member.SendChannelChat(ChatChannel.Team, from, text);
        Log.Info(from.Tag, $"{from.Name} to team {team.Id}: {text}");
    }

    public static void Refresh(ClientSession member)
    {
        if (member.Team != null) Publish(member.Team);
    }

    public static void Disconnect(ClientSession session)
    {
        foreach (ClientSession other in session.Context.World.Sessions)
        {
            if (other.TeamInviter == session) other.TeamInviter = null;
        }
        session.TeamInviter = null;
        Team? team = session.Team;
        if (team == null) return;
        Remove(team, session, connected: false);
        Tell(team, $"{session.Name} left the team.");
    }

    private static ClientSession? PendingInviter(ClientSession invitee)
    {
        ClientSession? inviter = invitee.TeamInviter;
        if (inviter == null || !inviter.InWorld) return null;
        return Environment.TickCount64 - invitee.TeamInvitedAt > InviteSeconds * 1000L ? null : inviter;
    }

    private static void Join(ClientSession member, Team team)
    {
        member.SetProperty("TeamNum", team.Id, visibleToOthers: true);
        member.SetProperty("TeamOceanMode", team.OceanMode);
    }

    private static void Part(ClientSession member)
    {
        member.Team = null;
        member.SetProperty("TeamNum", 0, visibleToOthers: true);
        member.SetProperty("TeamOceanMode", 0);
        member.Send(TablePackets.Clear(GameTables.Team), "(clear Team)");
    }

    private static void Remove(Team team, ClientSession? member, bool connected)
    {
        ClientSession leader = team.Leader;
        if (member != null)
        {
            team.Members.Remove(member);
            if (connected) Part(member);
            else member.Team = null;
        }
        if (team.Members.Count == 0) team.Virtual.Clear();

        if (team.Size <= 1)
        {
            foreach (ClientSession last in team.Members.ToList())
            {
                Part(last);
                last.SystemMessage("The team has been disbanded.");
            }
            team.Members.Clear();
            team.Virtual.Clear();
            return;
        }
        Publish(team);
        if (team.Leader != leader) Tell(team, $"{team.Leader.Name} is now the team leader.");
    }

    private static void Publish(Team team)
    {
        if (GameTables.Team.Columns.Length < 3) return;
        List<object[]> rows = team.Members
            .Select(member => new object[] { member.Name, member.Portrait, $"{member.ObjectId}.0" })
            .Concat(team.Virtual.Select(entry => new object[] { entry.Name, entry.Photo, "0.0" }))
            .ToList();
        foreach (ClientSession member in team.Members)
        {
            member.Send(TablePackets.Clear(GameTables.Team), "(clear Team)");
            member.Send(TablePackets.Rows(GameTables.Team, rows), "(Team)");
        }
    }

    private static void Tell(Team team, string text)
    {
        foreach (ClientSession member in team.Members) member.SystemMessage(text);
    }
}
