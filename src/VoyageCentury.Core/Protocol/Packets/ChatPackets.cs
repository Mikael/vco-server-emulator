using VoyageCentury.Core.Network;

namespace VoyageCentury.Core.Protocol.Packets;

public static class ChatPackets
{
    public static byte[] Chat(int speakerId, string text)
        => new PacketWriter(ServerMsg.Chat).WriteUInt32((uint)speakerId).WriteCString(text).ToArray();

    public static byte[] SystemMessage(string text, SystemMessageKind kind = SystemMessageKind.Information)
        => new PacketWriter(ServerMsg.SystemMessage).WriteByte((byte)kind).WriteCString(text).ToArray();

    public static byte[] NpcDialogue(int objectId, IEnumerable<(DialogueEntry Type, int Number, string Text)> entries)
    {
        var writer = new PacketWriter(ServerMsg.NpcDialogue).WriteUInt32((uint)objectId).WriteUInt32(0).WriteByte(3);
        foreach ((DialogueEntry type, int number, string text) in entries)
        {
            byte[] bytes = PacketWriter.Latin1(text);
            writer.WriteByte((byte)type).WriteInt16((short)number).WriteInt16((short)bytes.Length).WriteBytes(bytes);
        }
        return writer.WriteByte(0).WriteInt16(0).WriteInt16(0).WriteByte(0).ToArray();
    }
}
