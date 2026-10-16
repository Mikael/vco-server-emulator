using System.Numerics;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public bool InGuildCopy => InWorld && Scene == GuildCopyDirectory.Rules(Context).Scene;

    public void OpenGuildCopy()
    {
        if (Guild is not { } guild)
        {
            SystemMessage("Only guild members can take part in the Secret Isle adventure.", SystemMessageKind.Prompt);
            return;
        }
        GuildCopyRun run = GuildCopyDirectory.Run(Context, guild);
        Send(GuildCopyPackets.Show(), "(secret isle window)");
        Send(GuildCopyPackets.SeasonTime(GuildBuildDirectory.SecondsToNextSeason(DateTime.UtcNow)), "(secret isle season)");
        SendGuildCopyApplicants(guild, run);
        if (GuildCopyDirectory.Active(run, DateTime.UtcNow)) Send(GuildCopyPackets.TimeLeft(GuildCopyDirectory.SecondsLeft(run)), "(secret isle time)");
    }

    public void AskGuildCopyApplicants()
    {
        if (Guild is not { } guild) return;
        SendGuildCopyApplicants(guild, GuildCopyDirectory.Run(Context, guild));
    }

    public void ApplyGuildCopy()
    {
        if (Guild is not { } guild)
        {
            SystemMessage("Only guild members can sign up for the Secret Isle.", SystemMessageKind.Prompt);
            return;
        }
        GuildCopyRun run = GuildCopyDirectory.Run(Context, guild);
        GuildCopyRules rules = GuildCopyDirectory.Rules(Context);
        if (GuildCopyDirectory.Applicant(run, Name) != null)
        {
            SystemMessage("You have already signed up for the Secret Isle.", SystemMessageKind.Prompt);
            return;
        }
        if (GuildCopyDirectory.Active(run, DateTime.UtcNow))
        {
            SystemMessage("Your guild's Secret Isle adventure is already under way.", SystemMessageKind.Prompt);
            return;
        }
        if (run.Runs >= rules.RunsPerSeason)
        {
            SystemMessage("Your guild has already explored the Secret Isle this week.", SystemMessageKind.Prompt);
            return;
        }
        var applicant = new GuildCopyApplicant { Name = Name, State = GuildCopyPackets.Waiting };
        run.Applicants.Add(applicant);
        GuildBuildStore.Save();
        GuildCopyDirectory.Publish(Context, guild, run, applicant);
        SystemMessage("You have signed up for the Secret Isle adventure. Wait for an officer's approval.");
        Log.Info(Tag, $"{Name} signs up for the Secret Isle of {guild.Name}");
    }

    public void ApproveGuildCopy(string name)
    {
        if (!GuildCopyOfficer(out Guild guild)) return;
        GuildCopyRun run = GuildCopyDirectory.Run(Context, guild);
        if (GuildCopyDirectory.Applicant(run, name) is not { } applicant)
        {
            SystemMessage($"{name} has not signed up for the Secret Isle.", SystemMessageKind.Prompt);
            return;
        }
        if (applicant.State == GuildCopyPackets.Approved) return;
        applicant.State = GuildCopyPackets.Approved;
        GuildBuildStore.Save();
        GuildCopyDirectory.Publish(Context, guild, run, applicant);
        if (Context.World.FindPlayer(applicant.Name) is { InWorld: true } player)
        {
            player.SystemMessage($"{Name} approved your place in the Secret Isle adventure.");
            if (GuildCopyDirectory.Active(run, DateTime.UtcNow)) player.Send(GuildCopyPackets.Invite(), "(secret isle invitation)");
        }
        Log.Info(Tag, $"{Name} approves {applicant.Name} for the Secret Isle");
    }

    public void CommitGuildCopy()
    {
        if (!GuildCopyOfficer(out Guild guild)) return;
        GuildCopyRun run = GuildCopyDirectory.Run(Context, guild);
        GuildCopyRules rules = GuildCopyDirectory.Rules(Context);
        if (GuildCopyDirectory.Active(run, DateTime.UtcNow))
        {
            SystemMessage("The Secret Isle adventure is already under way.", SystemMessageKind.Prompt);
            return;
        }
        if (run.Runs >= rules.RunsPerSeason)
        {
            SystemMessage("Your guild has already explored the Secret Isle this week.", SystemMessageKind.Prompt);
            return;
        }
        int approved = run.Applicants.Count(entry => entry.State == GuildCopyPackets.Approved);
        if (approved < Math.Max(1, rules.MinMembers))
        {
            SystemMessage($"Approve at least {Math.Max(1, rules.MinMembers)} members before you submit.", SystemMessageKind.Prompt);
            return;
        }
        GuildCopyDirectory.Commit(Context, guild, run, Name);
        SendGuildCopyApplicants(guild, run);
    }

    public void JoinGuildCopy()
    {
        if (Guild is not { } guild)
        {
            SystemMessage("Only guild members can enter the Secret Isle.", SystemMessageKind.Prompt);
            return;
        }
        GuildCopyRun run = GuildCopyDirectory.Run(Context, guild);
        if (!GuildCopyDirectory.Active(run, DateTime.UtcNow))
        {
            SystemMessage("Your guild's Secret Isle adventure has not begun.", SystemMessageKind.Prompt);
            return;
        }
        if (GuildCopyDirectory.Applicant(run, Name) is not { State: GuildCopyPackets.Approved } applicant)
        {
            SystemMessage("You were not approved for this Secret Isle adventure.", SystemMessageKind.Prompt);
            return;
        }
        if (!InWorld) return;
        if (InGuildCopy)
        {
            SendGuildCopyState(run);
            return;
        }
        int scene = GuildCopyDirectory.Rules(Context).Scene;
        if (!Context.Data.Scenes.Exists(scene))
        {
            SystemMessage("The Secret Isle cannot be reached on this server.", SystemMessageKind.Prompt);
            Log.Warn(Tag, $"Secret Isle scene {scene} does not exist");
            return;
        }
        (float x, float z) = Mover!.Now();
        Character.GuildCopyReturnScene = Scene;
        Character.GuildCopyReturnX = x;
        Character.GuildCopyReturnZ = z;
        if (!applicant.Entered)
        {
            applicant.Entered = true;
            GuildBuildStore.Save();
            GuildBuildDirectory.AddActivity(Context, guild, GuildBuildDirectory.Rules(Context).Activity.CopyEntry, $"{Name} entered the Secret Isle");
        }
        Save();
        Log.Info(Tag, $"{Name} enters the Secret Isle (scene {scene})");
        Send(ScenePackets.LeaveScene(), "(leave scene)");
        EnterScene(scene);
        SendGuildCopyState(run);
    }

    public void LeaveGuildCopy()
    {
        SetGuildCopyFlag(0);
        if (!InGuildCopy) return;
        int scene = Character.GuildCopyReturnScene;
        Vector3? position = null;
        if (scene == 0 || !Context.Data.Scenes.Exists(scene) || scene == Scene) scene = Character.LastPort != 0 ? Character.LastPort : WorldSettings.StartScene;
        else position = new Vector3(Character.GuildCopyReturnX, 0f, Character.GuildCopyReturnZ);
        Character.GuildCopyReturnScene = 0;
        Save();
        Log.Info(Tag, $"{Name} leaves the Secret Isle for scene {scene}");
        Send(ScenePackets.LeaveScene(), "(leave scene)");
        EnterScene(scene, position);
    }

    public void GuildCopyGame(string[] words)
    {
        Log.Info(Tag, $"{Name} Secret Isle game request: {string.Join(' ', words)}");
        SystemMessage(InGuildCopy
            ? "The Isle Keeper's games are not open on this server yet."
            : "You can play the Isle Keeper's games only inside the Secret Isle.", SystemMessageKind.Prompt);
    }

    public void GuildCopyMonsterKilled()
    {
        if (!InGuildCopy || Guild is not { } guild) return;
        GuildCopyRun run = GuildCopyDirectory.Run(Context, guild);
        if (GuildCopyDirectory.Active(run, DateTime.UtcNow)) GuildCopyDirectory.Killed(Context, guild, run);
    }

    public void CheckGuildCopyOnEnter()
    {
        if (!InGuildCopy) return;
        GuildCopyRun? run = Guild is { } guild ? GuildCopyDirectory.Run(Context, guild) : null;
        if (run != null && GuildCopyDirectory.Active(run, DateTime.UtcNow)
            && GuildCopyDirectory.Applicant(run, Name) is { State: GuildCopyPackets.Approved })
        {
            SendGuildCopyState(run);
            return;
        }
        SystemMessage("The Secret Isle adventure is over. You are taken back.", SystemMessageKind.Prompt);
        LeaveGuildCopy();
    }

    internal void GuildCopyStageChanged(GuildCopyRun run)
    {
        GuildCopyRules rules = GuildCopyDirectory.Rules(Context);
        if (run.Stage > rules.Stages)
        {
            SystemMessage("Your guild has conquered the Secret Isle!", SystemMessageKind.Upgrade);
            return;
        }
        SendGuildCopyState(run);
    }

    private void SendGuildCopyState(GuildCopyRun run)
    {
        int stage = Math.Min(run.Stage, GuildCopyDirectory.Rules(Context).Stages);
        SetGuildCopyFlag(stage);
        Send(GuildCopyPackets.TimeLeft(GuildCopyDirectory.SecondsLeft(run)), "(secret isle time)");
        Send(GuildCopyPackets.Stage(stage), "(secret isle stage)");
    }

    private void SendGuildCopyApplicants(Guild guild, GuildCopyRun run)
    {
        Send(GuildCopyPackets.ClearApplicants(), "(secret isle applicants)");
        foreach (byte[] row in GuildCopyDirectory.ApplicantRows(Context, run)) Send(row, "(secret isle applicants)");
    }

    private void SetGuildCopyFlag(int stage)
    {
        if (!PropertyTable.Has("ConCopyFlag")) return;
        if (Properties.TryGetValue("ConCopyFlag", out object? current) && Equals(current, stage)) return;
        SetProperty("ConCopyFlag", stage, true);
    }

    private bool GuildCopyOfficer(out Guild guild)
    {
        Guild? found = Guild;
        guild = found!;
        if (found == null)
        {
            SystemMessage("You belong to no guild.", SystemMessageKind.Prompt);
            return false;
        }
        if ((guild.Member(Name)?.Duty ?? GuildDuty.Member) >= GuildDuty.Director) return true;
        SystemMessage("Only a director or a higher officer can manage the Secret Isle sign-ups.", SystemMessageKind.Prompt);
        return false;
    }
}
