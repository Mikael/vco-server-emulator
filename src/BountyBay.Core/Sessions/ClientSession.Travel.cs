using System.Numerics;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int MaxArrivalChecks = 40;
    private const float ArrivalSlack = 1.5f;

    private static readonly string[] DoorProperties = ["Name", "X", "Y", "Z", "Orient"];
    private static bool _doorPropertiesReported;

    private int _linkSerial;

    private SceneLinkData Links => SceneLinks.Load(Context.Settings);

    public void SendSceneLinks()
    {
        _linkSerial++;
        List<SceneLink> doors = Links.InScene(Scene).Where(link => link.HasObject && Context.Data.Scenes.Exists(link.TargetScene)).ToList();
        if (doors.Count == 0) return;
        if (!DoorProperties.All(PropertyTable.Has))
        {
            if (!_doorPropertiesReported)
                Log.Warn(Tag, "scene doors are not shown: properties.json needs X, Y and Z (Float)");
            _doorPropertiesReported = true;
            return;
        }
        foreach (SceneLink door in doors)
        {
            var properties = new PropertyBag
            {
                ["Name"] = door.Name,
                ["X"] = door.X,
                ["Y"] = door.Y,
                ["Z"] = door.Z,
                ["Orient"] = door.Orient,
            };
            if (door.ModelId > 0 && PropertyTable.Has("ModelID")) properties["ModelID"] = door.ModelId;
            Send(ScenePackets.CreateObject(door.Id, ObjectKind.Door, door.Model), $"(door {door.Name})");
            Send(ScenePackets.ObjectProperties(door.Id, properties), $"(door {door.Name} properties)");
            Send(ScenePackets.ShowObject(door.Id), $"(show door {door.Name})");
        }
    }

    public bool OnLinkClicked(int objectId)
    {
        if (Mover == null) return false;
        SceneLink? link = Links.Find(Scene, objectId);
        if (link == null) return false;
        (float x, float z) = Mover.Now();
        if (link.DistanceTo(x, z) <= MathF.Max(Links.UseRange, link.Radius))
        {
            UseLink(link);
            return true;
        }
        if (StallOpen) return true;
        MoveTo(link.X, link.Z);
        WatchArrival(link);
        return true;
    }

    private const float NpcLeaveDistance = 10f;

    private void LeaveNpcIfFar(float x, float z)
    {
        if (TalkingTo == 0 || AtSea || !Context.Data.Npcs.TryGetValue(TalkingTo, out NpcDefinition? npc) || npc.Scene != Scene) return;
        float dx = npc.X - x, dz = npc.Z - z;
        if (dx * dx + dz * dz <= NpcLeaveDistance * NpcLeaveDistance) return;
        TalkingTo = 0;
        CloseRebuild();
        CloseShipyard();
        CloseNpcDialogue();
    }

    public void OnMoveRequested(float x, float z)
    {
        _linkSerial++;
        LeaveNpcIfFar(x, z);
        if (Mover == null || (AtSea && SeaSunk)) return;
        BboCheckCoastExit(x, z);
        AutoSailOnMove();
        (float fromX, float fromZ) = Mover.Now();
        SceneLink? link = Links.WalkInto(Scene, fromX, fromZ, x, z);
        if (link != null) WatchArrival(link);
    }

    public bool SailByLink()
    {
        if (Mover == null) return false;
        if (AtSea)
        {
            SceneLink? home = Links.SailBackFrom(Scene);
            if (home == null || !Context.Data.Scenes.Exists(home.Scene)) return false;
            _linkSerial++;
            Log.Info(Tag, $"{Name} comes ashore in scene {home.Scene}");
            GoTo(home.Scene, home.X, home.Y, home.Z);
            return true;
        }
        SceneLink? link = Links.SailFrom(Scene);
        if (link == null || !Context.Data.Scenes.Exists(link.TargetScene)) return false;
        UseLink(link);
        return true;
    }

    private void UseLink(SceneLink link)
    {
        _linkSerial++;
        if (SceneLocked()) return;
        if (link.TargetScene <= 0 || !Context.Data.Scenes.Exists(link.TargetScene))
        {
            SystemMessage(link.Message.Length > 0 ? link.Message : "This way is closed.");
            return;
        }
        Log.Info(Tag, $"{Name} takes '{link.Name}' from scene {Scene} to scene {link.TargetScene}");
        GoTo(link.TargetScene, link.TargetX, link.TargetY, link.TargetZ);
    }

    private void GoTo(int scene, float x, float y, float z)
    {
        ForceLand = false;
        Send(ScenePackets.LeaveScene(), "(leave scene)");
        EnterScene(scene, new Vector3(x, y, z));
    }

    private void WatchArrival(SceneLink link)
    {
        int serial = ++_linkSerial;
        int scene = Scene;
        TimeSpan delay = TimeToDestination();
        _ = Task.Run(async () =>
        {
            for (int check = 0; check < MaxArrivalChecks; check++)
            {
                await Task.Delay(delay);
                lock (Context.World.Sync)
                {
                    if (serial != _linkSerial || Mover == null || Scene != scene) return;
                    try
                    {
                        float reach = MathF.Max(link.Radius, ArrivalSlack);
                        (float x, float z) = Mover.Now();
                        if (link.DistanceTo(x, z) <= reach)
                        {
                            UseLink(link);
                            return;
                        }
                        if (link.DistanceTo(Mover.DestinationX, Mover.DestinationZ) > reach) return;
                        delay = TimeToDestination();
                    }
                    catch (Exception exception)
                    {
                        Log.Error(Tag, exception);
                        return;
                    }
                }
            }
        });
    }

    private TimeSpan TimeToDestination()
    {
        if (Mover == null) return TimeSpan.FromMilliseconds(200);
        (float x, float z) = Mover.Now();
        float dx = Mover.DestinationX - x;
        float dz = Mover.DestinationZ - z;
        float seconds = MathF.Sqrt(dx * dx + dz * dz) / MathF.Max(0.5f, Mover.Speed);
        return TimeSpan.FromSeconds(Math.Clamp(seconds + 0.15f, 0.2f, 600f));
    }
}
