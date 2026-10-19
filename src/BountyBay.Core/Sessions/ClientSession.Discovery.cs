using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Discovery on the high sea. CLIENT: the ship command panel's "Discovery" (IDName00006828, only on the ocean) runs a progress
// bar and then sends "explore" (UILogic 0x102b958d). A find is announced with `Found <table> <id>` (wn_3dwnd 0x102144d0: the
// popup with the picture and text of ini\window\<table>.ini), and the logbook window lists the rows of the tables
// finding_animal / finding_conch / finding_nature / finding_plant / finding_relic. The record you bring home for the
// Adventurer Alliance quests is the "<name> Discovery Logbook" item. GUESS: the odds of a find and the spots without a quest
// (see discoveries.json).
public sealed partial class ClientSession
{
    private static readonly string[] DiscoveryCategories = ["animal", "conch", "nature", "plant", "relic"];

    public static readonly TableDefinition[] DiscoveryTables =
        DiscoveryCategories.Select((category, index) => new TableDefinition((byte)(101 + index), "finding_" + category, [new("Id", PropertyType.U32)])).ToArray();

    private DiscoveryBook Discoveries => DiscoveryBook.For(Context.Settings);

    private List<int> Discovered(string table)
    {
        if (!Character.Discoveries.TryGetValue(table, out List<int>? ids)) Character.Discoveries[table] = ids = [];
        return ids;
    }

    public void SendDiscoveries()
    {
        foreach (TableDefinition table in DiscoveryTables)
        {
            List<int> ids = Discovered(table.Name);
            if (ids.Count > 0) Send(TablePackets.Rows(table, ids.Select(id => new object[] { id }).ToList()), $"({table.Name})");
        }
    }

    public void Explore()
    {
        if (!BboAtOcean || Room != 0 || Mover == null)
        {
            SystemMessage("You can only search for discoveries on the high sea.", SystemMessageKind.Prompt);
            return;
        }
        DiscoveryBook book = Discoveries;
        (float x, float z) = Mover.Now();
        DiscoveryEntry? found = book.Entries.FirstOrDefault(entry => entry.Spots.Any(spot => spot.Scene == Scene
            && (spot.X - x) * (spot.X - x) + (spot.Z - z) * (spot.Z - z) <= spot.Radius * spot.Radius)
            && (!Discovered(entry.Table).Contains(entry.Id) || QuestWantsLogbook(entry)));
        if (found == null)
        {
            List<string> wanted = WantedLogbooks().ToList();
            if (wanted.Count > 0 && Random.Shared.Next(100) < book.QuestPercent)
            {
                string logbook = wanted[Random.Shared.Next(wanted.Count)];
                found = book.Entries.FirstOrDefault(entry => entry.Logbook == logbook);
                if (found == null)
                {
                    // A quest discovery the logbook window has no entry for: just the logbook.
                    if (GiveItem(new ItemStack { Name = logbook, Photo = Context.Data.PhotoOf(logbook), Amount = 1 }, "(discovery logbook)"))
                        ItemObtained(logbook, 1, "recorded");
                    Save();
                    return;
                }
            }
        }
        if (found == null && Random.Shared.Next(100) < book.FindPercent)
        {
            List<DiscoveryEntry> open = book.Entries.Where(entry => entry.Spots.Count == 0 && !Discovered(entry.Table).Contains(entry.Id)).ToList();
            if (open.Count > 0)
            {
                // Lower grades turn up more often: weight 11 - grade.
                int total = open.Sum(entry => Math.Max(1, 11 - entry.Grade));
                int roll = Random.Shared.Next(total);
                foreach (DiscoveryEntry entry in open)
                {
                    roll -= Math.Max(1, 11 - entry.Grade);
                    if (roll < 0)
                    {
                        found = entry;
                        break;
                    }
                }
            }
        }
        if (found == null)
        {
            SystemMessage("You searched the waters around you but found nothing worth recording.", SystemMessageKind.Information);
            return;
        }
        Discover(found);
    }

    // Discovery logbooks the active quests ask for and the bag does not hold yet.
    private IEnumerable<string> WantedLogbooks()
        => Character.Quests.Select(progress => QuestBook.Find(progress.Id)).Where(quest => quest != null)
            .SelectMany(quest => quest!.Targets.Where(target => target.Kind == QuestKind.Bring).Select(target => target.Key).Append(quest.Item))
            .Where(key => key.Length > 0 && ItemLabel(key).EndsWith("Discovery Logbook", StringComparison.Ordinal) && BagTotal(key) == 0)
            .Distinct();

    private bool QuestWantsLogbook(DiscoveryEntry entry)
        => entry.Logbook.Length > 0 && BagTotal(entry.Logbook) == 0 && Character.Quests.Any(progress => QuestBook.Find(progress.Id) is { } quest
            && (quest.Item == entry.Logbook || quest.Targets.Any(target => target.Kind == QuestKind.Bring && target.Key == entry.Logbook)));

    private void Discover(DiscoveryEntry entry)
    {
        List<int> ids = Discovered(entry.Table);
        bool fresh = !ids.Contains(entry.Id);
        if (fresh)
        {
            ids.Add(entry.Id);
            if (DiscoveryTables.FirstOrDefault(table => table.Name == entry.Table) is { } table)
                Send(TablePackets.Rows(table, [[entry.Id]]), "(new discovery)");
            Send(UiPackets.Custom($"Found {entry.Table} {entry.Id}"), "(discovery popup)");
            GainReputation(5 * Math.Max(1, entry.Grade));
        }
        if (entry.Logbook.Length > 0 && (fresh || QuestWantsLogbook(entry)))
        {
            if (GiveItem(new ItemStack { Name = entry.Logbook, Photo = Context.Data.PhotoOf(entry.Logbook), Amount = 1 }, "(discovery logbook)"))
                ItemObtained(entry.Logbook, 1, "recorded");
            else SystemMessage("Your bag is full: there is no room for the discovery logbook.");
        }
        else if (!fresh) SystemMessage($"You found the {entry.Name} again.", SystemMessageKind.Information);
        Save();
        Log.Info(Tag, $"{Name} discovers {entry.Table} {entry.Id} ({entry.Name}) on ocean {Scene}");
    }
}
