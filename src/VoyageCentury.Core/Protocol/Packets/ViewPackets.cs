using VoyageCentury.Core.Network;

namespace VoyageCentury.Core.Protocol.Packets;

public static class ViewPackets
{
    public static byte[] Create(int view, int capacity)
        => new PacketWriter(ServerMsg.ViewCreate).WriteByte((byte)view).WriteByte(0).WriteUInt16((ushort)capacity).ToArray();

    public static byte[] Delete(int view) => new PacketWriter(ServerMsg.ViewDelete).WriteByte((byte)view).ToArray();

    public static byte[] AddItem(int view, int index, IReadOnlyDictionary<string, object> values)
        => PropertyTable.WriteValues(
            new PacketWriter(ServerMsg.ViewAddItem).WriteByte((byte)view).WriteUInt32(0).WriteUInt32((uint)index), values).ToArray();

    public static byte[] UpdateItem(int view, int index, IReadOnlyDictionary<string, object> values)
        => PropertyTable.WriteValues(
            new PacketWriter(ServerMsg.ViewUpdateItem).WriteByte((byte)view).WriteUInt32((uint)index), values).ToArray();

    public static byte[] RemoveItem(int view, int index)
        => new PacketWriter(ServerMsg.ViewRemoveItem).WriteByte((byte)view).WriteUInt32((uint)index).ToArray();
}
