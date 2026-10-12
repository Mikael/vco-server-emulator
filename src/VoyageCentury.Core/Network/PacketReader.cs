using System.Buffers.Binary;
using System.Text;

namespace VoyageCentury.Core.Network;

public sealed class PacketReader
{
    private readonly byte[] _data;

    public PacketReader(byte[] data, int offset = 0)
    {
        _data = data;
        Position = offset;
    }

    public int Position { get; private set; }

    public int Length => _data.Length;

    public int Remaining => _data.Length - Position;

    public bool EndOfData => Position >= _data.Length;

    public byte[] Data => _data;

    public PacketReader Seek(int offset)
    {
        Position = offset;
        return this;
    }

    public byte ReadByte() => Take(1)[0];

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public byte[] ReadBytes(int count) => Take(count).ToArray();

    public string ReadFixedString(int size)
    {
        ReadOnlySpan<byte> field = Take(size);
        int end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
    }

    public string ReadCString()
    {
        ReadOnlySpan<byte> rest = _data.AsSpan(Math.Min(Position, _data.Length));
        int end = rest.IndexOf((byte)0);
        string text = Encoding.Latin1.GetString(end < 0 ? rest : rest[..end]);
        Position = end < 0 ? _data.Length : Position + end + 1;
        return text;
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (Position < 0 || Position + count > _data.Length)
            throw new EndOfStreamException($"needs {count} byte(s) at offset 0x{Position:x}, message has {_data.Length}");
        ReadOnlySpan<byte> span = _data.AsSpan(Position, count);
        Position += count;
        return span;
    }
}
