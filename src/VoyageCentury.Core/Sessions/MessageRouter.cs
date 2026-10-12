using System.Net.Sockets;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Network;
using VoyageCentury.Core.Protocol;

namespace VoyageCentury.Core.Sessions;

public sealed class MessageRouter
{
    private readonly Dictionary<ClientMsg, IMessageHandler> _handlers = [];

    public MessageRouter Add(IMessageHandler handler)
    {
        _handlers[handler.Id] = handler;
        return this;
    }

    public void Dispatch(ClientSession session, byte[] message)
    {
        if (message.Length < 2) return;

        var id = (ClientMsg)(message[0] | message[1] << 8);
        string name = Enum.IsDefined(id) ? id.ToString() : $"0x{(int)id:x2}";
        Log.Packet(session.Tag, "RECV", message, name);

        lock (session.Context.World.Sync)
        {
            if (!_handlers.TryGetValue(id, out IMessageHandler? handler))
            {
                Log.Missing(session.Tag, $"client message 0x{(int)id:x2}, {message.Length} bytes", message);
                return;
            }

            try
            {
                handler.Handle(session, new PacketReader(message, offset: 2));
            }
            catch (EndOfStreamException exception)
            {
                Log.Warn(session.Tag, $"Malformed {name}: {exception.Message}");
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or IOException or ObjectDisposedException or SocketException))
            {
                Log.Error(session.Tag, $"{name} failed: {exception}");
            }
        }
    }
}
