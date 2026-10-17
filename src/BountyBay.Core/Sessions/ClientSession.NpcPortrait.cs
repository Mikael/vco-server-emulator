using BountyBay.Core.Data;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public static PropertyBag NpcPortraitProperties(NpcDefinition npc) => new() { ["Photo"] = npc.Portrait };

    public void SendNpcPortrait(NpcDefinition npc)
        => Send(ScenePackets.ObjectProperties(npc.Id, NpcPortraitProperties(npc)), $"(portrait {npc.Name})");

    public void SendNpcPortrait(int npcId)
    {
        if (Context.Data.Npcs.TryGetValue(npcId, out NpcDefinition? npc)) SendNpcPortrait(npc);
    }
}
