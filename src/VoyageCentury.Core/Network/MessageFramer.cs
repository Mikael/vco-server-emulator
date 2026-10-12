namespace VoyageCentury.Core.Network;

public static class MessageFramer
{
    public const byte Marker = 0xEE;

    public static byte[] Frame(ReadOnlySpan<byte> body)
    {
        var framed = new List<byte>(body.Length + 8);
        foreach (byte value in body)
        {
            framed.Add(value);
            if (value == Marker) framed.Add(0);
        }
        framed.Add(Marker);
        framed.Add(Marker);
        return framed.ToArray();
    }
}

public sealed class MessageDeframer
{
    private readonly List<byte> _buffer = [];
    private byte _previous;

    public List<byte[]> Feed(ReadOnlySpan<byte> data)
    {
        var messages = new List<byte[]>();
        foreach (byte value in data)
        {
            if (value == MessageFramer.Marker && _previous == MessageFramer.Marker)
            {
                if (_buffer.Count > 1) messages.Add(_buffer.GetRange(0, _buffer.Count - 1).ToArray());
                _buffer.Clear();
                _previous = 0;
                continue;
            }
            if (!(value == 0 && _previous == MessageFramer.Marker)) _buffer.Add(value);
            _previous = value;
        }
        return messages;
    }
}
