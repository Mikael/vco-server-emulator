using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class GuildCopyDirectory
{
    public static GuildCopyRules Rules(ServerContext context) => GuildBuildDirectory.Rules(context).Copy;

    public static GuildCopyRun Run(ServerContext context, Guild guild)
    {
        GuildCopyRun run = GuildBuildDirectory.State(context, guild).Copy;
        DateTime now = DateTime.UtcNow;
        string season = GuildBuildDirectory.SeasonKey(now);
        bool changed = false;
        if (run.Season != season)
        {
            run.Season = season;
            run.Runs = 0;
            if (!Active(run, now)) Reset(run);
            changed = true;
        }
        if (run.Committed && !Active(run, now))
        {
            Finish(context, guild, run, "The Secret Isle adventure has ended.");
            changed = true;
        }
        if (changed) GuildBuildStore.Save();
        return run;
    }

    public static bool Active(GuildCopyRun run, DateTime now) => run.Committed && now < run.ExpiresAt;

    public static int SecondsLeft(GuildCopyRun run) => Math.Max(0, (int)Math.Ceiling((run.ExpiresAt - DateTime.UtcNow).TotalSeconds));

    public static GuildCopyApplicant? Applicant(GuildCopyRun run, string name)
        => run.Applicants.FirstOrDefault(entry => GuildDirectory.Same(entry.Name, name));

    public static IEnumerable<byte[]> ApplicantRows(ServerContext context, GuildCopyRun run, IEnumerable<GuildCopyApplicant>? only = null)
        => GuildCopyPackets.Applicants((only ?? run.Applicants)
            .Select(entry => (entry.Name, context.World.FindPlayer(entry.Name) is { InWorld: true }, entry.State)));

    public static void Publish(ServerContext context, Guild guild, GuildCopyRun run, GuildCopyApplicant applicant)
    {
        byte[][] rows = ApplicantRows(context, run, [applicant]).ToArray();
        foreach (ClientSession member in GuildBuildDirectory.Online(context, guild))
        {
            foreach (byte[] row in rows) member.Send(row, "(secret isle applicant)");
        }
    }

    public static void Commit(ServerContext context, Guild guild, GuildCopyRun run, string by)
    {
        run.Committed = true;
        run.ExpiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, Rules(context).Minutes));
        run.Stage = 1;
        run.Kills = 0;
        run.Runs++;
        foreach (GuildCopyApplicant applicant in run.Applicants) applicant.Entered = false;
        GuildBuildDirectory.Note(context, guild, $"{by} opened the Secret Isle adventure.");
        GuildBuildStore.Save();
        foreach (ClientSession member in GuildBuildDirectory.Online(context, guild))
        {
            if (Applicant(run, member.Name) is { State: GuildCopyPackets.Approved })
            {
                member.Send(GuildCopyPackets.Invite(), "(secret isle invitation)");
                member.SystemMessage("The Secret Isle adventure of your guild has begun. Join it from the guild window.");
            }
        }
        Log.Info("GuildCopy", $"{by} opens the Secret Isle for {guild.Name} until {run.ExpiresAt:u}");
    }

    public static void Killed(ServerContext context, Guild guild, GuildCopyRun run)
    {
        GuildCopyRules rules = Rules(context);
        run.Kills++;
        if (run.Kills < Math.Max(1, rules.KillsPerStage) * run.Stage || run.Stage > rules.Stages)
        {
            GuildBuildStore.Save();
            return;
        }
        run.Stage++;
        GuildBuildDirectory.AddActivity(context, guild, GuildBuildDirectory.Rules(context).Activity.CopyStage, $"Secret Isle stage {run.Stage - 1}");
        GuildBuildDirectory.Note(context, guild, run.Stage > rules.Stages
            ? "The guild conquered the Secret Isle."
            : $"The guild reached stage {run.Stage} of the Secret Isle.");
        GuildBuildStore.Save();
        foreach (ClientSession member in context.World.InScene(rules.Scene).Where(session => guild.Member(session.Name) != null))
            member.GuildCopyStageChanged(run);
    }

    private static void Finish(ServerContext context, Guild guild, GuildCopyRun run, string notice)
    {
        Reset(run);
        int scene = Rules(context).Scene;
        foreach (ClientSession member in context.World.InScene(scene).Where(session => guild.Member(session.Name) != null).ToList())
        {
            member.SystemMessage(notice, SystemMessageKind.Prompt);
            member.LeaveGuildCopy();
        }
    }

    private static void Reset(GuildCopyRun run)
    {
        run.Committed = false;
        run.Stage = 1;
        run.Kills = 0;
        run.Applicants.Clear();
    }
}
