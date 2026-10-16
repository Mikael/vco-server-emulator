using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed class Colony
{
    public string Guild { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public int Style { get; set; }
    public DateTime Founded { get; set; }
    public int Spent { get; set; }
    public int Bonus { get; set; }
    public Dictionary<int, List<int>> Routes { get; set; } = [];

    public List<int> Levels(ColonyRoute route)
    {
        if (!Routes.TryGetValue(route.Type, out List<int>? levels)) Routes[route.Type] = levels = [];
        while (levels.Count < route.Buttons.Count) levels.Add(0);
        return levels;
    }

    public int RouteTotal(int type) => Routes.TryGetValue(type, out List<int>? levels) ? levels.Sum() : 0;
}

public sealed class ColonyStore
{
    public List<Colony> Colonies { get; set; } = [];
}

public static class ColonyDirectory
{
    private static ColonyStore? _store;
    private static string _path = "";

    public static ColonyData Rules(ServerContext context) => ColonyData.For(context.Settings);

    public static Colony? Of(ServerContext context, string guild)
        => guild.Length == 0 ? null : Store(context).Colonies.FirstOrDefault(colony => GuildDirectory.Same(colony.Guild, guild));

    public static int Contribution(Guild guild) => guild.Members.Sum(member => member.Contribution);

    public static int Level(ServerContext context, Guild guild)
    {
        ColonyData rules = Rules(context);
        int contribution = Contribution(guild);
        int union = UnionContribution(context, guild);
        int level = 0;
        for (int index = 0; index < rules.LevelContribution.Count; index++)
        {
            int unionNeeded = index < rules.LevelUnionContribution.Count ? rules.LevelUnionContribution[index] : 0;
            if (contribution >= rules.LevelContribution[index] && union >= unionNeeded) level = index + 1;
            else break;
        }
        return Math.Max(1, level);
    }

    public static int Points(ServerContext context, Guild guild, Colony colony)
        => Math.Max(0, Level(context, guild) * Rules(context).PointsPerLevel + colony.Bonus - colony.Spent);

    public static string Describe(ClientSession session)
    {
        ColonyData rules = Rules(session.Context);
        Guild? guild = session.Guild;
        if (guild == null) return "Colonial Ports are founded and developed by guilds. Join a guild first.";
        Colony? colony = Of(session.Context, guild.Name);
        if (colony == null)
            return $"Your guild has no Colonial Port yet. The guild leader can found one for {rules.FoundCost} silver coins from the guild fund.";
        return $"The Colonial Port {colony.Name} ({StyleName(rules, colony.Style)}) of {guild.Name} is of level {Level(session.Context, guild)} "
            + $"and has {Points(session.Context, guild, colony)} development points left.";
    }

    public static void OfferFounding(ClientSession session, int npc)
    {
        string obstacle = FoundingObstacle(session);
        if (obstacle.Length > 0)
        {
            session.SayGuildWar(npc, obstacle, ClientSession.ColonyMenuOption);
            return;
        }
        session.ColonyStyleChosen = -1;
        session.GuildWarCityName = GuildWarData.CityOf(session.Context.Settings, session.Scene);
        session.Send(ColonyPackets.ChooseStyle(), "(choose colony style)");
    }

    public static void ChooseStyle(ClientSession session, int style)
    {
        ColonyData rules = Rules(session.Context);
        string obstacle = FoundingObstacle(session);
        if (obstacle.Length > 0)
        {
            session.SystemMessage(obstacle);
            return;
        }
        if (style < 0 || style >= rules.Styles.Count)
        {
            session.SystemMessage("Choose a style for your Colonial Port.");
            return;
        }
        session.ColonyStyleChosen = style;
        session.Send(ColonyPackets.InputCityName(), "(name the colony)");
    }

    public static void SetName(ClientSession session, string name)
    {
        ServerContext context = session.Context;
        ColonyData rules = Rules(context);
        int style = session.ColonyStyleChosen;
        session.ColonyStyleChosen = -1;
        if (name.Length == 0 || style < 0)
        {
            if (style >= 0) session.SystemMessage("You did not found a Colonial Port.");
            return;
        }
        string obstacle = FoundingObstacle(session);
        if (obstacle.Length > 0)
        {
            session.SystemMessage(obstacle);
            return;
        }
        if (name.Length > rules.NameLength || name.Any(letter => letter <= ' ' || letter == 0x7f || letter == '%' || letter == ','))
        {
            session.SystemMessage($"Please enter up to {rules.NameLength} letters or numbers. No spaces.");
            return;
        }
        if (Store(context).Colonies.Any(colony => GuildDirectory.Same(colony.Name, name)))
        {
            session.SystemMessage($"A Colonial Port named {name} already exists.");
            return;
        }
        Guild guild = session.Guild!;
        guild.Fund -= rules.FoundCost;
        GuildDirectory.Save();
        var colony = new Colony
        {
            Guild = guild.Name, Name = name, Style = style, Founded = DateTime.Now,
            City = session.GuildWarCityName.Length > 0 ? session.GuildWarCityName : GuildWarData.CityOf(context.Settings, session.Scene),
        };
        Store(context).Colonies.Add(colony);
        Save();
        GuildDirectory.Refresh(session);
        foreach (ClientSession member in context.World.Sessions.Where(other => other.InWorld && guild.Member(other.Name) != null).ToList())
            member.SystemMessage($"{session.Name} founded the Colonial Port {name} ({StyleName(rules, style)}) for the guild.");
        Log.Info(session.Tag, $"{session.Name} founds the colony {name} for {guild.Name}");
    }

    public static void ShowRoute(ClientSession session, int type, bool update)
    {
        ServerContext context = session.Context;
        Guild? guild = session.Guild;
        Colony? colony = guild == null ? null : Of(context, guild.Name);
        ColonyRoute? route = Rules(context).Route(type);
        if (guild == null || colony == null || route == null)
        {
            session.SystemMessage("Your guild has no Colonial Port.");
            return;
        }
        session.ColonyRouteShown = type;
        List<int> levels = colony.Levels(route);
        bool developer = guild.Member(session.Name)?.Duty >= Rules(context).DevelopDuty;
        int points = Points(context, guild, colony);
        var buttons = route.Buttons.Select((button, index) =>
        {
            bool met = button.RequiresIndex < 0 || (button.RequiresIndex < levels.Count && levels[button.RequiresIndex] >= button.RequiresLevel);
            return (developer && met && levels[index] < button.Max, levels[index], button.Max);
        }).ToList();
        session.Send(ColonyPackets.Route(update, type, developer ? points : 0, buttons), $"(colony {route.Name} route)");
    }

    public static void Develop(ClientSession session, int index)
    {
        ServerContext context = session.Context;
        ColonyData rules = Rules(context);
        Guild? guild = session.Guild;
        Colony? colony = guild == null ? null : Of(context, guild.Name);
        ColonyRoute? route = rules.Route(session.ColonyRouteShown);
        if (guild == null || colony == null || route == null)
        {
            session.SystemMessage("Open a development route of your Colonial Port first.");
            return;
        }
        if (guild.Member(session.Name)?.Duty < rules.DevelopDuty)
        {
            session.SystemMessage("Only the guild leader and vice leaders develop the Colonial Port.");
            return;
        }
        List<int> levels = colony.Levels(route);
        if (index < 0 || index >= route.Buttons.Count) return;
        ColonyRouteButton button = route.Buttons[index];
        if (Points(context, guild, colony) <= 0)
        {
            session.SystemMessage("The Colonial Port has no development points left. Raise the guild contribution to level the port.");
        }
        else if (levels[index] >= button.Max)
        {
            session.SystemMessage($"{button.Name} is fully developed.");
        }
        else if (button.RequiresIndex >= 0 && levels[button.RequiresIndex] < button.RequiresLevel)
        {
            session.SystemMessage($"{button.Name} requires {route.Buttons[button.RequiresIndex].Name} of level {button.RequiresLevel}.");
        }
        else
        {
            levels[index]++;
            colony.Spent++;
            Save();
            session.SystemMessage($"{button.Name} of {colony.Name} is now of level {levels[index]}.");
            Log.Info(session.Tag, $"{session.Name} develops {route.Name}/{button.Name} of {colony.Name} to {levels[index]}");
        }
        ShowRoute(session, route.Type, true);
    }

    public static void CityInfo(ClientSession session)
    {
        ServerContext context = session.Context;
        ColonyData rules = Rules(context);
        Guild? guild = session.Guild;
        Colony? colony = guild == null ? null : Of(context, guild.Name);
        if (guild == null || colony == null)
        {
            session.SystemMessage("Your guild has no Colonial Port.");
            return;
        }
        int level = Level(context, guild);
        (int, int) Index(int type) => (Math.Min(rules.IndexMax, colony.RouteTotal(type) * rules.IndexPerLevel), rules.IndexMax);
        session.Send(ColonyPackets.CityInfo(rules.CityType, colony.City.Length > 0 ? colony.City : colony.Name, StyleName(rules, colony.Style), level,
            RouteText(rules, colony), guild.Fund,
            [Index(ColonyRouteType.Industry), Index(ColonyRouteType.Gathering), Index(ColonyRouteType.Trade), Index(ColonyRouteType.Military)],
            rules.BasePopulation + rules.PopulationPerLevel * (level - 1),
            rules.BaseFood + rules.FoodPerGatheringLevel * colony.RouteTotal(ColonyRouteType.Gathering)), "(colony info)");
    }

    public static void BuildInfo(ClientSession session)
    {
        ServerContext context = session.Context;
        ColonyData rules = Rules(context);
        Guild? guild = session.Guild;
        Colony? colony = guild == null ? null : Of(context, guild.Name);
        if (guild == null || colony == null)
        {
            session.SystemMessage("Your guild has no Colonial Port.");
            return;
        }
        int level = Level(context, guild);
        int next = Math.Min(level, rules.LevelContribution.Count - 1);
        int guildNeeded = next >= 0 ? rules.LevelContribution[next] : 0;
        int unionNeeded = next >= 0 && next < rules.LevelUnionContribution.Count ? rules.LevelUnionContribution[next] : 0;
        session.Send(ColonyPackets.BuildCityInfo(colony.Name, UnionContribution(context, guild), unionNeeded, Contribution(guild), guildNeeded),
            "(colony contribution)");
        session.Send(GuildPackets.Clear(ColonyList.CityMembers), "(clear colony members)");
        foreach (byte[] row in GuildPackets.Rows(ColonyList.CityMembers, guild.Members.Select(member => new[]
        {
            member.Name, (Record(context, member.Name)?.UnionContribution ?? 0).ToString(), member.Contribution.ToString(),
        })))
            session.Send(row, "(colony members)");
    }

    public static void UnionInfo(ClientSession session, string code)
    {
        ServerContext context = session.Context;
        ColonyData rules = Rules(context);
        ColonyUnion? union = rules.Union(code) ?? rules.Union(session.Character.UnionName) ?? rules.Unions.FirstOrDefault();
        if (union == null) return;
        session.UnionViewed = union.Code;
        session.UnionPage = 0;
        foreach (byte[] row in GuildPackets.Rows(ColonyList.Unions, rules.Unions.Select(entry => new[]
        {
            entry.Code, entry.Headquarters,
            GuildDirectory.Same(entry.Code, session.Character.UnionName) ? session.Character.UnionContribution.ToString() : "0",
        })))
            session.Send(row, "(merchant alliances)");
        SendUnionGuilds(session);
    }

    public static void UnionPage(ClientSession session, string code, int step)
    {
        ColonyData rules = Rules(session.Context);
        if (rules.Union(code) is { } union && !GuildDirectory.Same(union.Code, session.UnionViewed))
        {
            session.UnionViewed = union.Code;
            session.UnionPage = 0;
        }
        session.UnionPage = Math.Max(0, session.UnionPage + step);
        SendUnionGuilds(session);
    }

    public static void JoinUnion(ClientSession session, string code)
    {
        ColonyData rules = Rules(session.Context);
        ColonyUnion? union = rules.Union(code);
        if (union == null)
        {
            session.SystemMessage("Choose a Merchant Alliance.");
            return;
        }
        if (GuildDirectory.Same(session.Character.UnionName, union.Code))
        {
            session.SystemMessage($"You already belong to {union.Name}.");
            return;
        }
        bool switched = session.Character.UnionName.Length > 0;
        session.Character.UnionName = union.Code;
        session.Character.UnionContribution = 0;
        session.Save();
        session.ApplyUnion();
        session.SystemMessage(switched
            ? $"You joined {union.Name}. Your contribution to your former alliance is lost."
            : $"You joined {union.Name}.");
        Log.Info(session.Tag, $"{session.Name} joins the merchant alliance {union.Code}");
        UnionInfo(session, union.Code);
    }

    public static void GuildDisbanded(ServerContext context, string guild)
    {
        if (Store(context).Colonies.RemoveAll(colony => GuildDirectory.Same(colony.Guild, guild)) > 0) Save();
    }

    public static int UnionContribution(ServerContext context, Guild guild)
        => guild.Members.Sum(member => Record(context, member.Name)?.UnionContribution ?? 0);

    private static void SendUnionGuilds(ClientSession session)
    {
        ServerContext context = session.Context;
        ColonyData rules = Rules(context);
        int size = Math.Max(1, rules.UnionPageSize);
        List<Guild> guilds = context.Accounts.AllCharacters()
            .Where(record => GuildDirectory.Same(record.UnionName, session.UnionViewed))
            .Select(record => GuildDirectory.Of(context, record.Name))
            .Where(guild => guild != null && guild.Members.Any(member => member.Duty == GuildDuty.Leader
                && GuildDirectory.Same(Record(context, member.Name)?.UnionName ?? "", session.UnionViewed)))
            .Select(guild => guild!)
            .Distinct()
            .OrderBy(guild => guild.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int pages = Math.Max(1, (guilds.Count + size - 1) / size);
        session.UnionPage = Math.Min(session.UnionPage, pages - 1);
        session.Send(GuildPackets.Clear(ColonyList.UnionGuilds), "(clear alliance guilds)");
        foreach (byte[] row in GuildPackets.Rows(ColonyList.UnionGuilds,
            guilds.Skip(session.UnionPage * size).Take(size).Select(guild => new[] { guild.Name, guild.Flag.ToString() })))
            session.Send(row, "(alliance guilds)");
        if (session.UnionPage >= pages - 1) session.Send(ColonyPackets.NoNextPage(), "(no next alliance page)");
        if (session.UnionPage == 0) session.Send(ColonyPackets.NoPrevPage(), "(no previous alliance page)");
    }

    private static string FoundingObstacle(ClientSession session)
    {
        ColonyData rules = Rules(session.Context);
        Guild? guild = session.Guild;
        if (guild == null) return "Colonial Ports are founded by guilds. Join a guild first.";
        if (guild.Member(session.Name)?.Duty < rules.FounderDuty) return "Only the guild leader can found a Colonial Port.";
        if (Of(session.Context, guild.Name) is { } colony) return $"Your guild already owns the Colonial Port {colony.Name}.";
        if (guild.Fund < rules.FoundCost) return $"Founding a Colonial Port costs {rules.FoundCost} silver coins from the guild fund, which holds {guild.Fund}.";
        return "";
    }

    private static string StyleName(ColonyData rules, int style) => style >= 0 && style < rules.Styles.Count ? rules.Styles[style] : "-";

    private static string RouteText(ColonyData rules, Colony colony)
    {
        ColonyRoute? best = rules.Routes.OrderByDescending(route => colony.RouteTotal(route.Type)).FirstOrDefault();
        return best == null || colony.RouteTotal(best.Type) == 0 ? "-" : best.Name;
    }

    private static CharacterRecord? Record(ServerContext context, string name)
        => context.World.FindPlayer(name)?.Character ?? context.Accounts.FindCharacter(name);

    private static ColonyStore Store(ServerContext context)
    {
        if (_store != null) return _store;
        _path = context.Settings.Resolve("data\\colonies.json");
        _store = new ColonyStore();
        try
        {
            if (File.Exists(_path))
                _store = JsonSerializer.Deserialize<ColonyStore>(File.ReadAllText(_path), EmulatorSettings.Json) ?? new ColonyStore();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Colony", $"Load failed: {exception.Message}");
        }
        return _store;
    }

    private static void Save()
    {
        if (_store == null || _path.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_store, EmulatorSettings.Json));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Colony", $"Save failed: {exception.Message}");
        }
    }
}
