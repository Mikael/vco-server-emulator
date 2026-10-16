using System.Net.Sockets;
using BountyBay.Core.Logging;
using BountyBay.Core.Network;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private readonly GameConnection _connection;
    private bool _closed;

    public ClientSession(GameConnection connection, ServerContext context)
    {
        _connection = connection;
        Context = context;
        ObjectId = context.World.NextObjectId();
    }

    public ServerContext Context { get; }

    public string Tag => _connection.Tag;

    public int Port => _connection.Port;

    public int ObjectId { get; }

    public string Account { get; set; } = "";

    public CharacterRecord Character { get; private set; } = new();

    public bool Persistent { get; private set; }

    public string Name => Character.Name;

    public PropertyBag Properties { get; private set; } = [];

    public Mover? Mover { get; private set; }

    public bool InWorld => Mover != null;

    public int Scene { get; private set; }

    public float Height { get; private set; }

    public bool AtSea { get; private set; }

    public bool ForceLand { get; set; }

    public float Sail { get; set; } = 1f;

    public int Hp { get; private set; } = 100;

    public int MaxHp { get; private set; } = 100;

    public int Money => Character.Money;

    public int TalkingTo { get; set; }

    public async Task RunAsync(MessageRouter router, CancellationToken cancellationToken)
    {
        Send(LoginPackets.Handshake(), "(handshake)");
        try
        {
            await foreach (byte[] message in _connection.ReadMessagesAsync(cancellationToken))
                router.Dispatch(this, message);
        }
        finally
        {
            lock (Context.World.Sync) Close();
        }
    }

    public void Send(byte[] body, string note = "")
    {
        if (_closed) return;
        Log.Packet(Tag, "SEND", body, note);
        try
        {
            _connection.Send(body);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            _closed = true;
        }
    }

    public IEnumerable<ClientSession> Others()
        => Context.World.Sessions.Where(other => other != this && other.Scene == Scene && other.InWorld).ToList();

    public void ToOthers(byte[] body, string note)
    {
        foreach (ClientSession other in Others()) other.Send(body, note);
    }

    public void SystemMessage(string text, SystemMessageKind kind = SystemMessageKind.Information)
        => Send(ChatPackets.SystemMessage(text, kind), "(system message)");

    public void Save()
    {
        if (Persistent) Context.Accounts.Save();
    }

    private void Close()
    {
        if (InWorld)
        {
            LeaveWorldSocial();
            LeaveWorldGuild();
            LeaveScene();
            (float x, float z) = Mover!.Now();
            Character.Position = [MathF.Round(x, 2), MathF.Round(Height, 2), MathF.Round(z, 2)];
            Character.Scene = Scene;
            BattleLeaveWorld();
            Save();
            Log.Info(Tag, $"{Name} left the world");
            Mover = null;
        }
        _closed = true;
    }
}
