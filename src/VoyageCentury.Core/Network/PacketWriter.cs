using System.Buffers.Binary;
using VoyageCentury.Core.Protocol;

namespace VoyageCentury.Core.Network;

public sealed class PacketWriter
{
    private readonly MemoryStream _stream = new();

    public PacketWriter()
    {
    }

    public PacketWriter(ServerMsg id) => WriteUInt16((ushort)id);

    public PacketWriter(ClientMsg id) => WriteUInt16((ushort)id);

    public int Length => (int)_stream.Length;

    public PacketWriter WriteByte(byte value)
    {
        _stream.WriteByte(value);
        return this;
    }

    public PacketWriter WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _stream.Write(buffer);
        return this;
    }

    public PacketWriter WriteInt16(short value) => WriteUInt16(unchecked((ushort)value));

    public PacketWriter WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
        return this;
    }

    public PacketWriter WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        _stream.Write(buffer);
        return this;
    }

    public PacketWriter WriteBytes(ReadOnlySpan<byte> value)
    {
        _stream.Write(value);
        return this;
    }

    public PacketWriter WriteZeros(int count) => WriteBytes(new byte[count]);

    public PacketWriter WriteFixedString(string value, int size)
    {
        byte[] field = new byte[size];
        byte[] text = Latin1(value);
        Array.Copy(text, field, Math.Min(text.Length, size));
        return WriteBytes(field);
    }

    public PacketWriter WriteCString(string value) => WriteBytes(Latin1(value)).WriteByte(0);

    public PacketWriter WriteShortString(string value)
    {
        byte[] text = Latin1(value);
        int length = Math.Min(text.Length, 255);
        return WriteByte((byte)length).WriteBytes(text.AsSpan(0, length));
    }

    public PacketWriter WriteLongString(string value)
    {
        byte[] text = Latin1(value);
        return WriteUInt16((ushort)text.Length).WriteBytes(text);
    }

    public byte[] ToArray() => _stream.ToArray();

    public static byte[] Latin1(string value)
    {
        byte[] bytes = new byte[value.Length];
        for (int i = 0; i < value.Length; i++)
            bytes[i] = value[i] < 256 ? (byte)value[i] : (byte)'?';
        return bytes;
    }
}
