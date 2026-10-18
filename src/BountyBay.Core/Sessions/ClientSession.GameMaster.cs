using System.Runtime.CompilerServices;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class HelpTicket
{
    public string Player { get; init; } = "";
    public string Type { get; set; } = "";
    public string Gm { get; set; } = "";
    public DateTime Opened { get; init; } = DateTime.Now;
    public List<string> Lines { get; } = [];
}

public sealed class HelpDesk
{
    private static readonly ConditionalWeakTable<ServerContext, HelpDesk> Desks = new();

    public static HelpDesk Of(ServerContext context) => Desks.GetValue(context, _ => new HelpDesk());

    public Dictionary<string, HelpTicket> Tickets { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed partial class ClientSession
{
    private const string RegardRequest = "#EXTE#regard#";
    private const string ConnectRequest = "#EXTE#con#";
    private const string VoteResult = "#EXTE#voteRes#";

    private HelpDesk Desk => HelpDesk.Of(Context);

    public bool IsGameMaster
        => Context.Settings.GameMasters.Any(name => string.Equals(name, Account, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Name, StringComparison.OrdinalIgnoreCase));

    public bool MayUseGmCommands => IsGameMaster || Context.Settings.GameMasters.Count == 0;

    private IEnumerable<ClientSession> OnlineGameMasters
        => Context.World.Sessions.Where(session => session.InWorld && session.IsGameMaster);

    public void ApplyGameMasterProperty()
    {
        if (PropertyTable.Has("GameMaster")) Properties["GameMaster"] = IsGameMaster && !_gmHidden ? 1 : 0;
    }

    private bool _gmHidden;

    public void OnServiceChat(string text)
    {
        if (text.StartsWith(RegardRequest, StringComparison.Ordinal))
        {
            string types = string.Join(';', Context.Settings.HelpTypes.Select(UiPackets.Line).Where(type => type.Length > 0));
            Send(ChatPackets.ServiceChat("GM", $"#EXTE#regardRes#1#{types}#"), "(help types)");
            return;
        }
        if (text.StartsWith(ConnectRequest, StringComparison.Ordinal))
        {
            OpenHelpTicket(text[ConnectRequest.Length..].Trim());
            return;
        }
        if (text.StartsWith(VoteResult, StringComparison.Ordinal))
        {
            string vote = text[VoteResult.Length..].Trim();
            Log.Info(Tag, $"{Name} rates the Game Master: {vote}");
            foreach (ClientSession gm in OnlineGameMasters)
                gm.SystemMessage($"[Help] {Name} rated the help: {vote}", SystemMessageKind.Information);
            return;
        }
        if (text == "BYE")
        {
            CloseHelpTicket(byPlayer: true);
            return;
        }
        if (text.StartsWith("#EXTE#", StringComparison.Ordinal))
        {
            Log.Debug(Tag, $"{Name} client extension message: {text}");
            return;
        }
        if (!Desk.Tickets.TryGetValue(Name, out HelpTicket? ticket)) ticket = OpenHelpTicket("");
        if (ticket.Lines.Count == 0 && string.Equals(text.Trim(), ticket.Type, StringComparison.Ordinal))
        {
            ticket.Lines.Add(text);
            return;
        }
        ticket.Lines.Add(text);
        Log.Info(Tag, $"{Name} to GM: {text}");
        var gms = OnlineGameMasters.Where(gm => ticket.Gm.Length == 0 || string.Equals(gm.Name, ticket.Gm, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (ClientSession gm in gms) gm.SystemMessage($"[Help] {Name}: {text}", SystemMessageKind.Information);
        if (gms.Count == 0) Send(ChatPackets.ServiceChat("GM", "No Game Master is online right now. Your message was saved and will be read."), "(no gm online)");
    }

    private HelpTicket OpenHelpTicket(string type)
    {
        if (!Desk.Tickets.TryGetValue(Name, out HelpTicket? ticket))
        {
            ticket = new HelpTicket { Player = Name };
            Desk.Tickets[Name] = ticket;
        }
        if (type.Length > 0) ticket.Type = type;
        Log.Info(Tag, $"{Name} asks for help ({(ticket.Type.Length > 0 ? ticket.Type : "no type")})");
        var gms = OnlineGameMasters.ToList();
        foreach (ClientSession gm in gms)
            gm.SystemMessage($"[Help] {Name} asks for help: {(ticket.Type.Length > 0 ? ticket.Type : "other")}. Reply with /gm r {Name} <text>, end with /gm close {Name}.", SystemMessageKind.Prompt);
        Send(ChatPackets.ServiceChat("GM", gms.Count > 0
            ? "Your request was sent to a Game Master. Please describe your problem here."
            : "No Game Master is online right now. Describe your problem here and it will be read later."), "(help opened)");
        return ticket;
    }

    private void CloseHelpTicket(bool byPlayer)
    {
        if (!Desk.Tickets.Remove(Name, out HelpTicket? ticket)) return;
        Log.Info(Tag, $"{Name}'s help request closed{(byPlayer ? " by the player" : "")}");
        foreach (ClientSession gm in OnlineGameMasters.Where(gm => ticket.Gm.Length == 0 || string.Equals(gm.Name, ticket.Gm, StringComparison.OrdinalIgnoreCase)))
            gm.SystemMessage($"[Help] {Name}'s request is closed{(byPlayer ? " (the player left the help chat)" : "")}.", SystemMessageKind.Information);
    }

    public void GmCommand(string[] words)
    {
        string action = words.Length > 1 ? words[1].ToLowerInvariant() : "";
        switch (action)
        {
            case "list":
                if (Desk.Tickets.Count == 0)
                {
                    SystemMessage("[Help] No open requests.");
                    return;
                }
                foreach (HelpTicket ticket in Desk.Tickets.Values.OrderBy(ticket => ticket.Opened))
                    SystemMessage($"[Help] {ticket.Player} ({(ticket.Type.Length > 0 ? ticket.Type : "other")}, {ticket.Opened:HH:mm})"
                        + $"{(ticket.Gm.Length > 0 ? $" taken by {ticket.Gm}" : "")}: {(ticket.Lines.Count > 0 ? ticket.Lines[^1] : "-")}");
                return;
            case "take" when words.Length >= 3:
                if (!Desk.Tickets.TryGetValue(words[2], out HelpTicket? taken))
                {
                    SystemMessage($"[Help] {words[2]} has no open request.");
                    return;
                }
                taken.Gm = Name;
                SystemMessage($"[Help] You took {taken.Player}'s request. Earlier messages: {string.Join(" | ", taken.Lines.TakeLast(5))}");
                Context.World.FindPlayer(taken.Player)?.Send(ChatPackets.ServiceChat(Name, $"Game Master {Name} is looking at your request."), "(gm took request)");
                return;
            case "r" or "reply" when words.Length >= 4:
                ClientSession? player = Context.World.FindPlayer(words[2]);
                if (player == null)
                {
                    SystemMessage($"[Help] {words[2]} is not online.");
                    return;
                }
                string reply = string.Join(' ', words.Skip(3));
                if (player.Desk.Tickets.TryGetValue(player.Name, out HelpTicket? open) && open.Gm.Length == 0) open.Gm = Name;
                player.Send(ChatPackets.ServiceChat(Name, reply), "(gm reply)");
                SystemMessage($"[Help] To {player.Name}: {reply}");
                Log.Info(Tag, $"GM {Name} to {player.Name}: {reply}");
                return;
            case "close" when words.Length >= 3:
                ClientSession? helped = Context.World.FindPlayer(words[2]);
                if (helped == null)
                {
                    if (Desk.Tickets.Remove(words[2])) SystemMessage($"[Help] {words[2]}'s request is closed.");
                    else SystemMessage($"[Help] {words[2]} has no open request.");
                    return;
                }
                helped.Send(ChatPackets.ServiceChat(Name, "BYE"), "(gm ends help)");
                helped.Send(ChatPackets.ServiceChat(Name, "#EXTE#gmVote#Good;Normal;Bad"), "(gm rating window)");
                helped.CloseHelpTicket(byPlayer: false);
                return;
            case "on" or "off" when IsGameMaster:
                _gmHidden = action == "off";
                ApplyGameMasterProperty();
                SetProperty("GameMaster", Properties.Int("GameMaster"), visibleToOthers: true);
                SystemMessage(_gmHidden ? "Game Master mode off." : "Game Master mode on.");
                return;
            default:
                SystemMessage("[Help] /gm list, /gm take <player>, /gm r <player> <text>, /gm close <player>, /gm on|off");
                return;
        }
    }
}
