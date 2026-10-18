using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Item exchanges at exchange NPCs (services.json "Exchanges", keyed by the NPC's NameKey), e.g. the Seville Material
// Exchange Staff: "Exchange 50 Blood Rose Fragments and some silver coins ... for a Blood Rose Stone", "3 Gems of the
// similar grade can be exchanged for a higher grade Gem". Groups -> pages of six exchanges -> one exchange per click.
public sealed partial class ClientSession
{
    public static class ExchangeOption
    {
        public const int Open = 620;
        public const int FirstGroup = 621;
        public const int LastGroup = 627;
        public const int NextPage = 628;
        public const int FirstEntry = 630;
        public const int LastEntry = 635;
        public const int Last = 639;
    }

    private const int ExchangesPerPage = ExchangeOption.LastEntry - ExchangeOption.FirstEntry + 1;

    private int _exchangeGroup = -1;
    private int _exchangePage;

    private List<ServiceExchangeGroup>? ExchangesOf(int target)
        => Context.Data.Npcs.TryGetValue(target, out NpcDefinition? npc) && Services.Exchanges.TryGetValue(npc.NameKey, out var groups) ? groups : null;

    private bool HandleExchangeOption(int target, int option)
    {
        if (option is < ExchangeOption.Open or > ExchangeOption.Last) return false;
        if (ExchangesOf(target) is not { } groups) return false;
        switch (option)
        {
            case ExchangeOption.Open:
                ShowExchangeGroups(target, groups);
                break;
            case >= ExchangeOption.FirstGroup and <= ExchangeOption.LastGroup when option - ExchangeOption.FirstGroup < groups.Count:
                _exchangeGroup = option - ExchangeOption.FirstGroup;
                _exchangePage = 0;
                ShowExchangePage(target, groups);
                break;
            case ExchangeOption.NextPage when _exchangeGroup >= 0 && _exchangeGroup < groups.Count:
                _exchangePage++;
                if (_exchangePage * ExchangesPerPage >= groups[_exchangeGroup].Entries.Count) _exchangePage = 0;
                ShowExchangePage(target, groups);
                break;
            case >= ExchangeOption.FirstEntry and <= ExchangeOption.LastEntry when _exchangeGroup >= 0 && _exchangeGroup < groups.Count:
                int index = _exchangePage * ExchangesPerPage + option - ExchangeOption.FirstEntry;
                if (index < groups[_exchangeGroup].Entries.Count) Exchange(groups[_exchangeGroup].Entries[index]);
                ShowExchangePage(target, groups);
                break;
            default:
                CloseNpcDialogue();
                break;
        }
        return true;
    }

    private void ShowExchangeGroups(int target, List<ServiceExchangeGroup> groups)
    {
        if (groups.Count == 1)
        {
            _exchangeGroup = 0;
            _exchangePage = 0;
            ShowExchangePage(target, groups);
            return;
        }
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, "What would you like to exchange?") };
        for (int i = 0; i < groups.Count && ExchangeOption.FirstGroup + i <= ExchangeOption.LastGroup; i++)
            entries.Add((DialogueEntry.Option, ExchangeOption.FirstGroup + i, groups[i].Title));
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(exchange groups)");
    }

    private void ShowExchangePage(int target, List<ServiceExchangeGroup> groups)
    {
        ServiceExchangeGroup group = groups[_exchangeGroup];
        int pages = Math.Max(1, (group.Entries.Count + ExchangesPerPage - 1) / ExchangesPerPage);
        string page = pages > 1 ? $" (page {_exchangePage + 1} of {pages})" : "";
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, $"{group.Title}{page}. Choose an exchange.") };
        for (int i = 0; i < ExchangesPerPage; i++)
        {
            int index = _exchangePage * ExchangesPerPage + i;
            if (index >= group.Entries.Count) break;
            entries.Add((DialogueEntry.Option, ExchangeOption.FirstEntry + i, ExchangeText(group.Entries[index])));
        }
        if (pages > 1) entries.Add((DialogueEntry.Option, ExchangeOption.NextPage, "More"));
        entries.Add((DialogueEntry.Option, groups.Count > 1 ? ExchangeOption.Open : (int)DialogueAction.Bye, groups.Count > 1 ? "Back" : "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(exchange list)");
    }

    private string ExchangeLabel(string item) => Context.Data.Items.Goods.GetValueOrDefault(item)?.Label ?? item;

    private string ExchangeText(ServiceExchange exchange)
    {
        string give = string.Join(", ", exchange.Give.Select(part => $"{part.Amount} {ExchangeLabel(part.Name)}"));
        if (exchange.Fee > 0) give += $" and {exchange.Fee:N0} silver";
        string get = exchange.Amount > 1 ? $"{exchange.Amount} {ExchangeLabel(exchange.Get)}" : ExchangeLabel(exchange.Get);
        return $"{give} for {get}";
    }

    private void Exchange(ServiceExchange exchange)
    {
        CompoundIngredient? missing = exchange.Give.FirstOrDefault(part => BagTotal(part.Name) < part.Amount);
        if (missing != null)
        {
            SystemMessage($"You need {missing.Amount} {ExchangeLabel(missing.Name)}.", SystemMessageKind.Prompt);
            return;
        }
        if (Money < exchange.Fee)
        {
            SystemMessage($"The exchange costs {exchange.Fee:N0} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        var taken = new List<ItemStack>();
        foreach (CompoundIngredient part in exchange.Give) taken.AddRange(TakeFromBags(part.Name, part.Amount));
        if (!ReceiveIntoBag(exchange.Get, Context.Data.PhotoOf(exchange.Get), exchange.Amount, "(exchanged)"))
        {
            foreach (ItemStack stack in taken) ReceiveIntoBag(stack.Name, stack.Photo, stack.Amount, "(exchange returned)");
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        if (exchange.Fee > 0) SetMoney(Money - exchange.Fee);
        RefreshLoad();
        Save();
        SystemMessage($"You received {(exchange.Amount > 1 ? $"{exchange.Amount} " : "")}{ExchangeLabel(exchange.Get)}.");
        Log.Info(Tag, $"{Name} exchanges for {exchange.Amount} x {exchange.Get}");
    }
}
