using System.Text.Json;
using System.Text.Json.Serialization;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class GuildDuty
{
    public const int Member = 0;
    public const int Officer = 1;
    public const int Director = 2;
    public const int ViceLeader = 3;
    public const int Leader = 4;

    private static readonly string[] Names = ["member", "executive officer", "director", "vice leader", "leader"];

    public static string Name(int duty) => Names[Math.Clamp(duty, Member, Leader)];
}

public static class GuildRelation
{
    public const int War = 1;
    public const int Alliance = 2;
    public const int HostileRequestSent = 3;
    public const int HostileRequestReceived = 4;
    public const int Hostile = 5;
    public const int RequestSent = 6;
    public const int RequestReceived = 7;
    public const int Normal = 8;
}

public static class GuildRelationCommand
{
    public const int Ally = 0;
    public const int Cancel = 1;
    public const int Hostility = 2;
    public const int War = 3;
}

public sealed class GuildMember
{
    public string Name { get; set; } = "";
    public int Duty { get; set; }
    public string Department { get; set; } = "";
    public int Contribution { get; set; }
    public int Photo { get; set; }
    public string Nationality { get; set; } = "";
    public string Title { get; set; } = "";
    public int SkillLevel { get; set; }
}

public sealed class GuildDepartmentRequest
{
    public string Name { get; set; } = "";
    public string Applicant { get; set; } = "";
    public string Explanation { get; set; } = "";
}

public sealed class Guild
{
    public string Name { get; set; } = "";
    public int Flag { get; set; }
    public string Tenet { get; set; } = "";
    public int Fund { get; set; }
    public int Level { get; set; } = 1;
    public int Commerce { get; set; }
    public int Industry { get; set; }
    public string Headquarters { get; set; } = "";
    public bool ApplyClosed { get; set; }
    public List<GuildMember> Members { get; set; } = [];
    public List<GuildMember> Applicants { get; set; } = [];
    public List<string> Departments { get; set; } = [];
    public List<GuildDepartmentRequest> DepartmentRequests { get; set; } = [];
    public Dictionary<string, int> Relations { get; set; } = [];

    [JsonIgnore]
    public int MemberLimit => GuildDirectory.MembersPerLevel * Math.Max(1, Level);

    public GuildMember? Member(string name) => Members.FirstOrDefault(member => GuildDirectory.Same(member.Name, name));

    public GuildMember? Applicant(string name) => Applicants.FirstOrDefault(member => GuildDirectory.Same(member.Name, name));

    public string? Department(string name) => Departments.FirstOrDefault(department => GuildDirectory.Same(department, name));

    public int Relation(Guild other) => Relations.TryGetValue(other.Name, out int state) ? state : GuildRelation.Normal;
}

public sealed class GuildStore
{
    public List<Guild> Guilds { get; set; } = [];
}

public static class GuildDirectory
{
    public static int CreationFee { get; private set; } = 100000;

    public static int MembersPerLevel { get; private set; } = 20;

    public const int ApplicantLimit = 30;
    public const int DepartmentLimit = 5;
    public const int NameLength = 16;
    public const int TenetLength = 400;
    public const int LastFlag = 31;
    public const int FounderContribution = 100;

    private static List<Guild>? _guilds;
    private static string _path = "";

    public static void Configure(ServerContext context)
    {
        CreationFee = context.Data.Rules.Guild.CreationFee;
        MembersPerLevel = Math.Max(1, context.Data.Rules.Guild.MemberLimit);
    }

    public static string FoundingObstacle(ClientSession session)
    {
        Data.GuildRules rules = session.Context.Data.Rules.Guild;
        Configure(session.Context);
        if (session.Character.Skills.All(skill => skill.Level < rules.FounderSkillLevel)
            && session.Character.LearnedSkills.Values.All(level => level < rules.FounderSkillLevel))
            return $"To found a guild one of your skills must reach Level {rules.FounderSkillLevel}.";
        if (session.Money < rules.CreationFee)
            return $"Founding a guild costs {rules.CreationFee} silver coins. You carry only {session.Money}.";
        if (!rules.RequireSupporters || rules.Supporters <= 0) return "";
        List<ClientSession> supporters = session.Team == null
            ? []
            : session.Team.Members.Where(member => member != session && Of(session.Context, member.Name) == null).ToList();
        if (session.Team == null || session.Team.Leader != session || supporters.Count < rules.Supporters)
            return $"Form a team with {rules.Supporters} supporters who belong to no guild, and come to me as its leader.";
        return "";
    }

    public static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    public static Guild? Find(ServerContext context, string name)
        => Guilds(context).FirstOrDefault(guild => Same(guild.Name, name));

