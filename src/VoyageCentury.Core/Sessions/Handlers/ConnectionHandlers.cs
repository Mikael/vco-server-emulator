using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Network;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions.Handlers;

public sealed class ServerListHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.HandshakeReply;

    public void Handle(ClientSession session, PacketReader reader)
    {
        var settings = session.Context.Settings;
        session.Send(LoginPackets.ServerList(settings.ServerName, settings.PublicAddress, settings.GamePort), "(server list)");
        Log.Info(session.Tag, "Sent the server list");
    }
}

public sealed class LoginHandshakeHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.HandshakeReply;

    public void Handle(ClientSession session, PacketReader reader)
        => session.Send(LoginPackets.VerifyPicture(), "(verification picture)");
}

public sealed class SilentHandler : IMessageHandler
{
    public SilentHandler(ClientMsg id) => Id = id;

    public ClientMsg Id { get; }

    public void Handle(ClientSession session, PacketReader reader)
    {
    }
}
