using System.Buffers.Binary;
using BountyBay.Core.Data;
using BountyBay.Core.Network;
using BountyBay.Core.Persistence;

namespace BountyBay.Core.Protocol.Packets;

public static class LoginPackets
{
    public static byte[] Handshake()
    {
        var writer = new PacketWriter(ServerMsg.Handshake)
            .WriteUInt32(ProtocolConstants.SessionValue)
            .WriteUInt32(ProtocolConstants.NoObfuscation)
            .WriteUInt32(ProtocolConstants.HandshakeEcho);
        for (int i = 0; i < 32; i++) writer.WriteByte((byte)i);
        return writer.WriteZeros(20).ToArray();
    }

    public static byte[] ServerList(string name, string address, int port)
        => new PacketWriter(ServerMsg.ServerList)
            .WriteUInt32(1)
            .WriteFixedString(name, 32)
            .WriteUInt32(1)
            .WriteFixedString(address, 16)
            .WriteUInt32((uint)port)
            .ToArray();

    public static byte[] VerifyPicture(int width = 64, int height = 20)
    {
        var writer = new PacketWriter(ServerMsg.VerifyPicture).WriteUInt32((uint)width).WriteUInt32((uint)height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                writer.WriteUInt32((x / 4 + y / 4) % 2 == 1 ? 0x00FFD060u : 0x00203040u);
        }
        return writer.ToArray();
    }

    public static byte[] Result(byte code) => new PacketWriter(ServerMsg.Result).WriteByte(code).ToArray();

    public static byte[] CharacterList(IReadOnlyList<CharacterRecord> characters)
    {
        byte[] body = new byte[ProtocolConstants.CharacterListSize];
        body[0] = (byte)ServerMsg.CharacterList;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x02, 4), ProtocolConstants.FreeGamePoints);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x06, 4), ProtocolConstants.FreePlayDeadline);
        body[0x0A] = (byte)Math.Min(characters.Count, ProtocolConstants.CharacterSlots);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x0B, 4), ProtocolConstants.DynamicKey);
        for (int i = 0; i < Math.Min(characters.Count, ProtocolConstants.CharacterSlots); i++)
        {
            CharacterRecord character = characters[i];
            int slot = ProtocolConstants.CharacterSlotsOffset + ProtocolConstants.CharacterSlotSize * i;
            byte[] name = PacketWriter.Latin1(character.Name);
            Array.Copy(name, 0, body, slot, Math.Min(name.Length, 31));
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(slot + 0x30, 4), (uint)Math.Max(1, character.Skills.Sum(skill => skill.Level)));
            WornLook worn = WornLook.Of(character);
            uint[] look =
            [
                (uint)character.Role, (uint)character.Face, (uint)worn.Body, (uint)worn.TexColor, (uint)worn.Hat, (uint)worn.CapColor, 0, 0,
                (uint)character.HairColor, (uint)character.Hair,
            ];
            for (int k = 0; k < look.Length; k++)
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(slot + 0x34 + 4 * k, 4), look[k]);
        }
        return body;
    }

    public static byte[] CreateInfo(CreationData creation)
    {
        var writer = new PacketWriter(ServerMsg.CreateInfo)
            .WriteUInt32((uint)creation.Skills.Count)
            .WriteUInt32((uint)creation.Roles.Count)
            .WriteUInt32((uint)creation.Faces.Count)
            .WriteUInt32((uint)creation.Bodies.Count)
            .WriteUInt32((uint)creation.Skills.Count)
            .WriteUInt32((uint)creation.Cities.Count);
        foreach (CreationChoice role in creation.Roles) writer.WriteUInt32((uint)role.Id).WriteUInt32((uint)role.Sex);
        foreach (CreationChoice face in creation.Faces) writer.WriteUInt32((uint)face.Id).WriteUInt32((uint)face.Sex);
        foreach (CreationChoice body in creation.Bodies) writer.WriteUInt32((uint)body.Id).WriteUInt32((uint)body.Sex);
        for (int i = 0; i < creation.Skills.Count; i++) writer.WriteUInt32((uint)i).WriteUInt32(0);
        foreach (int city in creation.Cities) writer.WriteUInt32((uint)city).WriteUInt32(0);
        return writer.ToArray();
    }

    public static byte[] PropertyTableDefinition()
    {
        var writer = new PacketWriter(ServerMsg.PropertyTable).WriteUInt16((ushort)PropertyTable.Definitions.Count);
        foreach (PropertyDefinition definition in PropertyTable.Definitions)
            writer.WriteFixedString(definition.Name, 16).WriteByte((byte)definition.Type);
        return writer.ToArray();
    }

    public static byte[] OwnProperties(IReadOnlyDictionary<string, object> values)
        => PropertyTable.WriteValues(new PacketWriter(ServerMsg.OwnProperties), values).ToArray();

    public static byte[] TableCreate(TableDefinition table)
    {
        var writer = new PacketWriter(ServerMsg.TableCreate)
            .WriteFixedString(table.Name, 16)
            .WriteByte(table.Id)
            .WriteByte((byte)table.Columns.Length);
        foreach (TableColumn column in table.Columns)
            writer.WriteFixedString(column.Name, 16).WriteByte((byte)column.Type);
        return writer.ToArray();
    }
}