    public static Guild? Of(ServerContext context, string player)
        => Guilds(context).FirstOrDefault(guild => guild.Member(player) != null);

    public static void Enter(ClientSession session)
    {
        ServerContext context = session.Context;
        Configure(context);
        Guild? guild = Of(context, session.Name);
        GuildMember? self = guild?.Member(session.Name);
        session.ApplyGuild(guild, self);
        if (guild == null || self == null) return;
        if (Snapshot(self, session)) Save();
        SendAll(session, guild);
        byte[][] row = GuildPackets.Rows(GuildList.Members, [MemberRow(context, self, null)]).ToArray();
        foreach (ClientSession other in Online(context, guild, session)) Send(other, row, $"(guild member {self.Name})");
        if (self.Duty >= GuildDuty.ViceLeader)
        {
            foreach (GuildDepartmentRequest request in guild.DepartmentRequests) ShowDepartmentRequest(session, request);
        }
    }

    public static void Refresh(ClientSession session)
    {
        if (!session.GuildEntered) return;
        Guild? guild = Of(session.Context, session.Name);
        if (guild != null) session.Send(Detail(guild), "(guild detail)");
    }

    public static void Leave(ClientSession session)
    {
        ServerContext context = session.Context;
        Guild? guild = Of(context, session.Name);
        GuildMember? self = guild?.Member(session.Name);
        if (guild == null || self == null) return;
        byte[][] row = GuildPackets.Rows(GuildList.Members, [MemberRow(context, self, session)]).ToArray();
        foreach (ClientSession other in Online(context, guild, session)) Send(other, row, $"(guild member {self.Name})");
    }

    public static void OfferCreation(ClientSession session, int npc)
    {
        Guild? guild = Of(session.Context, session.Name);
        if (guild != null)
        {
            session.GuildSay(npc, $"You already belong to the guild {guild.Name}.");
            return;
        }
        string obstacle = FoundingObstacle(session);
        if (obstacle.Length > 0)
        {
            session.GuildSay(npc, obstacle);
            return;
        }
        session.GuildFounding = true;
        session.Send(GuildPackets.ShowCreate(), "(guild creation window)");
    }

    public static void Create(ClientSession session, string name, int flag, string tenet)
    {
        ServerContext context = session.Context;
        List<Guild> guilds = Guilds(context);
        if (!session.GuildFounding)
        {
            session.SystemMessage("Guilds are founded at a guild official.");
            return;
        }
        if (Of(context, session.Name) != null)
        {
            session.SystemMessage("You already belong to a guild.");
            return;
        }
        if (!ValidName(name))
        {
            session.SystemMessage($"A guild name has 1 to {NameLength} characters and no spaces.");
            return;
        }
        if (Find(context, name) != null)
        {
            session.SystemMessage($"A guild named {name} already exists.");
            return;
        }
        string obstacle = FoundingObstacle(session);
        if (obstacle.Length > 0)
        {
            session.SystemMessage(obstacle);
            return;
        }

        session.GuildFounding = false;
        foreach (Guild applied in WithdrawApplications(guilds, session.Name)) PublishApplicants(context, applied);
        session.SetMoney(session.Money - CreationFee);
        var leader = new GuildMember { Name = session.Name, Duty = GuildDuty.Leader, Contribution = FounderContribution };
        Snapshot(leader, session);
        var guild = new Guild
        {
            Name = name,
            Flag = Math.Clamp(flag, 0, LastFlag),
            Tenet = Text(tenet, TenetLength),
            Fund = CreationFee,
            Members = [leader],
        };
        guilds.Add(guild);
        Save();
        session.ApplyGuild(guild, leader);
        session.Save();
        SendAll(session, guild);
        PublishRelations(context, guild);
        session.SystemMessage($"You founded the guild {guild.Name} for {CreationFee} silver coins.");
        Log.Info(session.Tag, $"{session.Name} founds the guild {guild.Name}");
    }

    public static void ShowAll(ClientSession session)
    {
        List<Guild> guilds = Guilds(session.Context);
        session.Send(GuildPackets.ShowAll(), "(guild list window)");
        Send(session, GuildPackets.Rows(GuildList.Guilds, guilds.Select(GuildRow)), "(guild list)");
        if (guilds.Count == 0) session.SystemMessage("No guild has been founded yet.");
    }

