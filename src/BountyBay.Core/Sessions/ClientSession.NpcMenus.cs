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
        var farewells = new List<(DialogueEntry, int, string)>();
        foreach (DialogueOption option in npc.Options)
        {
            List<(DialogueEntry, int, string)> into = option.Action == DialogueAction.Bye ? farewells : entries;
            if (option.ClientKey.StartsWith('-') && int.TryParse(option.ClientKey[1..], out int number))
            {
                _clientMenuOptions[number] = option.Action;
                into.Add((DialogueEntry.IniOption, number, option.ClientKey));
            }
            else
            {
                into.Add((DialogueEntry.Option, (int)option.Action, option.Text));
            }
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
        if (farewells.Count == 0) farewells.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        entries.AddRange(farewells);
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
