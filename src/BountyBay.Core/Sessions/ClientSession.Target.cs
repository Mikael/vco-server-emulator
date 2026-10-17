using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public void ShowTarget(int objectId)
    {
        if (Links.Find(Scene, objectId) != null) return;
        Send(ChatPackets.Script(ProtocolConstants.SelfObjectId, "3DCO", objectId), $"(target panel {objectId})");
    }
}
