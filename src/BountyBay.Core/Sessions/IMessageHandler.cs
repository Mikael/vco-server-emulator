using BountyBay.Core.Network;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Sessions;

public interface IMessageHandler
{
    ClientMsg Id { get; }

    void Handle(ClientSession session, PacketReader reader);
}
