using System.Net.Sockets;
using System.Text;
using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Network;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Sessions.Handlers;

namespace VoyageCentury.Core.Sessions;

public sealed class BotDirector
{
    private const string Tag = "Bots";

    private readonly ServerContext _context;
    private readonly List<(string Name, CancellationTokenSource Stop)> _bots = [];
    private readonly object _gate = new();

    public BotDirector(ServerContext context) => _context = context;

    public int Count
    {
        get { lock (_gate) return _bots.Count; }
    }

    public void SetCount(int count, CancellationToken serverStopping)
    {
        lock (_gate)
        {
            while (_bots.Count > count)
            {
                (string name, CancellationTokenSource stop) = _bots[^1];
                _bots.RemoveAt(_bots.Count - 1);
                stop.Cancel();
                Log.Info(Tag, $"{name} is leaving");
            }
            while (_bots.Count < count)
            {
                List<string> names = _context.Data.Bots.Names;
                int index = _bots.Count;
                string name = EnterWorldHandler.BotPrefix + names[index % names.Count] + (index < names.Count ? "" : index.ToString());
                var stop = CancellationTokenSource.CreateLinkedTokenSource(serverStopping);
                _bots.Add((name, stop));
                _ = RunBotAsync(name, index, stop.Token);
            }
        }
    }

    private async Task RunBotAsync(string name, int index, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(700 * index + 300, cancellationToken);
            await new Bot(_context, name).PlayAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            Log.Warn(Tag, $"{name} stopped: {exception.Message}");
        }
        catch (Exception exception)
        {
            Log.Error(Tag, $"{name} failed: {exception}");
        }
    }

    private sealed class Bot
    {
        private readonly ServerContext _context;
        private readonly string _name;
        private readonly MessageDeframer _deframer = new();
        private readonly Dictionary<int, int> _received = [];
        private NetworkStream _stream = null!;

        public Bot(ServerContext context, string name)
        {
            _context = context;
            _name = name;
        }

        public async Task PlayAsync(CancellationToken cancellationToken)
        {
            GameData data = _context.Data;
            var world = _context.Settings.World;
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync("127.0.0.1", _context.Settings.GamePort, cancellationToken);
            _stream = client.GetStream();
            Task reader = ReadAsync(cancellationToken);

            await Task.Delay(500, cancellationToken);
            await SendAsync(new PacketWriter(ClientMsg.HandshakeReply).WriteZeros(52), cancellationToken);
            await Task.Delay(500, cancellationToken);
            await SendAsync(new PacketWriter(ClientMsg.EnterWorld).WriteZeros(16).WriteFixedString(_name, 32).WriteByte(0), cancellationToken);
            await Task.Delay(1500, cancellationToken);
            await SendAsync(new PacketWriter(ClientMsg.SceneLoaded), cancellationToken);
            Log.Info(Tag, $"{_name} entered the world");

            List<NpcDefinition> shops = data.Npcs.Values.Where(npc => npc.Shop != null && npc.Scene == world.StartScene).ToList();
            List<MonsterDefinition> monsters = data.Monsters.Values.Where(monster => monster.Scene == world.StartScene).ToList();
            var random = new Random();
            try
            {
                while (!cancellationToken.IsCancellationRequested && !reader.IsCompleted)
                {
                    float x = world.StartX + (float)(random.NextDouble() * 22 - 10);
                    float z = world.StartZ + (float)(random.NextDouble() * 18 - 4);
                    await SendAsync(new PacketWriter(ClientMsg.Move).WriteSingle(x).WriteSingle(z), cancellationToken);
                    await Task.Delay(random.Next(3000, 7000), cancellationToken);

                    double roll = random.NextDouble();
                    string? reply = Interlocked.Exchange(ref _replyField, null);
                    if (reply != null)
                    {
                        await CommandAsync(reply, cancellationToken);
                    }
                    else if (roll < 0.25)
                    {
                        await CommandAsync(data.Bots.Lines[random.Next(data.Bots.Lines.Count)], cancellationToken);
                    }
                    else if (roll < 0.35 && shops.Count > 0)
                    {
                        NpcDefinition npc = shops[random.Next(shops.Count)];
                        await InteractAsync(npc.Id, cancellationToken);
                        await Task.Delay(500, cancellationToken);
                        await OptionAsync(npc.Id, DialogueAction.Trade, cancellationToken);
                        await Task.Delay(500, cancellationToken);
                        await CommandAsync($"Select {npc.Shop!.Keys.First()} 0 1", cancellationToken);
                        await OptionAsync(npc.Id, DialogueAction.Bye, cancellationToken);
                    }
                    else if (roll < 0.45 && monsters.Count > 0)
                    {
                        await InteractAsync(monsters[random.Next(monsters.Count)].Id, cancellationToken);
                    }
                    else if (roll < 0.5 && data.Bots.Emotes.Count > 0)
                    {
                        await CommandAsync($"DoAction {data.Bots.Emotes[random.Next(data.Bots.Emotes.Count)]}", cancellationToken);
                    }
                }
            }
            finally
            {
                string seen = string.Join(", ", _received.OrderBy(pair => pair.Key).Select(pair => $"0x{pair.Key:x2}:{pair.Value}"));
                Log.Info(Tag, $"{_name} left; messages received: {seen}");
            }
        }

        private string? _replyField;

        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[65536];
            try
            {
                while (true)
                {
                    int read = await _stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0) return;
                    foreach (byte[] message in _deframer.Feed(buffer.AsSpan(0, read)))
                        OnMessage(message);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }

        private void OnMessage(byte[] message)
        {
            if (message.Length < 2) return;
            int id = message[0] | message[1] << 8;
            lock (_received) _received[id] = _received.GetValueOrDefault(id) + 1;
            if (id != (int)ServerMsg.Chat || message.Length < 7) return;

            uint speaker = BitConverter.ToUInt32(message, 2);
            string text = Encoding.Latin1.GetString(message, 6, message.Length - 6).Split('\0')[0];
            if (speaker > ProtocolConstants.FirstPlayerObjectId
                && !text.Contains(EnterWorldHandler.BotPrefix, StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("Hello there", StringComparison.Ordinal)
                && !_context.Data.Bots.Lines.Contains(text)
                && Random.Shared.NextDouble() < 0.5)
            {
                Interlocked.Exchange(ref _replyField, $"Hello there! You said: {(text.Length > 30 ? text[..30] : text)}");
            }
        }

        private Task CommandAsync(string text, CancellationToken cancellationToken)
            => SendAsync(new PacketWriter(ClientMsg.TextCommand).WriteCString(text), cancellationToken);

        private Task InteractAsync(int objectId, CancellationToken cancellationToken)
            => SendAsync(new PacketWriter(ClientMsg.Interact).WriteZeros(16).WriteUInt32(0).WriteUInt32((uint)objectId).WriteUInt32(0).WriteByte(0),
                cancellationToken);

        private Task OptionAsync(int objectId, DialogueAction action, CancellationToken cancellationToken)
            => SendAsync(new PacketWriter(ClientMsg.DialogueOption).WriteUInt32((uint)objectId).WriteUInt32(0).WriteUInt16((ushort)action),
                cancellationToken);

        private async Task SendAsync(PacketWriter writer, CancellationToken cancellationToken)
            => await _stream.WriteAsync(MessageFramer.Frame(writer.ToArray()), cancellationToken);
    }
}
