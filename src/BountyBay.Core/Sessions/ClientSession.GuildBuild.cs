using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int GuildHallOption = 460;
    public const int GuildHallCopyOption = 461;
    public const int GuildHallMatchOption = 462;
    public const int GuildHallComposeOption = 463;
    public const int GuildHallMedalOption = 464;
    public const int GuildHallBuildOption = 465;
    public const int GuildHallActivityOption = 466;
    public const int GuildHallAllServerOption = 467;
    public const int GuildHallLastOption = 479;

    public bool HandleGuildHallOption(int target, int option)
    {
        if (option is < GuildHallOption or > GuildHallLastOption) return false;
        switch (option)
        {
            case GuildHallCopyOption:
                OpenGuildCopy();
                break;
            case GuildHallMatchOption:
                ShowGuildMatches(1);
                GuildHallSay(target, "The guild tournament bracket has been sent to your Guild Match window.");
                return true;
            case GuildHallAllServerOption:
                ShowGuildMatches(2);
                GuildHallSay(target, "The all-server bracket has been sent to your Guild Match window.");
                return true;
            case GuildHallComposeOption:
                OpenGuildArm(GuildArmPackets.ComposeView);
                break;
            case GuildHallMedalOption:
                OpenGuildArm(GuildArmPackets.MedalView);
                break;
            case GuildHallBuildOption:
                ShowGuildBuildSummary(target);
                return true;
            case GuildHallActivityOption:
                ShowGuildActivitySummary(target);
                return true;
            default:
                ShowGuildHallMenu(target);
                return true;
        }
        Send(UiPackets.CloseMenu(), "(close guild hall menu)");
        return true;
    }

    public void ShowGuildHallMenu(int target)
    {
        Guild? guild = Guild;
        var entries = new List<(DialogueEntry, int, string)>
        {
            (DialogueEntry.Text, 0, guild == null
                ? "The guild hall serves only guild members."
                : $"Welcome to the hall of {guild.Name}. What do you need?"),
        };
        if (guild != null)
        {
            entries.Add((DialogueEntry.Option, GuildHallCopyOption, "Secret Isle adventure (guild instance)"));
            entries.Add((DialogueEntry.Option, GuildHallMatchOption, "Guild tournament and wagers"));
            entries.Add((DialogueEntry.Option, GuildHallAllServerOption, "All-server guild tournament"));
            entries.Add((DialogueEntry.Option, GuildHallComposeOption, "Synthesize an Honor Medal"));
            entries.Add((DialogueEntry.Option, GuildHallMedalOption, "Manage an Honor Medal"));
            entries.Add((DialogueEntry.Option, GuildHallBuildOption, "Guild construction"));
            entries.Add((DialogueEntry.Option, GuildHallActivityOption, "Guild activity and welfare"));
        }
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(guild hall)");
    }

    public void EnterWorldGuildBuild()
    {
        CheckGuildCopyOnEnter();
        if (Guild is not { } guild) return;
        GuildBuildState state = GuildBuildDirectory.State(Context, guild);
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (state.LastActive.TryGetValue(Name, out string? last) && last == today)
        {
            SendGuildActivity(guild);
            return;
        }
        state.LastActive[Name] = today;
        GuildBuildDirectory.AddActivity(Context, guild, GuildBuildDirectory.Rules(Context).Activity.DailyLogin, $"{Name} logged in");
    }

    public void AskGuildActivity(string guildName)
    {
        Guild? guild = guildName.Length > 0 ? GuildDirectory.Find(Context, guildName) : Guild;
        guild ??= Guild;
        if (guild == null)
        {
            Send(GuildBuildPackets.Activity(0, 0), "(guild activity)");
            return;
        }
        SendGuildActivity(guild);
    }

    public void AskGuildConstruction(string guildName)
    {
        Guild? guild = Guild;
        if (guild == null || (guildName.Length > 0 && !GuildDirectory.Same(guild.Name, guildName)))
        {
            SystemMessage("Only members can view their guild's construction.", SystemMessageKind.Prompt);
            return;
        }
        Send(GuildBuildDirectory.Construction(Context, guild), "(guild construction)");
    }

    public void AskGuildLog(int page)
    {
        if (Guild is not { } guild)
        {
            Send(GuildBuildPackets.LogLine(0, "-"), "(guild log)");
            return;
        }
        GuildBuildState state = GuildBuildDirectory.State(Context, guild);
        int size = Math.Max(1, GuildBuildDirectory.Rules(Context).Construction.LogPageSize);
        int pages = Math.Max(1, (state.Log.Count + size - 1) / size);
        int index = Math.Clamp(page, 0, pages - 1);
        List<string> lines = state.Log.Skip(index * size).Take(size).ToList();
        if (lines.Count == 0) lines.Add("No guild events have been recorded yet.");
        foreach (string line in lines) Send(GuildBuildPackets.LogLine(state.Log.Count, line), "(guild log)");
    }

    public bool UseGuildBuildItem(int index, ItemStack item)
    {
        GuildBuildMedal? medal = GuildBuildDirectory.Rules(Context).Construction.Medals.FirstOrDefault(entry => entry.Item == item.Name);
        if (medal == null) return false;
        if (Guild is not { } guild)
        {
            SystemMessage("Only guild members can use this medal.", SystemMessageKind.Prompt);
            return true;
        }
        if (!GuildBuildDirectory.AddDegree(Context, guild, medal.Degree, medal.Amount, Name))
        {
            SystemMessage("Your guild cannot be built any further.", SystemMessageKind.Prompt);
            return true;
        }
        TakeFrom(ViewId.BagItems, index, 1, "(guild medal used)");
        if (guild.Member(Name) is { } member)
        {
            member.Contribution += medal.Contribution;
            GuildDirectory.Save();
        }
        GuildBuildDirectory.AddActivity(Context, guild, medal.Contribution, $"{Name} used {item.Name}");
        Send(GuildBuildDirectory.Construction(Context, guild), "(guild construction)");
        SystemMessage($"The {GuildBuildDirectory.DegreeNames[medal.Degree]} degree of {guild.Name} rises by {medal.Amount}; your contribution rises by {medal.Contribution}.");
        Save();
        Log.Info(Tag, $"{Name} uses {item.Name} for guild {guild.Name}");
        return true;
    }

    private void SendGuildActivity(Guild guild)
    {
        GuildBuildState state = GuildBuildDirectory.State(Context, guild);
        Send(GuildBuildPackets.Activity(GuildBuildDirectory.Rules(Context).Activity.LevelOf(state.Activity), state.Activity), "(guild activity)");
    }

    private void ShowGuildBuildSummary(int target)
    {
        if (Guild is not { } guild)
        {
            GuildHallSay(target, "You belong to no guild.");
            return;
        }
        GuildConstructionRules rules = GuildBuildDirectory.Rules(Context).Construction;
        GuildBuildState state = GuildBuildDirectory.State(Context, guild);
        int max = rules.DegreeMax(guild.Level);
        string degrees = string.Join(", ", GuildBuildDirectory.DegreeNames.Select((name, i) => $"{name} {state.Degrees[i]}/{max}"));
        string next = guild.Level >= rules.MaxLevel ? "It is fully built." : "Fill all four degrees to build the guild up a level.";
        GuildHallSay(target, $"{guild.Name} is a level {guild.Level} guild. {degrees}. {next} Use Economic, Industrial, Military and Agricultural Medals to raise the degrees.");
        Send(GuildBuildDirectory.Construction(Context, guild), "(guild construction)");
    }

    private void ShowGuildActivitySummary(int target)
    {
        if (Guild is not { } guild)
        {
            GuildHallSay(target, "You belong to no guild.");
            return;
        }
        GuildActivityRules rules = GuildBuildDirectory.Rules(Context).Activity;
        GuildBuildState state = GuildBuildDirectory.State(Context, guild);
        int level = rules.LevelOf(state.Activity);
        string next = level < rules.Levels.Count ? $" Level {level + 1} needs {rules.Levels[level]} activity." : "";
        GuildHallSay(target, $"{guild.Name} has {state.Activity} activity points (activity level {level}).{next} Members earn activity by playing every day, the Secret Isle and the guild tournament.");
        SendGuildActivity(guild);
    }

    private void GuildHallSay(int target, string text)
        => Send(ChatPackets.NpcDialogue(target,
        [
            (DialogueEntry.Text, 0, text),
            (DialogueEntry.Option, GuildHallOption, "Back"),
            (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
        ]), "(guild hall)");
}
