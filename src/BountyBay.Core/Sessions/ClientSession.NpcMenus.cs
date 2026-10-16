using BountyBay.Core.Data;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private readonly Dictionary<int, DialogueAction> _clientMenuOptions = [];

    private bool TalkWithClientMenu(NpcDefinition npc)
    {
        if (npc.Scene != Scene) return false;
        NpcMenu? menu = NpcMenuBook.For(Context.Settings).Find(npc.ChatSet);
        if (menu == null) return false;
        (string Key, Dictionary<string, string> Values)? greeting = NpcMenuBook.MatchText(menu, npc.Greeting);
        if (greeting == null) return false;

        foreach ((string name, string value) in greeting.Value.Values)
            Send(ChatPackets.Script(npc.Id, $"SetVar {name} {UiPackets.Token(value)}"), "(menu variable)");
        Send(ChatPackets.Script(npc.Id, $"SetVar temp_s {UiPackets.Token(Name)}"), "(menu variable)");

        _clientMenuOptions.Clear();
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.IniText, 0, greeting.Value.Key) };
        bool farewell = false;
        foreach (DialogueOption option in npc.Options)
        {
            if (option.ClientKey.StartsWith('-') && int.TryParse(option.ClientKey[1..], out int number))
            {
                _clientMenuOptions[number] = option.Action;
                entries.Add((DialogueEntry.IniOption, number, option.ClientKey));
                farewell |= option.Action == DialogueAction.Bye;
            }
            else
            {
                entries.Add((DialogueEntry.Option, (int)option.Action, option.Text));
                farewell |= option.Action == DialogueAction.Bye;
            }
        }
        if (!farewell) entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(npc.Id, entries), "(client npc menu)");
        return true;
    }

    private DialogueAction ClientMenuAction(DialogueAction action)
        => _clientMenuOptions.TryGetValue((int)action, out DialogueAction mapped) ? mapped : action;
}