    public static void Apply(ClientSession session, string name)
    {
        ServerContext context = session.Context;
        List<Guild> guilds = Guilds(context);
        Guild? own = Of(context, session.Name);
        if (own != null)
        {
            session.SystemMessage($"You already belong to the guild {own.Name}.");
            return;
        }
        Guild? guild = Find(context, name);
        if (guild == null)
        {
            session.SystemMessage($"There is no guild named {name}.");
            return;
        }
        if (guild.ApplyClosed)
        {
            session.SystemMessage($"{guild.Name} does not accept applications at the moment.");
            return;
        }
        if (guild.Applicant(session.Name) != null)
        {
            session.SystemMessage($"You have already applied to {guild.Name}.");
            return;
        }
        if (guild.Members.Count >= guild.MemberLimit || guild.Applicants.Count >= ApplicantLimit)
        {
            session.SystemMessage($"{guild.Name} cannot take more applications.");
            return;
        }

        foreach (Guild applied in WithdrawApplications(guilds, session.Name)) PublishApplicants(context, applied);
        var applicant = new GuildMember { Name = session.Name };
        Snapshot(applicant, session);
        guild.Applicants.Add(applicant);
        Save();
        PublishApplicants(context, guild);
        foreach (ClientSession recruiter in Online(context, guild).Where(other => Duty(guild, other) >= GuildDuty.Officer))
            recruiter.SystemMessage($"{session.Name} applies to join the guild.");
        session.SystemMessage($"Your application was sent to {guild.Name}.");
        Log.Info(session.Tag, $"{session.Name} applies to the guild {guild.Name}");
    }

