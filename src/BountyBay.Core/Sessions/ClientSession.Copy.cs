using System.Numerics;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class CopyTicket
{
    public CopyTicket(ClientSession session, CopyDefinition definition, int role, string comment)
    {
        Session = session;
        Definition = definition;
        Role = role;
        Comment = comment;
        QueuedAt = Environment.TickCount64;
    }

    public ClientSession Session { get; }
    public CopyDefinition Definition { get; }
    public int Role { get; }
    public string Comment { get; }
    public long QueuedAt { get; }
}

public static class CopyDirectory
{
    private static readonly List<CopyTicket> Tickets = [];
    private static readonly Dictionary<ClientSession, CopyDefinition> Recruits = [];

    public static IEnumerable<CopyTicket> Of(ClientSession session) => Tickets.Where(ticket => ticket.Session == session);

    public static IEnumerable<CopyTicket> For(CopyDefinition definition) => Tickets.Where(ticket => ticket.Definition == definition);

    public static void Add(CopyTicket ticket)
    {
        Tickets.RemoveAll(existing => existing.Session == ticket.Session && existing.Definition == ticket.Definition);
        Tickets.Add(ticket);
    }

    public static bool Remove(ClientSession session, CopyDefinition definition)
        => Tickets.RemoveAll(ticket => ticket.Session == session && ticket.Definition == definition) > 0;

    public static void Forget(ClientSession session)
    {
        Tickets.RemoveAll(ticket => ticket.Session == session);
        Recruits.Remove(session);
    }

    public static void Recruit(ClientSession session, CopyDefinition definition) => Recruits[session] = definition;

    public static bool StopRecruiting(ClientSession session) => Recruits.Remove(session);

    public static void Tick()
    {
        Tickets.RemoveAll(ticket => !ticket.Session.InWorld);
        foreach (CopyDefinition definition in Tickets.Select(ticket => ticket.Definition).Distinct().ToList())
        {
            List<CopyTicket> waiting = For(definition).OrderBy(ticket => ticket.QueuedAt).ToList();
            ClientSession first = waiting[0].Session;
            BattleBook book = BattleBook.For(first.Context.Settings);
            if (waiting.Count < Math.Max(1, book.CopyMinPlayers)) continue;
            foreach (CopyTicket ticket in waiting.Take(Math.Max(1, book.CopyMaxPlayers)))
            {
                Tickets.RemoveAll(other => other.Session == ticket.Session);
                ticket.Session.InstanceSendQueue();
                ticket.Session.InstanceEnter(definition);
            }
        }
    }
}

public sealed partial class ClientSession
{
    public int InstanceRole { get; private set; }

    public bool InInstanceScene => Character.BattleReturnScene != 0 && BattleRules.CopyInScene(Scene) != null && CurrentBattle == null;

    public void InstanceSendQueue()
    {
        Send(BattlePackets.CleanQueue(), "(instance queue)");
        foreach (bool sea in new[] { false, true })
        {
            int slot = 0;
            foreach (CopyTicket ticket in CopyDirectory.Of(this).Where(ticket => ticket.Definition.Sea == sea))
                Send(BattlePackets.InQueue(sea, slot++, ticket.Definition.Group, ticket.Definition.Copy), "(instance queue entry)");
        }
    }

    public void InstanceJoinQueue(int group, int copy, int role, string comment)
    {
        BattleDirectory.EnsureTicking(Context.World);
        CopyDefinition? definition = BattleRules.Copy(group, copy);
        if (definition == null)
        {
            SystemMessage("This instance is not open on this server.");
            Log.Debug(Tag, $"{Name} asked for unknown instance {group}/{copy}");
            return;
        }
        string? refusal = InstanceRefusal(definition);
        if (refusal != null)
        {
            SystemMessage(refusal);
            return;
        }
        if (CopyDirectory.Of(this).Count(ticket => ticket.Definition.Sea == definition.Sea && ticket.Definition != definition) >= Math.Max(1, BattleRules.CopyQueueSlots))
        {
            SystemMessage($"You can queue for at most {BattleRules.CopyQueueSlots} {(definition.Sea ? "sea" : "land")} instances.");
            return;
        }
        InstanceRole = role;
        CopyDirectory.Add(new CopyTicket(this, definition, role, comment));
        Log.Info(Tag, $"{Name} queues for instance {definition.Name}");
        InstanceSendQueue();
        CopyDirectory.Tick();
    }

    public void InstanceLeaveQueue(int group, int copy)
    {
        if (BattleRules.Copy(group, copy) is { } definition) CopyDirectory.Remove(this, definition);
        InstanceSendQueue();
    }

    public void InstanceSelectRole(int role)
    {
        InstanceRole = role;
        Send(BattlePackets.CloseSelectRole(), "(role chosen)");
    }

    public void InstanceQueueInfo(int group, int copy)
    {
        if (BattleRules.Copy(group, copy) is not { } definition) return;
        List<CopyTicket> waiting = CopyDirectory.For(definition).ToList();
        SystemMessage(waiting.Count == 0
            ? $"Nobody is waiting for {definition.Name}."
            : $"Waiting for {definition.Name}: {string.Join(", ", waiting.Select(ticket => ticket.Session.Name))}");
    }

    private const int CopyRowKey = 0x7E000000;

    public void InstanceShowLeftTimes()
    {
        List<object[]> rows = BattleRules.Copies
            .Select(definition => new object[] { definition.Key, definition.Name, $"{InstanceEntriesLeft(definition)}", $"{BattleRules.CopyDailyEntries}" }).ToList();
        Send(TablePackets.Clear(BattleTables.CopyLeftTimes), "(clear CopyLeftTimes)");
        Send(TablePackets.Rows(BattleTables.CopyLeftTimes, rows), "(CopyLeftTimes)");
        // dlgCopyLeftTimes shows the client's custom table 47, filled row by row with the "ut" keyword:
        // "ut <table>\b<cell>\b<cell>...", where the message's object id is the row key.
        for (int i = 0; i < rows.Count; i++)
            Send(ChatPackets.Script(CopyRowKey + i, $"ut {BattleTables.CopyLeftTimes.Id}\b" + string.Join("\b", rows[i])), "(instance row)");
    }

