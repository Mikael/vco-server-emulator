using BountyBay.Core.Network;

namespace BountyBay.Core.Protocol.Packets;

public static class TablePackets
{
    public static byte[] Rows(TableDefinition table, IReadOnlyList<object[]> rows)
    {
        var writer = new PacketWriter(ServerMsg.TableRows).WriteByte(table.Id).WriteUInt16((ushort)rows.Count);
        foreach (object[] row in rows)
        {
            for (int i = 0; i < table.Columns.Length; i++)
                PropertyTable.WriteValue(writer, table.Columns[i].Type, row[i]);
        }
        return writer.ToArray();
    }

    public static byte[] UpdateCell(TableDefinition table, int row, int column, object value)
    {
        var writer = new PacketWriter(ServerMsg.TableUpdateRow).WriteByte(table.Id).WriteUInt16(1)
            .WriteInt16((short)row).WriteByte((byte)column);
        return PropertyTable.WriteValue(writer, table.Columns[column].Type, value).ToArray();
    }

    public static byte[] DeleteRow(TableDefinition table, int row)
        => new PacketWriter(ServerMsg.TableDeleteRow).WriteByte(table.Id).WriteUInt16((ushort)row).ToArray();

    public static byte[] Clear(TableDefinition table) => new PacketWriter(ServerMsg.TableClear).WriteByte(table.Id).ToArray();
}
