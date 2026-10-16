using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public void SetupTorpedo()
    {
        if (!AtSea || Mover == null || SeaSunk || SceneType != OffshoreSceneType)
        {
            SystemMessage("This Function can only be used in the Offshore.", SystemMessageKind.Prompt);
            return;
        }
        if (FirstSeaBomb() == null)
        {
            SystemMessage("You have no mines aboard.", SystemMessageKind.Prompt);
            return;
        }
        _seaBombArmed = true;
        Send(SeaBombPackets.ShowBombArea(SeaBombAreaRadius), "(choose where to lay the mine)");
        Log.Debug(Tag, $"{Name} readies a mine");
    }
}
