using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Mining and timber-felling: the suburbs have gathering points (land goods objects). With the pickaxe or the
// felling axe equipped the client shows the gathering cursor over them; clicking one walks there, plays the
// gathering action and gives ore or logs of the point's grade. Ore Study / Log Study (US_MINING2 / US_LUMBER2)
// decide the highest grade you can take.
public sealed partial class ClientSession
{
    private const float GatherReach = 4f;
    private static readonly Dictionary<int, long> GatherDepletedUntil = [];

    private int _gatherSerial;

    private GatheringBook Gathering => GatheringBook.For(Context.Settings);

    private static bool GatherDepleted(GatherNode node)
    {
        lock (GatherDepletedUntil)
            return GatherDepletedUntil.TryGetValue(node.ObjectId, out long until) && until > Environment.TickCount64;
    }

    public void SendGatherNodes()
    {
        if (AtSea) return;
        foreach (GatherNode node in Gathering.InScene(Scene).Where(node => !GatherDepleted(node))) ShowGatherNode(node);
    }

    private void ShowGatherNode(GatherNode node)
    {
        Send(ScenePackets.CreateObject(node.ObjectId, ObjectKind.Goods, node.Model), "(gathering point)");
        Send(ScenePackets.ObjectProperties(node.ObjectId, new PropertyBag { ["X"] = node.X, ["Y"] = node.Y, ["Z"] = node.Z, ["Orient"] = 0f }),
            "(gathering point)");
        Send(ScenePackets.ShowObject(node.ObjectId), "(gathering point)");
    }

    public bool OnGatherNodeClicked(int objectId)
    {
        if (Gathering.Find(objectId) is not { } node || node.Scene != Scene || Mover == null) return false;
        if (GatherDepleted(node))
        {
            SystemMessage("There is nothing left to gather here. Come back later.");
            return true;
        }
        int serial = ++_gatherSerial;
        (float x, float z) = Mover.Now();
        float dx = node.X - x, dz = node.Z - z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance <= GatherReach)
        {
            StartGather(node);
            return true;
        }
        float stop = (distance - GatherReach * 0.6f) / distance;
        MoveTo(x + dx * stop, z + dz * stop);
        Sea.Later(TimeToDestination().TotalSeconds + 0.2, () =>
        {
            if (serial != _gatherSerial || Mover == null || Scene != node.Scene) return;
            (float nowX, float nowZ) = Mover.Now();
            if (MathF.Sqrt((node.X - nowX) * (node.X - nowX) + (node.Z - nowZ) * (node.Z - nowZ)) <= GatherReach + 0.5f) StartGather(node);
        });
        return true;
    }

    private void StartGather(GatherNode node)
    {
        string study = node.Skill == "SKILL_MINING" ? "US_MINING2" : "US_LUMBER2";
        int highest = 1 + UniqueSkillLevel(study);
        if (node.Grade > highest)
        {
            SystemMessage($"{(node.Skill == "SKILL_MINING" ? "Ore Study" : "Log Study")} Level {node.Grade - 1} is needed for grade {node.Grade} {(node.Skill == "SKILL_MINING" ? "ore" : "timber")}.",
                SystemMessageKind.Prompt);
            return;
        }
        BeginBboGathering(node.Skill, Gathering.GatherSeconds, () => FinishGather(node));
    }

    private void FinishGather(GatherNode node)
    {
        if (Scene != node.Scene) return;
        bool mining = node.Skill == "SKILL_MINING";
        if (GatherDepleted(node))
        {
            SystemMessage("Someone else got there first.");
            return;
        }
        int chance = Math.Clamp(75 + 2 * SkillLevel(node.Skill) - 15 * (node.Grade - 1), 25, 95);
        if (Random.Shared.Next(100) >= chance)
        {
            GainSkillExperience(node.Skill, Gathering.ExperiencePerGrade * node.Grade / 4);
            SystemMessage(mining ? "You struck the rock but found no usable ore." : "The tree did not yield any usable timber.");
            return;
        }
        if (!GiveItem(new ItemStack { Name = node.Item, Photo = Context.Data.PhotoOf(node.Item), Amount = 1 }, mining ? "(mined)" : "(felled)"))
        {
            SystemMessage("Your bag is full.");
            return;
        }
        ItemObtained(node.Item, 1, mining ? "mined" : "felled");
        GainSkillExperience(node.Skill, Gathering.ExperiencePerGrade * node.Grade);
        Save();
        DepleteGatherNode(node);
        Log.Info(Tag, $"{Name} {(mining ? "mines" : "fells")} {node.Item} (grade {node.Grade}) in scene {Scene}");
    }

    private void DepleteGatherNode(GatherNode node)
    {
        int respawn = Math.Max(5, Gathering.RespawnSeconds);
        lock (GatherDepletedUntil) GatherDepletedUntil[node.ObjectId] = Environment.TickCount64 + respawn * 1000L;
        foreach (ClientSession other in Context.World.Sessions.Where(other => other.InWorld && other.Scene == node.Scene && !other.AtSea).ToList())
            other.Send(ScenePackets.RemoveObject(node.ObjectId), "(gathering point used)");
        Sea.Later(respawn, () =>
        {
            foreach (ClientSession other in Context.World.Sessions.Where(other => other.InWorld && other.Scene == node.Scene && !other.AtSea).ToList())
                other.ShowGatherNode(node);
        });
    }
}
