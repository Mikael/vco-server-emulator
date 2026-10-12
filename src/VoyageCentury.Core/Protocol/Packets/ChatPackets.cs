using VoyageCentury.Core.Network;

namespace VoyageCentury.Core.Protocol.Packets;

public static class ChatPackets
{
    public static byte[] Chat(int speakerId, string text)
        => new PacketWriter(ServerMsg.Chat).WriteUInt32((uint)speakerId).WriteCString(text).ToArray();

    public static byte[] SystemMessage(string text, SystemMessageKind kind = SystemMessageKind.Information)
        => new PacketWriter(ServerMsg.SystemMessage).WriteByte((byte)kind).WriteCString(text).ToArray();

    public static byte[] Script(int objectId, string text, int secondObjectId = 0)
        => new PacketWriter(ServerMsg.Script).WriteUInt32((uint)objectId).WriteUInt32((uint)secondObjectId).WriteCString(text).ToArray();

    public static byte[] FloatingText(int objectId, string text, uint colour, string? effect = null, float x = 0, float y = 0, float z = 0)
    {
        string script = $"3DA\ninfo {text}\ncolo {colour & 0xffffff:x6}\n";
        if (effect != null)
        {
            script += string.Create(System.Globalization.CultureInfo.InvariantCulture, $"weff {effect}\nx {x:F2}\ny {y:F2}\nz {z:F2}\n");
        }
        return Script(objectId, script);
    }

    public static byte[] MessageBox(string text) => Script(0, "MBox " + text);

    public static byte[] PlaySound(string name) => Script(0, "PlaySound " + name);

    public static byte[] ServiceChat(string sender, string text)
        => new PacketWriter(ServerMsg.ServiceChat).WriteUInt32(0).WriteFixedString(sender, 32).WriteCString(text).ToArray();

    public static byte[] NpcDialogue(int objectId, IEnumerable<(DialogueEntry Type, int Number, string Text)> entries)
    {
        List<(DialogueEntry Type, int Number, string Text)> list = entries.ToList();
        var writer = new PacketWriter(ServerMsg.NpcDialogue).WriteUInt32((uint)objectId).WriteUInt32(0).WriteByte((byte)Math.Min(list.Count, 255));
        foreach ((DialogueEntry type, int number, string text) in list)
        {
            byte[] bytes = PacketWriter.Latin1(text);
            writer.WriteByte((byte)type).WriteInt16((short)number).WriteInt16((short)bytes.Length).WriteBytes(bytes);
        }
        return writer.WriteByte(0).WriteInt16(0).WriteInt16(0).WriteByte(0).ToArray();
    }
}