    public static void ProcessApplication(ClientSession session, string name, bool accept)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.Officer, "Only guild officers decide on applications.", out Guild guild, out _)) return;
        GuildMember? applicant = guild.Applicant(name);
        if (applicant == null)
        {
            session.SystemMessage($"{name} has no open application.");
            SendApplicants(session, guild);
            return;
        }
        ClientSession? target = OnlinePlayer(context, applicant.Name);
        if (!accept)
        {
            guild.Applicants.Remove(applicant);
            Save();
            PublishApplicants(context, guild);
            target?.SystemMessage($"{guild.Name} declined your application.");
            session.SystemMessage($"You declined the application of {applicant.Name}.");
            return;
        }
        if (guild.Members.Count >= guild.MemberLimit)
        {
            session.SystemMessage("The guild is full.");
            return;
        }
        if (Of(context, applicant.Name) != null)
        {
            guild.Applicants.Remove(applicant);
            Save();
            PublishApplicants(context, guild);
            session.SystemMessage($"{applicant.Name} already belongs to a guild.");
            return;
        }

        foreach (Guild applied in WithdrawApplications(Guilds(context), applicant.Name).Where(applied => applied != guild))
            PublishApplicants(context, applied);
        applicant.Duty = GuildDuty.Member;
        applicant.Department = "";
        applicant.Contribution = 0;
        if (target != null) Snapshot(applicant, target);
        guild.Members.Add(applicant);
        Save();
        if (target != null)
        {
            target.ApplyGuild(guild, applicant);
            target.Save();
            SendAll(target, guild);
        }
        PublishMember(context, guild, applicant, target);
        PublishDetail(context, guild, target);
        PublishApplicants(context, guild);
        Tell(context, guild, $"{applicant.Name} joined the guild.");
        Log.Info(session.Tag, $"{session.Name} accepts {applicant.Name} into the guild {guild.Name}");
    }

    public static void Expel(ClientSession session, string name)
    {
        if (!Require(session, GuildDuty.ViceLeader, "Only the leader and vice leaders dismiss members.", out Guild guild, out GuildMember self)) return;
        GuildMember? target = guild.Member(name);
        if (target == null)
        {
            session.SystemMessage($"{name} is not a member of your guild.");
            return;
        }
        if (target == self)
        {
            session.SystemMessage("Use Exit Guild to leave the guild yourself.");
            return;
        }
        if (target.Duty >= self.Duty)
        {
            session.SystemMessage($"You cannot dismiss a {GuildDuty.Name(target.Duty)}.");
            return;
        }
        Remove(session.Context, guild, target, $"You were dismissed from the guild {guild.Name}.");
        Tell(session.Context, guild, $"{target.Name} was dismissed from the guild.");
        Log.Info(session.Tag, $"{session.Name} dismisses {target.Name} from the guild {guild.Name}");
    }

    public static void Quit(ClientSession session)
    {
        if (!Require(session, GuildDuty.Member, "", out Guild guild, out GuildMember self)) return;
        if (self.Duty == GuildDuty.Leader)
        {
            if (guild.Members.Count > 1) session.SystemMessage("The leader cannot leave while the guild has other members. Disband the guild instead.");
            else Dismiss(session);
            return;
        }
        Remove(session.Context, guild, self, $"You left the guild {guild.Name}.");
        Tell(session.Context, guild, $"{self.Name} left the guild.");
        Log.Info(session.Tag, $"{session.Name} leaves the guild {guild.Name}");
    }

    public static void Dismiss(ClientSession session)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.Leader, "Only the leader can disband the guild.", out Guild guild, out _)) return;
        List<Guild> guilds = Guilds(context);
        List<ClientSession> members = Online(context, guild).ToList();
        guilds.Remove(guild);
        foreach (Guild other in guilds) other.Relations.Remove(guild.Name);
        Save();
        foreach (ClientSession member in members)
        {
            Part(member);
            member.SystemMessage($"The guild {guild.Name} has been disbanded.");
        }
        foreach (Guild other in guilds)
        {
            byte[] gone = GuildPackets.Delete(GuildList.Relations, guild.Name);
            foreach (ClientSession member in Online(context, other)) member.Send(gone, "(guild relation removed)");
        }
        Log.Info(session.Tag, $"{session.Name} disbands the guild {guild.Name}");
        GuildBuildDirectory.Dissolved(context, guild.Name);
    }

    public static void SetTenet(ClientSession session, string tenet)
    {
        if (!Require(session, GuildDuty.ViceLeader, "Only the leader and vice leaders edit the tenet.", out Guild guild, out _)) return;
        guild.Tenet = Text(tenet, TenetLength);
        Save();
        PublishDetail(session.Context, guild);
        Tell(session.Context, guild, $"{session.Name} changed the guild tenet.");
    }

    public static void SetApplyClosed(ClientSession session, bool closed)
    {
        if (!Require(session, GuildDuty.ViceLeader, "Only the leader and vice leaders open or close applications.", out Guild guild, out _)) return;
        guild.ApplyClosed = closed;
        Save();
        PublishDetail(session.Context, guild);
        session.SystemMessage(closed ? "The guild no longer accepts applications." : "The guild accepts applications again.");
    }

    public static void SetDuty(ClientSession session, string name, int duty)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.ViceLeader, "Only the leader and vice leaders appoint positions.", out Guild guild, out GuildMember self)) return;
        GuildMember? target = guild.Member(name);
        if (target == null)
        {
            session.SystemMessage($"{name} is not a member of your guild.");
            return;
        }
        if (duty is < GuildDuty.Member or > GuildDuty.ViceLeader || target == self || target.Duty >= self.Duty || duty >= self.Duty)
        {
            session.SystemMessage("You can only appoint members below your own position to positions below your own.");
            SendMember(session, context, target);
            return;
        }
        if (target.Duty == duty) return;
        target.Duty = duty;
        Save();
        Update(context, guild, target);
        Tell(context, guild, $"{target.Name} is now {GuildDuty.Name(duty)} of the guild.");
        Log.Info(session.Tag, $"{session.Name} appoints {target.Name} as {GuildDuty.Name(duty)} in the guild {guild.Name}");
    }

    public static void TransferContribution(ClientSession session, string name, int amount)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.Member, "", out Guild guild, out GuildMember self)) return;
        GuildMember? target = guild.Member(name);
        if (target == null || target == self)
        {
            session.SystemMessage("Select another member of your guild.");
            return;
        }
        if (amount <= 0 || amount > self.Contribution)
        {
            session.SystemMessage($"You have {self.Contribution} contribution to transfer.");
            return;
        }
        self.Contribution -= amount;
        target.Contribution += amount;
        Save();
        PublishMember(context, guild, self);
        PublishMember(context, guild, target);
        session.SystemMessage($"You transferred {amount} contribution to {target.Name}.");
        OnlinePlayer(context, target.Name)?.SystemMessage($"{self.Name} transferred {amount} contribution to you.");
    }

    public static void ApplyDepartment(ClientSession session, string name, string explanation)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.Member, "", out Guild guild, out GuildMember self)) return;
        if (!ValidName(name) || name == GuildPackets.Empty)
        {
            session.SystemMessage($"A department name has 1 to {NameLength} characters and no spaces.");
            return;
        }
        if (guild.Department(name) != null)
        {
            session.SystemMessage($"The department {name} already exists.");
            return;
        }
        if (guild.Departments.Count >= DepartmentLimit)
        {
            session.SystemMessage("The guild cannot have more departments.");
            return;
        }
        if (self.Department.Length > 0)
        {
            session.SystemMessage($"You already belong to the department {self.Department}.");
            return;
        }
        guild.DepartmentRequests.RemoveAll(other => Same(other.Applicant, self.Name));
        var request = new GuildDepartmentRequest { Name = name, Applicant = self.Name, Explanation = Text(explanation, TenetLength) };
        guild.DepartmentRequests.Add(request);
        Save();
        foreach (ClientSession leader in Online(context, guild).Where(other => Duty(guild, other) >= GuildDuty.ViceLeader))
            ShowDepartmentRequest(leader, request);
        session.SystemMessage($"Your application for the department {name} was submitted to the guild leaders.");
    }

    public static void DecideDepartment(ClientSession session, string name, string applicantName, bool accept)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.ViceLeader, "Only the leader and vice leaders decide on departments.", out Guild guild, out _)) return;
        GuildDepartmentRequest? request = guild.DepartmentRequests
            .FirstOrDefault(other => Same(other.Name, name) && Same(other.Applicant, applicantName));
        if (request == null)
        {
            session.SystemMessage("That department application is no longer open.");
            return;
        }
        guild.DepartmentRequests.Remove(request);
        GuildMember? applicant = guild.Member(request.Applicant);
        ClientSession? target = OnlinePlayer(context, request.Applicant);
        if (!accept || applicant == null || guild.Department(request.Name) != null || guild.Departments.Count >= DepartmentLimit)
        {
            Save();
            target?.SystemMessage($"Your application for the department {request.Name} was declined.");
            session.SystemMessage($"The application for the department {request.Name} was declined.");
            return;
        }
        guild.Departments.Add(request.Name);
        applicant.Department = request.Name;
        if (applicant.Duty < GuildDuty.Director) applicant.Duty = GuildDuty.Director;
        Save();
        Update(context, guild, applicant);
        Tell(context, guild, $"The department {request.Name} was founded under {applicant.Name}.");
    }

    public static void SetDepartment(ClientSession session, string name, string? department)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.Director, "Only directors and guild leaders assign departments.", out Guild guild, out GuildMember self)) return;
        GuildMember? target = guild.Member(name);
        if (target == null)
        {
            session.SystemMessage($"{name} is not a member of your guild.");
            return;
        }
        string wanted = "";
        if (!string.IsNullOrEmpty(department) && department != GuildPackets.Empty)
        {
            string? existing = guild.Department(department);
            if (existing == null)
            {
                session.SystemMessage($"The guild has no department named {department}.");
                SendMember(session, context, target);
                return;
            }
            wanted = existing;
        }
        bool own = self.Department.Length > 0
            && (wanted.Length == 0 || Same(wanted, self.Department))
            && (target.Department.Length == 0 || Same(target.Department, self.Department));
        if (self.Duty < GuildDuty.ViceLeader && !own)
        {
            session.SystemMessage("A director can only move members into or out of the own department.");
            SendMember(session, context, target);
            return;
        }
        if (target != self && target.Duty >= self.Duty)
        {
            session.SystemMessage($"You cannot reassign a {GuildDuty.Name(target.Duty)}.");
            SendMember(session, context, target);
            return;
        }
        if (target.Department == wanted) return;
        target.Department = wanted;
        Save();
        Update(context, guild, target);
        OnlinePlayer(context, target.Name)?.SystemMessage(wanted.Length > 0
            ? $"You were assigned to the department {wanted}."
            : "You were removed from your department.");
    }

    public static void CloseDepartment(ClientSession session, string name)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.Director, "Only directors and guild leaders repeal departments.", out Guild guild, out GuildMember self)) return;
        string? department = guild.Department(name);
        if (department == null)
        {
            session.SystemMessage($"The guild has no department named {name}.");
            return;
        }
        if (self.Duty < GuildDuty.ViceLeader && !Same(self.Department, department))
        {
            session.SystemMessage("A director can only repeal the own department.");
            return;
        }
        guild.Departments.Remove(department);
        List<GuildMember> affected = guild.Members.Where(member => Same(member.Department, department)).ToList();
        foreach (GuildMember member in affected)
        {
            member.Department = "";
            if (member.Duty == GuildDuty.Director) member.Duty = GuildDuty.Member;
        }
        Save();
        foreach (GuildMember member in affected) Update(context, guild, member);
        Tell(context, guild, $"The department {department} was repealed.");
    }

    public static void Relate(ClientSession session, int command, string name)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.ViceLeader, "Only the leader and vice leaders conduct diplomacy.", out Guild guild, out _)) return;
        Guild? other = Find(context, name);
        if (other == null || other == guild)
        {
            session.SystemMessage("Select another guild.");
            return;
        }
        int state = guild.Relation(other);
        switch (command)
        {
            case GuildRelationCommand.Ally:
                switch (state)
                {
                    case GuildRelation.RequestReceived or GuildRelation.HostileRequestReceived:
                        Relate(context, guild, other, GuildRelation.Alliance, GuildRelation.Alliance, "are now allies");
                        break;
                    case GuildRelation.Normal:
                        Relate(context, guild, other, GuildRelation.RequestSent, GuildRelation.RequestReceived, null);
                        Tell(context, other, $"The guild {guild.Name} proposes an alliance.");
                        session.SystemMessage($"You proposed an alliance to {other.Name}.");
                        break;
                    case GuildRelation.Hostile or GuildRelation.War:
                        Relate(context, guild, other, GuildRelation.HostileRequestSent, GuildRelation.HostileRequestReceived, null);
                        Tell(context, other, $"The hostile guild {guild.Name} proposes an alliance.");
                        session.SystemMessage($"You proposed an alliance to {other.Name}.");
                        break;
                    case GuildRelation.Alliance:
                        session.SystemMessage($"{other.Name} is already your ally.");
                        break;
                    default:
                        session.SystemMessage($"Your alliance request to {other.Name} is still open.");
                        break;
                }
                break;
            case GuildRelationCommand.Cancel:
                switch (state)
                {
                    case GuildRelation.RequestSent or GuildRelation.RequestReceived:
                        Relate(context, guild, other, GuildRelation.Normal, GuildRelation.Normal, "dropped the alliance request");
                        break;
                    case GuildRelation.HostileRequestSent or GuildRelation.HostileRequestReceived:
                        Relate(context, guild, other, GuildRelation.Hostile, GuildRelation.Hostile, "dropped the alliance request");
                        break;
                    case GuildRelation.Alliance:
                        Relate(context, guild, other, GuildRelation.Normal, GuildRelation.Normal, "ended their alliance");
                        break;
                    default:
                        session.SystemMessage($"There is no open request with {other.Name}.");
                        break;
                }
                break;
            case GuildRelationCommand.Hostility:
                if (state is GuildRelation.Hostile or GuildRelation.War) session.SystemMessage($"{other.Name} is already hostile.");
                else Relate(context, guild, other, GuildRelation.Hostile, GuildRelation.Hostile, "are now hostile");
                break;
            case GuildRelationCommand.War:
                if (state == GuildRelation.War) session.SystemMessage($"You are already at war with {other.Name}.");
                else Relate(context, guild, other, GuildRelation.War, GuildRelation.War, "are now at war");
                break;
            default:
                session.SystemMessage("Unknown diplomatic action.");
                break;
        }
    }

    public static void Chat(ClientSession session, ChatChannel channel, string text)
    {
        ServerContext context = session.Context;
        if (!Require(session, GuildDuty.Member, "", out Guild guild, out GuildMember self)) return;
        bool department = channel is ChatChannel.Department or ChatChannel.DepartmentNotice;
        if (department && self.Department.Length == 0)
        {
            session.SystemMessage("You are not in a guild department.");
            return;
        }
        if (channel == ChatChannel.GuildNotice && self.Duty < GuildDuty.ViceLeader)
        {
            session.SystemMessage("Only the leader and vice leaders post guild bulletins.");
            return;
        }
        if (channel == ChatChannel.DepartmentNotice && self.Duty < GuildDuty.Director)
        {
            session.SystemMessage("Only the director posts department bulletins.");
            return;
        }
        foreach (ClientSession member in Online(context, guild))
        {
            if (department && !Same(guild.Member(member.Name)?.Department ?? "", self.Department)) continue;
            member.SendChannelChat(channel, session, text);
        }
        Log.Info(session.Tag, $"{session.Name} on {channel} of {guild.Name}: {text}");
    }

    private static List<Guild> Guilds(ServerContext context)
    {
        if (_guilds != null) return _guilds;
        _path = context.Settings.Resolve("data\\guilds.json");
        _guilds = [];
        try
        {
            if (File.Exists(_path))
                _guilds = JsonSerializer.Deserialize<GuildStore>(File.ReadAllText(_path), EmulatorSettings.Json)?.Guilds ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Guilds", $"Load failed: {exception.Message}");
        }
        return _guilds;
    }

    internal static void Save()
    {
        if (_guilds == null || _path.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new GuildStore { Guilds = _guilds }, EmulatorSettings.Json));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Guilds", $"Save failed: {exception.Message}");
        }
    }

    private static bool Require(ClientSession session, int duty, string denied, out Guild guild, out GuildMember self)
    {
        Guild? found = Of(session.Context, session.Name);
        GuildMember? member = found?.Member(session.Name);
        guild = found!;
        self = member!;
        if (found == null || member == null)
        {
            session.SystemMessage("You are not in a guild.");
            return false;
        }
        if (member.Duty >= duty) return true;
        session.SystemMessage(denied);
        return false;
    }

    private static bool ValidName(string name)
        => name.Length is > 0 and <= NameLength && name.All(letter => letter > ' ' && letter != '%' && letter != 0x7f);

    private static string Text(string text, int limit)
    {
        string cleaned = UiPackets.Line(text).Trim();
        return cleaned.Length > limit ? cleaned[..limit] : cleaned;
    }

    private static bool Snapshot(GuildMember member, ClientSession session)
    {
        int photo = session.Portrait != 0 ? session.Portrait : session.Character.Face;
        string nationality = session.Properties.Text("CountryName");
        string title = session.Character.Title;
        int skill = session.Character.Skills.Sum(entry => entry.Level);
        if (member.Photo == photo && member.Nationality == nationality && member.Title == title && member.SkillLevel == skill
            && member.Name == session.Name)
            return false;
        member.Name = session.Name;
        member.Photo = photo;
        member.Nationality = nationality;
        member.Title = title;
        member.SkillLevel = skill;
        return true;
    }

    private static ClientSession? OnlinePlayer(ServerContext context, string name)
        => context.World.FindPlayer(name) is { InWorld: true } session ? session : null;

    private static List<ClientSession> Online(ServerContext context, Guild guild, ClientSession? except = null)
        => context.World.Sessions.Where(session => session.InWorld && session != except && guild.Member(session.Name) != null).ToList();

    private static int Duty(Guild guild, ClientSession session) => guild.Member(session.Name)?.Duty ?? GuildDuty.Member;

    private static List<Guild> WithdrawApplications(List<Guild> guilds, string player)
    {
        List<Guild> changed = guilds.Where(guild => guild.Applicants.RemoveAll(applicant => Same(applicant.Name, player)) > 0).ToList();
        if (changed.Count > 0) Save();
        return changed;
    }

    private static string[] GuildRow(Guild guild)
        => [guild.Name, guild.Flag.ToString(), guild.Commerce.ToString(), guild.Industry.ToString(), guild.Tenet];

    private static string[] MemberRow(ServerContext context, GuildMember member, ClientSession? gone)
    {
        ClientSession? session = OnlinePlayer(context, member.Name);
        bool online = session != null && session != gone;
        return
        [
            member.Name, member.Department, member.Duty.ToString(), online ? "1" : "0", member.Photo.ToString(),
            member.Nationality, member.Title, member.SkillLevel.ToString(), member.Contribution.ToString(),
        ];
    }

    private static string[] ApplicantRow(GuildMember applicant)
        => [applicant.Name, applicant.Photo.ToString(), applicant.Nationality, applicant.Title, applicant.SkillLevel.ToString()];

    private static string[] RelationRow(Guild guild, Guild other)
        => [other.Name, guild.Relation(other).ToString(), other.Flag.ToString()];

    private static byte[] Detail(Guild guild)
        => BboProtocolPackets.ConsDetail(new BboGuildDetail(guild.ApplyClosed, guild.Flag, guild.Tenet, guild.Name, guild.Members.Count,
            guild.MemberLimit, guild.Level, guild.Fund, 0, guild.Headquarters, guild.Commerce, guild.Industry, 0, 0, 0, 0));

    private static void Send(ClientSession session, IEnumerable<byte[]> packets, string note)
    {
        foreach (byte[] packet in packets) session.Send(packet, note);
    }

    private static void SendAll(ClientSession session, Guild guild)
    {
        ServerContext context = session.Context;
        session.Send(Detail(guild), "(guild detail)");
        session.Send(GuildPackets.Clear(GuildList.Members), "(clear guild members)");
        Send(session, GuildPackets.Rows(GuildList.Members, guild.Members.Select(member => MemberRow(context, member, null))), "(guild members)");
        SendApplicants(session, guild);
        SendRelations(session, guild);
    }

    private static void SendMember(ClientSession session, ServerContext context, GuildMember member)
        => Send(session, GuildPackets.Rows(GuildList.Members, [MemberRow(context, member, null)]), $"(guild member {member.Name})");

    private static void SendApplicants(ClientSession session, Guild guild)
    {
        session.Send(GuildPackets.Clear(GuildList.Applicants), "(clear guild applicants)");
        bool recruiter = Duty(guild, session) >= GuildDuty.Officer;
        List<GuildMember> applicants = recruiter ? guild.Applicants : new List<GuildMember>();
        session.Send(GuildPackets.Applicants(applicants.Select(applicant => applicant.Name).ToList()), "(guild applicants)");
        Send(session, GuildPackets.Rows(GuildList.Applicants, applicants.Select(ApplicantRow)), "(guild applicant details)");
    }

    private static void SendRelations(ClientSession session, Guild guild)
    {
        session.Send(GuildPackets.Clear(GuildList.Relations), "(clear guild relations)");
        Send(session, GuildPackets.Rows(GuildList.Relations,
            Guilds(session.Context).Where(other => other != guild).Select(other => RelationRow(guild, other))), "(guild relations)");
    }

    private static void ShowDepartmentRequest(ClientSession session, GuildDepartmentRequest request)
        => session.Send(GuildPackets.ShowCreateDepartment(request.Applicant, request.Name, request.Explanation),
            $"(department application of {request.Applicant})");

    private static void PublishDetail(ServerContext context, Guild guild, ClientSession? except = null)
    {
        byte[] detail = Detail(guild);
        foreach (ClientSession member in Online(context, guild, except)) member.Send(detail, "(guild detail)");
    }

    private static void PublishMember(ServerContext context, Guild guild, GuildMember member, ClientSession? except = null)
    {
        byte[][] row = GuildPackets.Rows(GuildList.Members, [MemberRow(context, member, null)]).ToArray();
        foreach (ClientSession other in Online(context, guild, except)) Send(other, row, $"(guild member {member.Name})");
    }

    internal static void PublishApplicants(ServerContext context, Guild guild)
    {
        foreach (ClientSession member in Online(context, guild)) SendApplicants(member, guild);
    }

    private static void PublishRelations(ServerContext context, Guild created)
    {
        foreach (Guild other in Guilds(context).Where(other => other != created))
        {
            byte[][] row = GuildPackets.Rows(GuildList.Relations, [RelationRow(other, created)]).ToArray();
            foreach (ClientSession member in Online(context, other)) Send(member, row, "(guild relation)");
        }
    }

    private static void Update(ServerContext context, Guild guild, GuildMember member)
    {
        PublishMember(context, guild, member);
        ClientSession? session = OnlinePlayer(context, member.Name);
        if (session == null) return;
        session.ApplyGuild(guild, member);
        SendApplicants(session, guild);
    }

    private static void Relate(ServerContext context, Guild guild, Guild other, int mine, int theirs, string? news)
    {
        SetRelation(guild, other, mine);
        SetRelation(other, guild, theirs);
        Save();
        byte[][] own = GuildPackets.Rows(GuildList.Relations, [RelationRow(guild, other)]).ToArray();
        foreach (ClientSession member in Online(context, guild)) Send(member, own, "(guild relation)");
        byte[][] foreign = GuildPackets.Rows(GuildList.Relations, [RelationRow(other, guild)]).ToArray();
        foreach (ClientSession member in Online(context, other)) Send(member, foreign, "(guild relation)");
        if (news == null) return;
        Tell(context, guild, $"{guild.Name} and {other.Name} {news}.");
        Tell(context, other, $"{guild.Name} and {other.Name} {news}.");
    }

    private static void SetRelation(Guild guild, Guild other, int state)
    {
        if (state == GuildRelation.Normal) guild.Relations.Remove(other.Name);
        else guild.Relations[other.Name] = state;
    }

    private static void Remove(ServerContext context, Guild guild, GuildMember member, string notice)
    {
        guild.Members.Remove(member);
        guild.DepartmentRequests.RemoveAll(request => Same(request.Applicant, member.Name));
        Save();
        ClientSession? session = OnlinePlayer(context, member.Name);
        if (session != null)
        {
            Part(session);
            session.SystemMessage(notice);
        }
        byte[] gone = GuildPackets.Delete(GuildList.Members, member.Name);
        foreach (ClientSession other in Online(context, guild)) other.Send(gone, $"(guild member {member.Name} removed)");
        PublishDetail(context, guild);
    }

    private static void Part(ClientSession session)
    {
        session.ApplyGuild(null, null);
        session.Save();
        session.Send(GuildPackets.Clear(GuildList.Members), "(clear guild members)");
        session.Send(GuildPackets.Clear(GuildList.Applicants), "(clear guild applicants)");
        session.Send(GuildPackets.Applicants([]), "(guild applicants)");
        session.Send(GuildPackets.Clear(GuildList.Relations), "(clear guild relations)");
    }

    private static void Tell(ServerContext context, Guild guild, string text)
    {
        foreach (ClientSession member in Online(context, guild)) member.SystemMessage(text);
    }
}
