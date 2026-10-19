using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    // BtnFreshPlayer ("refresh players") sends csSetVisualPlayers: the server sends the players around again.
    public void RefreshVisiblePlayers()
    {
        foreach (ClientSession other in Others())
        {
            if (other.GmHidden && !IsGameMaster) continue;
            Send(ScenePackets.RemoveObject(other.ObjectId), $"(refresh {other.Name})");
            other.ShowTo(this);
        }
    }
}
