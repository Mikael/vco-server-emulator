using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const float NpcTalkRange = 3f;
    private const float NpcStopDistance = 2f;

    public void ApproachNpc(NpcDefinition npc)
    {
        if (Mover == null || AtSea)
        {
            TalkNow(npc);
            return;
        }
        (float x, float z) = Mover.Now();
        float dx = x - npc.X, dz = z - npc.Z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance <= NpcTalkRange)
        {
            TalkNow(npc);
            return;
        }
        float stopX = npc.X + dx / distance * NpcStopDistance;
        float stopZ = npc.Z + dz / distance * NpcStopDistance;
        MoveTo(stopX, stopZ);
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
                        (float nowX, float nowZ) = Mover.Now();
                        float ax = nowX - npc.X, az = nowZ - npc.Z;
                        if (ax * ax + az * az <= NpcTalkRange * NpcTalkRange)
                        {
                            TalkNow(npc);
                            return;
                        }
                        if (MathF.Abs(Mover.DestinationX - stopX) > 0.01f || MathF.Abs(Mover.DestinationZ - stopZ) > 0.01f) return;
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

    private void TalkNow(NpcDefinition npc)
    {
        if (!AtSea) PlayTalkAnimation(npc);
        if (!OnNpcTalked(npc.Id)) Talk(npc);
    }

    private void PlayTalkAnimation(NpcDefinition npc)
    {
        if (Mover == null) return;
        int talk = Context.Data.Actions.Gesture("talking");
        if (talk < 0) return;
        (float x, float z) = Mover.Now();
        Mover.MoveTo(x, z);
        float dx = npc.X - x, dz = npc.Z - z;
        int facing = Mover.Heading;
        if (dx * dx + dz * dz > 1e-4f)
        {
            double tenths = Math.Atan2(dx, dz) * 1800.0 / Math.PI;
            facing = (int)Math.Round(tenths < 0 ? tenths + 3600.0 : tenths) % 3600;
        }
        Send(ScenePackets.PlaceObject(ProtocolConstants.SelfObjectId, x, Height, z, orient: facing), "(face npc)");
        ToOthers(ScenePackets.PlaceObject(ObjectId, x, Height, z, orient: facing), "(face npc)");
        Send(ScenePackets.PlayAction(ProtocolConstants.SelfObjectId, talk), "(talk)");
        ToOthers(ScenePackets.PlayAction(ObjectId, talk), "(talk)");
        Send(ScenePackets.PlayAction(npc.Id, talk), "(npc talks)");
    }
}
