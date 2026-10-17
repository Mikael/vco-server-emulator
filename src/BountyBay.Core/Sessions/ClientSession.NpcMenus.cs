using BountyBay.Core.Data;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int ClientMenuCancel = 45099;

    private readonly Dictionary<int, DialogueAction> _clientMenuOptions = [];
    private readonly Dictionary<int, string> _clientMenuTalk = [];

    private bool TalkWithClientMenu(NpcDefinition npc)
    {
        if (npc.Scene != Scene) return false;
        NpcMenu? menu = NpcMenuBook.For(Context.Settings).Find(npc.ChatSet);
        if (menu == null) return false;
        (string Key, Dictionary<string, string> Values)? greeting = NpcMenuBook.MatchText(menu, npc.Greeting);
        if (greeting == null && menu.Entries.FirstOrDefault(entry => entry.Key == "12" && entry.Vars.Count == 0) is { } standard)
            greeting = (standard.Key, []);
        if (greeting == null) return false;

        foreach ((string name, string value) in greeting.Value.Values)
            Send(ChatPackets.Script(npc.Id, $"SetVar {name} {UiPackets.Token(value)}"), "(menu variable)");
        Send(ChatPackets.Script(npc.Id, $"SetVar temp_s {UiPackets.Token(Name)}"), "(menu variable)");
        SendClientMenu(npc, menu, greeting.Value.Key);
        return true;
    }

    private void SendClientMenu(NpcDefinition npc, NpcMenu menu, string textKey)
    {
        _clientMenuOptions.Clear();
        _clientMenuTalk.Clear();
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.IniText, 0, textKey) };
        bool farewell = false;
        foreach (DialogueOption option in npc.Options)
        {
            if (option.ClientKey.StartsWith('-') && int.TryParse(option.ClientKey[1..], out int number))
            {
                _clientMenuOptions[number] = option.Action;
                entries.Add((DialogueEntry.IniOption, number, option.ClientKey));
            }
            else
            {
                entries.Add((DialogueEntry.Option, (int)option.Action, option.Text));
            }
            farewell |= option.Action == DialogueAction.Bye;
        }
        HashSet<int> taken = npc.Options.Select(option => (int)option.Action).ToHashSet();
        HashSet<string> answers = menu.Entries.Where(entry => !entry.Key.StartsWith('-')).Select(entry => entry.Key).ToHashSet();
        foreach (NpcMenuEntry entry in menu.Entries)
        {
            if (!entry.Key.StartsWith('-') || !int.TryParse(entry.Key[1..], out int number) || number == ClientMenuCancel) continue;
            string answer = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_clientMenuOptions.ContainsKey(number) || taken.Contains(number) || !answers.Contains(answer) || answer == textKey) continue;
            _clientMenuTalk[number] = answer;
            entries.Add((DialogueEntry.IniOption, number, entry.Key));
        }
        if (!farewell) entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(npc.Id, entries), "(client npc menu)");
    }

    private bool ShowClientMenuTalk(int target, int option)
    {
        if (!_clientMenuTalk.TryGetValue(option, out string? answer)) return false;
        if (!Context.Data.Npcs.TryGetValue(target, out NpcDefinition? npc)) return false;
        NpcMenu? menu = NpcMenuBook.For(Context.Settings).Find(npc.ChatSet);
        if (menu == null) return false;
        SendClientMenu(npc, menu, answer);
        return true;
    }

    private DialogueAction ClientMenuAction(DialogueAction action)
        => _clientMenuOptions.TryGetValue((int)action, out DialogueAction mapped) ? mapped : action;
}
