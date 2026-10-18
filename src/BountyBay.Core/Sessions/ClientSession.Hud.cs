using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private static readonly Dictionary<int, string> HudChatChannels = new()
    {
        [BboChatKind.Popular] = "popular",
        [BboChatKind.Newbie] = "newbie",
        [BboChatKind.Trumpet] = "trumpet",
    };

    public void PrepareHudProperties()
    {
        ApplyVipProperty();
    }

    public void AnswerPing(string counter)
        => Send(HudPackets.NetPingAck(int.TryParse(counter, out int value) ? value : 0), "(ping reply)");

    public void SendVisibleTable(string table)
    {
        if (table.Equals(GameTables.Shortcut.Name, StringComparison.OrdinalIgnoreCase))
        {
            SendShortcuts(clear: true);
            return;
        }
        Log.Debug(Tag, $"{Name} asks for table '{table}', which the server does not keep");
    }

    public void HudChat(int kind, string text)
    {
        if (text.Length == 0 || !HudChatChannels.TryGetValue(kind, out string? channel)) return;
        if (!ChatAllowed(channel, ref text)) return;
        byte[] line = HudPackets.ChannelChat(kind, Name, ChatTitle, text);
        foreach (ClientSession other in Context.World.Sessions.ToList())
        {
            if (other.InWorld && !Listed(other.Character.Blacklist, Name)) other.Send(line, $"({channel} chat from {Name})");
        }
        Log.Info(Tag, $"{Name} on {channel}: {text}");
    }
}
