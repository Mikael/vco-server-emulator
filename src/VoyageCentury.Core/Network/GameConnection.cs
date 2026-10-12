using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace VoyageCentury.Core.Network;

public sealed class GameConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly object _writeGate = new();
    private readonly MessageDeframer _deframer = new();

    public GameConnection(TcpClient client, int port)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        Port = port;
        var remote = (IPEndPoint)client.Client.RemoteEndPoint!;
        Tag = $"[{port} {remote.Address}:{remote.Port}]";
    }

    public int Port { get; }

    public string Tag { get; }

    public void Send(ReadOnlySpan<byte> body)
    {
        byte[] framed = MessageFramer.Frame(body);
        lock (_writeGate) _stream.Write(framed);
    }

    public async IAsyncEnumerable<byte[]> ReadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[65536];
        while (true)
        {
            int read = await _stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) yield break;
            foreach (byte[] message in _deframer.Feed(buffer.AsSpan(0, read)))
                yield return message;
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
