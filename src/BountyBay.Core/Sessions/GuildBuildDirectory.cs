using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class GuildBuildDirectory
{
    public static readonly string[] DegreeNames = ["Economic", "Industrial", "Military", "Agriculture"];

    public static GuildBuildData Rules(ServerContext context) => GuildBuildData.For(context.Settings);

    public static GuildBuildState State(ServerContext context, Guild guild) => GuildBuildStore.Of(context.Settings, guild.Name);

    public static DateTime SeasonStart(DateTime now)
    {
        DateTime day = now.Date;
        int back = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-back);
    }

    public static string SeasonKey(DateTime now) => SeasonStart(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static int SecondsToNextSeason(DateTime now) => (int)Math.Ceiling((SeasonStart(now).AddDays(7) - now).TotalSeconds);

    public static List<ClientSession> Online(ServerContext context, Guild guild)
        => context.World.Sessions.Where(session => session.InWorld && guild.Member(session.Name) != null).ToList();

    public static List<Guild> All(ServerContext context)
        => Rankings.Guilds(context).Select(entry => GuildDirectory.Find(context, entry.Name)).OfType<Guild>().ToList();

    public static int ActivityLevel(ServerContext context, Guild guild) => Rules(context).Activity.LevelOf(State(context, guild).Activity);

    public static void AddActivity(ServerContext context, Guild guild, long amount, string reason)
    {
        if (amount <= 0) return;
        GuildBuildState state = State(context, guild);
        int before = Rules(context).Activity.LevelOf(state.Activity);
        state.Activity += amount;
        int after = Rules(context).Activity.LevelOf(state.Activity);
        if (after > before) Note(context, guild, $"Guild activity reached level {after}.");
        GuildBuildStore.Save();
        Log.Info("GuildBuild", $"{guild.Name} activity +{amount} ({reason}) = {state.Activity}");
        byte[] packet = GuildBuildPackets.Activity(after, state.Activity);
        foreach (ClientSession member in Online(context, guild)) member.Send(packet, "(guild activity)");
    }

    public static void Note(ServerContext context, Guild guild, string line)
    {
        GuildBuildState state = State(context, guild);
        string stamp = DateTime.Now.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        state.Log.Insert(0, $"{stamp} {line}");
        int limit = Math.Max(1, Rules(context).Construction.LogLimit);
        if (state.Log.Count > limit) state.Log.RemoveRange(limit, state.Log.Count - limit);
        GuildBuildStore.Save();
    }

    public static int ConstructionState(Guild guild)
        => guild.Relations.Values.Any(state => state == GuildRelation.War) ? GuildBuildPackets.AtWar : GuildBuildPackets.Neutral;

    public static bool AddDegree(ServerContext context, Guild guild, int degree, int amount, string by)
    {
        if (degree < 0 || degree > 3 || amount <= 0) return false;
        GuildConstructionRules rules = Rules(context).Construction;
        GuildBuildState state = State(context, guild);
        int max = rules.DegreeMax(guild.Level);
        if (state.Degrees[degree] >= max && guild.Level >= rules.MaxLevel) return false;
        state.Degrees[degree] = Math.Min(max, state.Degrees[degree] + amount);
        state.BuildExp += (long)amount * rules.ExpPerDegree;
        Note(context, guild, $"{by} raised the {DegreeNames[degree]} degree by {amount}.");
        if (guild.Level < rules.MaxLevel && state.Degrees.All(value => value >= max))
        {
            guild.Level++;
            for (int i = 0; i < state.Degrees.Length; i++) state.Degrees[i] = 0;
            GuildDirectory.Save();
            Note(context, guild, $"The guild was built up to level {guild.Level}.");
            foreach (ClientSession member in Online(context, guild))
                member.SystemMessage($"Your guild {guild.Name} has been built up to level {guild.Level}!", SystemMessageKind.Upgrade);
        }
        GuildBuildStore.Save();
        return true;
    }

    public static byte[] Construction(ServerContext context, Guild guild)
    {
        GuildConstructionRules rules = Rules(context).Construction;
        GuildBuildState state = State(context, guild);
        int max = rules.DegreeMax(guild.Level);
        return GuildBuildPackets.Construction(ConstructionState(guild), guild.Level, state.BuildExp, state.Degrees.Select(value => (value, max)));
    }

    public static void Dissolved(ServerContext context, string guild) => GuildBuildStore.Forget(context.Settings, guild);
}
