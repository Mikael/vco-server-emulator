using System.Buffers.Binary;
using System.Text;

namespace BountyBay.Core.Data;

public sealed class IniFile
{
    private static readonly byte[] Magic = [0x5A, 0x04, 0x01, 0x04, 0x03, 0x07, 0x07, 0x07];

    private IniFile(List<IniSection> sections) => Sections = sections;

    public List<IniSection> Sections { get; }

    public IniSection Header => Sections.Count > 0 && Sections[0].Name.Length == 0 ? Sections[0] : new IniSection("");

    public static IniFile Load(string path) => Parse(File.ReadAllBytes(path));

    public static IniFile Parse(byte[] data)
        => data.Length >= 10 && data.AsSpan(0, 8).SequenceEqual(Magic) ? ParseBinary(data) : ParseText(Encoding.Latin1.GetString(data));

    public IEnumerable<IniSection> Named(string prefix)
        => Sections.Where(section => section.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IniFile ParseBinary(byte[] data)
    {
        int offset = 10;
        int sectionBytes = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        int sectionStart = offset + 4;
        offset = sectionStart + sectionBytes;
        int keyBytes = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        int keyStart = offset + 4;
        offset = keyStart + keyBytes;
        int poolStart = offset + 4;

        string Text(int position)
        {
            if (position < 0) return "";
            int start = poolStart + position;
            int end = Array.IndexOf(data, (byte)0, start);
            return Encoding.Latin1.GetString(data, start, (end < 0 ? data.Length : end) - start);
        }

        var sections = new List<IniSection>();
        for (int s = 0; s < sectionBytes / 12; s++)
        {
            int at = sectionStart + 12 * s;
            int nameOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            int firstKey = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4));
            int keyCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 8));
            var section = new IniSection(Text(nameOffset));
            for (int k = firstKey; k < firstKey + keyCount && k < keyBytes / 8; k++)
            {
                int keyAt = keyStart + 8 * k;
                int keyName = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(keyAt));
                int keyValue = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(keyAt + 4));
                section.Add(Text(keyName), keyValue < 0 ? null : Text(keyValue));
            }
            sections.Add(section);
        }
        return new IniFile(sections);
    }

    private static IniFile ParseText(string text)
    {
        var sections = new List<IniSection> { new("") };
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.StartsWith('[') && line.Contains(']'))
            {
                sections.Add(new IniSection(line[1..line.IndexOf(']')].Trim()));
                continue;
            }
            int equals = line.IndexOf('=');
            if (equals < 0) sections[^1].Add(line, null);
            else sections[^1].Add(line[..equals].Trim(), line[(equals + 1)..].Trim());
        }
        return new IniFile(sections);
    }
}

public sealed class IniSection
{
    private readonly List<KeyValuePair<string, string?>> _entries = [];

    public IniSection(string name) => Name = name;

    public string Name { get; }

    public IReadOnlyList<KeyValuePair<string, string?>> Entries => _entries;

    public void Add(string key, string? value) => _entries.Add(new KeyValuePair<string, string?>(key, value));

    public bool HasFlag(string key) => _entries.Any(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public string? this[string key]
        => _entries.FirstOrDefault(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && entry.Value != null).Value;
}
