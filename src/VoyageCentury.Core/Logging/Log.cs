using System.Text;

namespace VoyageCentury.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

public static class Log
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> MissingSeen = [];
    private static StreamWriter? _file;

    public static LogLevel ConsoleLevel { get; set; } = LogLevel.Info;

    public static bool PacketsToConsole { get; set; }

    public static void Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lock (Gate)
        {
            _file?.Dispose();
            _file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        }
    }

    public static void Debug(string tag, string message) => Write(LogLevel.Debug, tag, message, null);

    public static void Info(string tag, string message) => Write(LogLevel.Info, tag, message, null);

    public static void Warn(string tag, string message) => Write(LogLevel.Warn, tag, message, null);

    public static void Error(string tag, string message) => Write(LogLevel.Error, tag, message, null);

    public static void Error(string tag, Exception exception) => Write(LogLevel.Error, tag, exception.ToString(), null);

    public static void Packet(string tag, string direction, ReadOnlySpan<byte> body, string note)
    {
        int id = body.Length >= 2 ? body[0] | body[1] << 8 : -1;
        string text = $"{direction} id=0x{id:x4} len={body.Length} {note}\n{HexDump(body)}";
        Write(LogLevel.Debug, tag, text, PacketsToConsole);
    }

    public static void Missing(string tag, string what, ReadOnlySpan<byte> body)
    {
        int seen;
        lock (Gate)
        {
            MissingSeen.TryGetValue(what, out seen);
            MissingSeen[what] = ++seen;
        }
        string text = $"MISSING {what} (seen {seen} time{(seen == 1 ? "" : "s")})";
        if (body.Length > 0 && seen <= 3) text += "\n" + HexDump(body);
        Write(LogLevel.Warn, tag, text, true);
    }

    public static IReadOnlyList<KeyValuePair<string, int>> MissingSummary()
    {
        lock (Gate) return MissingSeen.OrderByDescending(pair => pair.Value).ToList();
    }

    public static string HexDump(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder();
        for (int offset = 0; offset < data.Length; offset += 16)
        {
            ReadOnlySpan<byte> row = data.Slice(offset, Math.Min(16, data.Length - offset));
            var hex = new StringBuilder();
            var chars = new StringBuilder();
            foreach (byte value in row)
            {
                hex.Append(value.ToString("x2")).Append(' ');
                chars.Append(value is >= 32 and < 127 ? (char)value : '.');
            }
            if (offset > 0) text.Append('\n');
            text.Append($"    {offset:x4}  {hex.ToString().TrimEnd(),-47}  {chars}");
        }
        return text.ToString();
    }

    private static void Write(LogLevel level, string tag, string message, bool? console)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-5} {tag} {message}";
        bool toConsole = console ?? level >= ConsoleLevel;
        lock (Gate)
        {
            if (toConsole)
            {
                ConsoleColor previous = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Warn => ConsoleColor.Yellow,
                    LogLevel.Debug => ConsoleColor.DarkGray,
                    _ => previous,
                };
                Console.WriteLine(line);
                Console.ForegroundColor = previous;
            }
            _file?.WriteLine(line);
        }
    }
}