    public void InstanceRecruit(int group, int copy)
    {
        if (BattleRules.Copy(group, copy) is not { } definition)
        {
            SystemMessage("This instance is not open on this server.");
            return;
        }
        CopyDirectory.Recruit(this, definition);
        string text = $"{Name} is currently recruiting elite warriors for [{definition.Name}].";
        foreach (ClientSession other in Context.World.Sessions.Where(other => other.InWorld)) other.SystemMessage(text);
        Send(BattlePackets.RecruitPosted(), "(recruit posted)");
        Log.Info(Tag, $"{Name} recruits for {definition.Name}");
    }

    public void InstanceStopRecruiting()
    {
        if (CopyDirectory.StopRecruiting(this)) SystemMessage("You stopped recruiting team members.");
    }

    public void InstanceEnter(CopyDefinition definition)
    {
        if (!InWorld) return;
        string? refusal = InstanceRefusal(definition);
        if (refusal != null)
        {
            SystemMessage(refusal);
            return;
        }
        BattleEntryCount count = InstanceCount(definition);
        count.Today++;
        count.ThisWeek++;
        BattleTravelTo(definition.Scene, definition.Spawn, 0f);
        SystemMessage($"You entered {definition.Name}.");
        Log.Info(Tag, $"{Name} entered instance {definition.Name} (scene {definition.Scene})");
        Save();
    }

    public void InstanceLeave()
    {
        if (!InInstanceScene) return;
        if (Hp <= 0) SetHp(MaxHp);
        BattleTravelBack("(instance left)");
    }

    private bool InstanceKnockedOut()
    {
        CopyDefinition? definition = InInstanceScene ? BattleRules.CopyInScene(Scene) : null;
        TowerDefinition? tower = TowerHere;
        if (definition == null && tower == null) return false;
        SetHp(MaxHp);
        float[]? spawn = definition?.Spawn ?? tower?.Spawn;
        Vector3 at = spawn is { Length: >= 3 } ? new Vector3(spawn[0], spawn[1], spawn[2]) : Context.Data.Scenes.SpawnPoint(Scene);
        TeleportTo(at.X, at.Z);
        SystemMessage("You were knocked out and wake up at the entrance.");
        return true;
    }

    private string? InstanceRefusal(CopyDefinition definition)
    {
        if (CurrentBattle != null || BattleDirectory.InviteOf(this) != null) return "You cannot enter an instance during a battle.";
        if (!Context.Data.Scenes.Exists(definition.Scene)) return $"{definition.Name} is not available on this server.";
        if (InstanceEntriesLeft(definition) == 0) return $"You have no entries left today for {definition.Name}.";
        return null;
    }

    private int InstanceEntriesLeft(CopyDefinition definition)
        => BattleRules.CopyDailyEntries <= 0 ? -1 : Math.Max(0, BattleRules.CopyDailyEntries - InstanceCount(definition).Today);

    private BattleEntryCount InstanceCount(CopyDefinition definition)
    {
        string key = $"{definition.Group}/{definition.Copy}";
        if (!Character.CopyEntries.TryGetValue(key, out BattleEntryCount? count)) Character.CopyEntries[key] = count = new BattleEntryCount();
        RollCount(count);
        return count;
    }

    private bool InstanceTalk(NpcDefinition npc)
    {
        var entries = new List<(DialogueEntry, int, string)>();
        if (InInstanceScene && npc.Scene == Scene && npc.NameKey.StartsWith("jiheshi", StringComparison.OrdinalIgnoreCase))
        {
            entries.Add((DialogueEntry.Text, 0, "Do you want to leave the instance?"));
            entries.Add((DialogueEntry.Option, OptionCopyLeave, "Leave the instance"));
        }
        else if (!InInstanceScene && BattleRules.CopyByKey(npc.NameKey) is { } definition)
        {
            entries.Add((DialogueEntry.Text, 0, $"{definition.Name}. Recommended for players with at least {definition.Points} Land Battle Points."));
            entries.Add((DialogueEntry.Option, OptionCopyEnter, $"Enter {definition.Name}"));
            entries.Add((DialogueEntry.Option, OptionCopyInfo, "Remaining entries"));
        }
        else
        {
            return false;
        }
        TalkingTo = npc.Id;
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(npc.Id, entries), "(instance npc)");
        return true;
    }

    private bool HandleInstanceOption(int target, int action)
    {
        Context.Data.Npcs.TryGetValue(target, out NpcDefinition? npc);
        CopyDefinition? definition = npc == null ? null : BattleRules.CopyByKey(npc.NameKey);
        switch (action)
        {
            case OptionCopyEnter when definition != null:
                List<ClientSession> party = Team is { } team && team.Leader == this
                    ? team.Members.Where(member => member.InWorld && member.Scene == Scene).ToList()
                    : [this];
                foreach (ClientSession member in party) member.InstanceEnter(definition);
                return true;
            case OptionCopyInfo when definition != null:
                int left = InstanceEntriesLeft(definition);
                SystemMessage(left < 0 ? $"{definition.Name} has no entry limit." : $"{definition.Name}: {left} entries left today.");
                return true;
            case OptionCopyLeave:
                InstanceLeave();
                return true;
            case OptionCopyEnter or OptionCopyInfo:
                return true;
            default:
                return false;
        }
    }
}
