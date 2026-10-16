using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int GuildMenuOption = 200;
    public const int GuildCreateOption = 201;
    public const int GuildJoinOption = 202;
    public const int GuildLastOption = 239;

    public bool GuildFounding { get; internal set; }

    public bool GuildEntered { get; private set; }

    public Guild? Guild => GuildDirectory.Of(Context, Name);

    public void EnterWorldGuild()
    {
        GuildEntered = true;
        GuildDirectory.Enter(this);
    }

    public void LeaveWorldGuild()
    {
        GuildFounding = false;
        if (!GuildEntered) return;
        GuildEntered = false;
        GuildDirectory.Leave(this);
    }

    public bool HandleGuildOption(int target, int option)
    {
        if (option is < GuildMenuOption or > GuildLastOption) return false;
        switch (option)
        {
            case GuildCreateOption:
                GuildDirectory.OfferCreation(this, target);
                break;
            case GuildJoinOption:
                if (Guild is { } joined) GuildSay(target, $"You already belong to the guild {joined.Name}.");
                else GuildDirectory.ShowAll(this);
                break;
            default:
                ShowGuildMenu(target);
                break;
        }
        return true;
    }

    public void ApplyGuild(Guild? guild, GuildMember? member)
    {
        string name = guild != null && member != null ? guild.Name : "";
        Character.Guild = name;
        if (name.Length == 0 && Properties.Text("ConsName").Length == 0) return;
        SetGuildProperty("ConsName", name, true);
        SetGuildProperty("ConsFlag", name.Length > 0 ? guild!.Flag : 0, true);
        SetGuildProperty("ConsDuty", member?.Duty ?? 0, false);
        SetGuildProperty("ConsDept", member?.Department ?? "", false);
    }

    public void GuildSay(int target, string text)
        => Send(ChatPackets.NpcDialogue(target,
        [
            (DialogueEntry.Text, 0, text),
            (DialogueEntry.Option, GuildMenuOption, "Back"),
            (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
        ]), "(guild dialogue)");

    private void ShowGuildMenu(int target)
    {
        var entries = new List<(DialogueEntry, int, string)>();
        Guild? guild = Guild;
        GuildMember? self = guild?.Member(Name);
        if (guild != null && self != null)
        {
            entries.Add((DialogueEntry.Text, 0,
                $"You serve the guild {guild.Name} as {GuildDuty.Name(self.Duty)}. It has {guild.Members.Count} of {guild.MemberLimit} members."));
        }
        else
        {
            Data.GuildRules rules = Rule.Guild;
            entries.Add((DialogueEntry.Text, 0, $"Founding a guild needs a skill of Level {rules.FounderSkillLevel}, {rules.CreationFee} silver coins"
                + (rules.RequireSupporters ? $" and a team of {rules.Supporters} supporters who belong to no guild" : "")
                + ". You can also apply to an existing guild."));
            entries.Add((DialogueEntry.Option, GuildCreateOption, $"Found a guild ({rules.CreationFee} silver)"));
            entries.Add((DialogueEntry.Option, GuildJoinOption, "Join a guild"));
        }
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(guild dialogue)");
    }

    private void SetGuildProperty(string name, object value, bool visibleToOthers)
    {
        if (!PropertyTable.Has(name)) return;
        if (Properties.TryGetValue(name, out object? current) && Equals(current, value)) return;
        SetProperty(name, value, visibleToOthers);
    }
}
