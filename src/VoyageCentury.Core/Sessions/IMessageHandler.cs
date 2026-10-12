using VoyageCentury.Core.Network;
using VoyageCentury.Core.Protocol;

namespace VoyageCentury.Core.Sessions;

public interface IMessageHandler
{
    ClientMsg Id { get; }

    void Handle(ClientSession session, PacketReader reader);
}
