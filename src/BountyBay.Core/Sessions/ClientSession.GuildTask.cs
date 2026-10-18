using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int GuildHallTaskCommerceOption = 468;
    public const int GuildHallTaskIndustryOption = 469;
    public const int GuildHallTaskSubmitOption = 470;
    public const int GuildHallTaskAbandonOption = 471;
    public const int GuildHallTaskOption = 472;

    private static readonly int[] GuildTaskViews = [ViewId.BagItems, ViewId.BagArticle, ViewId.Cargo];

    private GuildTaskRules GuildTaskData => GuildBuildDirectory.Rules(Context).Construction.Tasks;

    private string GuildTaskLabel(string item) => Context.Data.Items.Goods.GetValueOrDefault(item)?.Label ?? item;

    private bool HandleGuildTaskOption(int target, int option)
    {
        switch (option)
        {
            case GuildHallTaskCommerceOption or GuildHallTaskIndustryOption:
                TakeGuildTask(target, option == GuildHallTaskCommerceOption ? 0 : 1);
                return true;
            case GuildHallTaskOption:
                ShowGuildTaskMenu(target);
                return true;
            case GuildHallTaskSubmitOption:
                OpenGuildTaskHandIn(target);
                return true;
            case GuildHallTaskAbandonOption:
                Character.GuildTask = null;
                SetProperty("HaveConsTask", 0);
                Save();
                GuildHallSay(target, "You gave up your guild construction quest.");
                return true;
            default:
                return false;
        }
    }

    private void AddGuildTaskOptions(List<(DialogueEntry, int, string)> entries)
        => entries.Add((DialogueEntry.Option, GuildHallTaskOption, "Guild construction quests"));

    private void ShowGuildTaskMenu(int target)
    {
        var entries = new List<(DialogueEntry, int, string)>();
        if (Character.GuildTask is { } task)
        {
            entries.Add((DialogueEntry.Text, 0, $"You are collecting {task.Count} {GuildTaskLabel(task.Item)} for the guild; {task.Submitted} handed in so far."));
            entries.Add((DialogueEntry.Option, GuildHallTaskSubmitOption, $"Hand in {GuildTaskLabel(task.Item)}"));
            entries.Add((DialogueEntry.Option, GuildHallTaskAbandonOption, "Give up the quest"));
        }
        else
        {
            entries.Add((DialogueEntry.Text, 0, "The guild ministers need goods to build up the guild's commerce and industry."));
            entries.Add((DialogueEntry.Option, GuildHallTaskCommerceOption, "Commercialization construction quest"));
            entries.Add((DialogueEntry.Option, GuildHallTaskIndustryOption, "Industrialization construction quest"));
        }
        entries.Add((DialogueEntry.Option, GuildHallOption, "Back"));
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(guild construction quests)");
    }

    private void TakeGuildTask(int target, int kind)
    {
        if (Guild is not { } guild)
        {
            GuildHallSay(target, "You belong to no guild.");
            return;
        }
        GuildTaskRules rules = GuildTaskData;
        List<string> goods = rules.Goods(kind);
        if (goods.Count == 0)
        {
            GuildHallSay(target, "The guild needs nothing right now.");
            return;
        }
        string item = goods[Random.Shared.Next(goods.Count)];
        Character.GuildTask = new GuildTaskRecord { Kind = kind, Item = item, Count = rules.Count };
        SetProperty("HaveConsTask", 1);
        Save();
        string minister = kind == 0 ? "Guild Commercial Minister" : "Guild Industry Minister";
        GuildHallSay(target, $"Collect {rules.Count} {GuildTaskLabel(item)} for the {minister} of {guild.Name}, then hand them in here.");
        Log.Info(Tag, $"{Name} takes a guild construction quest: {rules.Count} x {item}");
    }

    private void OpenGuildTaskHandIn(int target)
    {
        if (Character.GuildTask is not { } task) return;
        if (Guild == null)
        {
            GuildHallSay(target, "You belong to no guild.");
            return;
        }
        Send(UiPackets.CloseMenu(), "(close guild hall menu)");
        Send(UiPackets.Custom($"ShowConsTaskItemByName {string.Join(',', GuildTaskViews)} {task.Item}"), "(guild task hand-in)");
    }

    public void ReferGuildTaskItems(List<int> numbers)
    {
        if (Character.GuildTask is not { } task || Guild is not { } guild)
        {
            SystemMessage("You have no guild construction quest.", SystemMessageKind.Prompt);
            return;
        }
        int handed = 0;
        for (int i = 0; i + 1 < numbers.Count && task.Submitted < task.Count; i += 2)
        {
            if (!GuildTaskSlot(numbers[i], numbers[i + 1], task.Item, out int view, out int index)) continue;
            int amount = Math.Min(OwnSlots(view)![index].Amount, task.Count - task.Submitted);
            TakeFrom(view, index, amount, "(guild task goods handed in)");
            if (view == ViewId.Cargo) RefreshLoad();
            task.Submitted += amount;
            handed += amount;
        }
        if (handed == 0)
        {
            SystemMessage($"Put {GuildTaskLabel(task.Item)} into the window to hand them in.", SystemMessageKind.Prompt);
            return;
        }
        if (task.Submitted < task.Count)
        {
            Save();
            SystemMessage($"You handed in {handed} {GuildTaskLabel(task.Item)}: {task.Submitted}/{task.Count}.");
            return;
        }
        GuildTaskRules rules = GuildTaskData;
        Character.GuildTask = null;
        SetProperty("HaveConsTask", 0);
        if (task.Kind == 0) guild.Commerce += rules.Degree;
        else guild.Industry += rules.Degree;
        if (guild.Member(Name) is { } member) member.Contribution += rules.Contribution;
        GuildDirectory.Save();
        GuildBuildDirectory.AddDegree(Context, guild, task.Kind, rules.Degree, Name);
        GuildBuildDirectory.AddActivity(Context, guild, rules.Activity, $"{Name} finished a construction quest");
        Save();
        SystemMessage($"Guild construction quest completed: the {GuildBuildDirectory.DegreeNames[task.Kind]} degree of {guild.Name} rises by {rules.Degree} "
            + $"and your contribution by {rules.Contribution}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} completes a guild construction quest for {guild.Name}");
    }

    private bool GuildTaskSlot(int first, int second, string item, out int view, out int index)
    {
        foreach ((int v, int i) in new[] { (first, second), (second, first) })
        {
            if (GuildTaskViews.Contains(v) && OwnSlots(v) is { } slots && slots.TryGetValue(i, out ItemStack? stack) && stack.Name == item)
            {
                view = v;
                index = i;
                return true;
            }
        }
        view = index = 0;
        return false;
    }
}
