using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int BboFirstOption = 420;
    public const int NoviceGuideOption = 420;
    public const int RankingMenuOption = 421;
    public const int RichRankingOption = 422;
    public const int GuildRankingOption = 423;
    public const int MasterRankingOption = 424;
    public const int MentorMenuOption = 425;
    public const int MentorRecruitOption = 426;
    public const int MentorLeaveOption = 427;
    public const int BboLastOption = 449;
    public const int MostApprentices = 5;

    private static readonly (string Name, object Value)[] BboDefaults = [("UnionName", ""), ("TeamPickWay", 0), ("TeamPickLevel", 0)];

    public void ApplyBboSocialProperties()
    {
        foreach ((string name, object value) in BboDefaults)
        {
            if (PropertyTable.Has(name) && !Properties.ContainsKey(name)) Properties[name] = value;
        }
    }

    public void EnterWorldBbo()
    {
        SendBboQuestState(clear: false);
        if (Character.Master.Length > 0 && Context.World.FindPlayer(Character.Master) is { InWorld: true } master)
            master.SystemMessage($"Your apprentice {Name} is online.");
        foreach (string apprentice in Character.Apprentices)
        {
            if (Context.World.FindPlayer(apprentice) is { InWorld: true } online) online.SystemMessage($"Your master {Name} is online.");
        }
    }

    public bool HandleBboSocialOption(int target, int option)
    {
        if (option is < BboFirstOption or > BboLastOption) return false;
        switch (option)
        {
            case NoviceGuideOption:
                StartNoviceGuide();
                Send(ChatPackets.NpcDialogue(target, [(DialogueEntry.Text, 0, "Follow the guide window step by step. Press Next when you are ready.")]), "(novice dialogue)");
                break;
            case RichRankingOption:
                BboSay(target, RankingText("The richest captains", "silver", Rankings.Richest(Context)), RankingMenuOption);
                break;
            case GuildRankingOption:
                BboSay(target, RankingText("The largest guilds", "members", Rankings.Guilds(Context)), RankingMenuOption);
                break;
            case MasterRankingOption:
                BboSay(target, RankingText("The most respected masters", "apprentices", Rankings.Masters(Context)), RankingMenuOption);
                break;
            case MentorMenuOption:
                ShowMentorMenu(target);
                break;
            case MentorRecruitOption:
                SetRecruiting(!Character.RecruitingApprentices);
                ShowMentorMenu(target);
                break;
            case MentorLeaveOption:
                if (Character.Master.Length > 0) RemoveMentor(Character.Master);
                ShowMentorMenu(target);
                break;
            default:
                Send(ChatPackets.NpcDialogue(target,
                [
                    (DialogueEntry.Text, 0, "Which list would you like to see?"),
                    (DialogueEntry.Option, RichRankingOption, "Fortune ranking"),
                    (DialogueEntry.Option, GuildRankingOption, "Guild ranking"),
                    (DialogueEntry.Option, MasterRankingOption, "Master ranking"),
                    (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
                ]), "(ranking dialogue)");
                break;
        }
        return true;
    }

    public void SetTeamPick(string property, int value)
    {
        Team? team = Team;
        if (team == null)
        {
            SystemMessage("You are not in a team.");
            return;
        }
        if (team.Leader != this)
        {
            SystemMessage("Only the team leader can change how loot is shared.");
            return;
        }
        foreach (ClientSession member in team.Members) member.SetKnownProperty(property, Math.Max(0, value));
    }

    public void ChangeTeamCaptain(string name)
    {
        Team? team = Team;
        if (team == null || team.Leader != this)
        {
            SystemMessage("Only the team leader can hand over the lead.");
            return;
        }
        ClientSession? next = team.Members.FirstOrDefault(member => member.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (next == null || next == this)
        {
            SystemMessage($"{name} is not in your team.");
            return;
        }
        team.Members.Remove(next);
        team.Members.Insert(0, next);
        TeamDirectory.Refresh(this);
        foreach (ClientSession member in team.Members) member.SystemMessage($"{next.Name} is now the team leader.");
        Log.Info(Tag, $"{Name} hands the team lead to {next.Name}");
    }

    public void AskJoinTeam(string first, string second)
    {
        ClientSession? target = OnlinePlayer(first) ?? OnlinePlayer(second);
        if (target == null || target == this)
        {
            SystemMessage("That player is not online.");
            return;
        }
        if (Team != null)
        {
            SystemMessage("You are already in a team.");
            return;
        }
        ClientSession leader = target.Team?.Leader ?? target;
        ClientSession asker = this;
        leader.Ask($"{Name} asks to join your team.", yes =>
        {
            if (!asker.InWorld) return;
            if (yes) TeamDirectory.Invite(leader, asker.Name);
            else asker.SystemMessage($"{leader.Name} declined your request.");
        });
        SystemMessage($"You asked {leader.Name} to let you join the team.");
    }

    public void InviteToGuild(string name)
    {
        Guild? guild = Guild;
        GuildMember? self = guild?.Member(Name);
        if (guild == null || self == null)
        {
            SystemMessage("You do not belong to a guild.");
            return;
        }
        if (self.Duty < GuildDuty.Officer)
        {
            SystemMessage("Only guild officers can invite players.");
            return;
        }
        ClientSession? target = OnlinePlayer(name);
        if (target == null || target == this)
        {
            SystemMessage($"{name} is not online.");
            return;
        }
        if (target.Guild != null)
        {
            SystemMessage($"{target.Name} already belongs to a guild.");
            return;
        }
        if (guild.Members.Count >= guild.MemberLimit)
        {
            SystemMessage("The guild is full.");
            return;
        }
        string guildName = guild.Name;
        ClientSession inviter = this;
        target.Ask($"{Name} invites you to join the guild {guildName}.", yes => target.AnswerGuildInvite(inviter, guildName, yes));
        SystemMessage($"You invited {target.Name} to the guild.");
        Log.Info(Tag, $"{Name} invites {target.Name} to the guild {guildName}");
    }

    public void CancelGuildApplication(string name)
    {
        Guild? guild = GuildDirectory.Find(Context, name);
        if (guild == null || guild.Applicants.RemoveAll(applicant => GuildDirectory.Same(applicant.Name, Name)) == 0)
        {
            SystemMessage("You have no open application there.");
            return;
        }
        GuildDirectory.Save();
        GuildDirectory.PublishApplicants(Context, guild);
        SystemMessage($"You withdrew your application to {guild.Name}.");
    }

    public void ShowGuildDetail(string name)
    {
        Guild? guild = name.Length > 0 ? GuildDirectory.Find(Context, name) : Guild;
        if (guild == null)
        {
            SystemMessage($"There is no guild named {name}.");
            return;
        }
        Send(GuildPackets.Detail(guild.ApplyClosed, guild.Flag, guild.Tenet, guild.Name, guild.Members.Count, guild.MemberLimit,
            guild.Fund, guild.Headquarters, guild.Commerce, guild.Industry, guild.Level), $"(guild detail {guild.Name})");
    }

    public void RefreshGuildLists()
    {
        if (GuildEntered && Guild != null) GuildDirectory.Enter(this);
    }

    public void AskMaster(string name)
    {
        ClientSession? target = OnlinePlayer(name);
        if (target == null || target == this)
        {
            SystemMessage($"{name} is not online.");
            return;
        }
        if (Character.Master.Length > 0)
        {
            SystemMessage($"You already learn from {Character.Master}.");
            return;
        }
        if (GuildDirectory.Same(target.Character.Master, Name) || Character.Apprentices.Contains(target.Name))
        {
            SystemMessage($"{target.Name} is your apprentice.");
            return;
        }
        if (target.Character.Apprentices.Count >= MostApprentices)
        {
            SystemMessage($"{target.Name} cannot take more apprentices.");
            return;
        }
        ClientSession apprentice = this;
        target.Ask($"{Name} asks to become your apprentice.", yes => target.AnswerApprentice(apprentice, yes));
        SystemMessage($"You asked {target.Name} to become your master.");
    }

    public void SearchMaster(string name)
    {
        if (name.Length > 0)
        {
            CharacterRecord? record = MailDirectory.Find(Context, name);
            if (record == null)
            {
                SystemMessage($"There is no captain named {name}.");
                return;
            }
            bool online = OnlinePlayer(record.Name) != null;
            SystemMessage($"{record.Name}: {record.Apprentices.Count} of {MostApprentices} apprentices, "
                + (record.RecruitingApprentices ? "takes apprentices" : "takes no apprentices") + (online ? ", online." : ", offline."));
            return;
        }
        List<string> masters = Context.World.Sessions
            .Where(session => session.InWorld && session != this && session.Character.RecruitingApprentices
                && session.Character.Apprentices.Count < MostApprentices)
            .Select(session => session.Name)
            .ToList();
        SystemMessage(masters.Count > 0 ? $"Masters taking apprentices: {string.Join(", ", masters)}." : "Nobody online takes apprentices right now.");
    }

    public void ShowMentorList()
    {
        SystemMessage(Character.Master.Length > 0 ? $"Your master: {Character.Master}." : "You have no master.");
        SystemMessage(Character.Apprentices.Count > 0
            ? $"Your apprentices: {string.Join(", ", Character.Apprentices)}."
            : "You have no apprentices.");
    }

    public void RemoveMentor(string name)
    {
        if (GuildDirectory.Same(Character.Master, name))
        {
            CharacterRecord? master = MailDirectory.Find(Context, Character.Master);
            master?.Apprentices.RemoveWhere(entry => GuildDirectory.Same(entry, Name));
            OnlinePlayer(Character.Master)?.SystemMessage($"{Name} is no longer your apprentice.");
            SystemMessage($"You left your master {Character.Master}.");
            Character.Master = "";
            Context.Accounts.Save();
            return;
        }
        string? apprentice = Character.Apprentices.FirstOrDefault(entry => GuildDirectory.Same(entry, name));
        if (apprentice == null)
        {
            SystemMessage($"{name} is neither your master nor your apprentice.");
            return;
        }
        Character.Apprentices.Remove(apprentice);
        CharacterRecord? record = MailDirectory.Find(Context, apprentice);
        if (record != null && GuildDirectory.Same(record.Master, Name)) record.Master = "";
        OnlinePlayer(apprentice)?.SystemMessage($"{Name} is no longer your master.");
        SystemMessage($"{apprentice} is no longer your apprentice.");
        Context.Accounts.Save();
    }

    public void SetRecruiting(bool recruiting)
    {
        Character.RecruitingApprentices = recruiting;
        Save();
        SystemMessage(recruiting ? "You now take apprentices." : "You no longer take apprentices.");
    }

    public void AskMailGoods()
    {
        if (Character.Parcels.Count > 0) SendParcels();
        else SystemMessage("There is no parcel for you.");
    }

    public void NotAvailable(string feature, string command)
    {
        SystemMessage($"{feature} is not available on this server.");
        Log.Debug(Tag, $"{Name} unavailable feature: {command}");
    }

    private void AnswerGuildInvite(ClientSession inviter, string guildName, bool yes)
    {
        if (!yes)
        {
            if (inviter.InWorld) inviter.SystemMessage($"{Name} declined your guild invitation.");
            return;
        }
        Guild? guild = GuildDirectory.Find(Context, guildName);
        if (guild == null || !inviter.InWorld || guild.Member(inviter.Name) == null)
        {
            SystemMessage("The invitation is no longer valid.");
            return;
        }
        if (guild.Applicant(Name) == null) GuildDirectory.Apply(this, guild.Name);
        if (guild.Applicant(Name) != null) GuildDirectory.ProcessApplication(inviter, Name, true);
    }

    private void AnswerApprentice(ClientSession apprentice, bool yes)
    {
        if (!yes)
        {
            if (apprentice.InWorld) apprentice.SystemMessage($"{Name} declined to take you as an apprentice.");
            return;
        }
        if (!apprentice.InWorld || apprentice.Character.Master.Length > 0 || Character.Apprentices.Count >= MostApprentices)
        {
            SystemMessage("The request is no longer valid.");
            return;
        }
        apprentice.Character.Master = Name;
        Character.Apprentices.Add(apprentice.Name);
        Context.Accounts.Save();
        apprentice.SystemMessage($"{Name} is now your master.", SystemMessageKind.Prompt);
        SystemMessage($"{apprentice.Name} is now your apprentice.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} takes {apprentice.Name} as apprentice");
    }

    private void ShowMentorMenu(int target)
    {
        string text = (Character.Master.Length > 0 ? $"Your master is {Character.Master}. " : "You have no master. ")
            + (Character.Apprentices.Count > 0 ? $"Your apprentices: {string.Join(", ", Character.Apprentices)}." : "You have no apprentices.");
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, text) };
        entries.Add((DialogueEntry.Option, MentorRecruitOption, Character.RecruitingApprentices ? "Stop taking apprentices" : "Take apprentices"));
        if (Character.Master.Length > 0) entries.Add((DialogueEntry.Option, MentorLeaveOption, "Leave my master"));
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(mentor dialogue)");
    }

    private void BboSay(int target, string text, int back)
        => Send(ChatPackets.NpcDialogue(target,
        [
            (DialogueEntry.Text, 0, text),
            (DialogueEntry.Option, back, "Back"),
            (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
        ]), "(bbo dialogue)");

    private static string RankingText(string title, string unit, List<RankingEntry> entries)
        => entries.Count == 0
            ? $"{title}: nobody is listed yet."
            : $"{title}: " + string.Join("; ", entries.Select(entry => $"{entry.Rank}. {entry.Name} ({entry.Value} {unit})")) + ".";

    private ClientSession? OnlinePlayer(string name)
        => name.Length > 0 && Context.World.FindPlayer(name) is { InWorld: true } player ? player : null;
}
