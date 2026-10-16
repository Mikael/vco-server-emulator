using BountyBay.Core.Network;

namespace BountyBay.Core.Protocol;

public enum PropertyType : byte
{
    U8 = 0,
    U16 = 1,
    U32 = 2,
    Float = 3,
    String = 4,
    LongString = 5,
}

public sealed class PropertyDefinition
{
    public string Name { get; set; } = "";
    public PropertyType Type { get; set; }
}

public sealed class PropertyBag : Dictionary<string, object>
{
    public PropertyBag()
    {
    }

    public PropertyBag(IDictionary<string, object> source) : base(source)
    {
    }

    public int Int(string name, int fallback = 0) => TryGetValue(name, out object? value) ? Convert.ToInt32(value) : fallback;

    public string Text(string name, string fallback = "") => TryGetValue(name, out object? value) ? Convert.ToString(value) ?? fallback : fallback;
}

public static class PropertyTable
{
    private static readonly Dictionary<string, int> Index = [];

    public static IReadOnlyList<PropertyDefinition> Definitions { get; private set; } = [];

    public static void Define(IReadOnlyList<PropertyDefinition> definitions)
    {
        Definitions = definitions;
        Index.Clear();
        for (int i = 0; i < definitions.Count; i++) Index[definitions[i].Name] = i;
    }

    public static bool Has(string name) => Index.ContainsKey(name);

    public static object? Parse(string name, string text)
    {
        if (!Index.TryGetValue(name, out int index)) return null;
        switch (Definitions[index].Type)
        {
            case PropertyType.String or PropertyType.LongString:
                return text;
            case PropertyType.Float:
                return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number) ? number : null;
            default:
                return long.TryParse(text, out long integer) ? integer : null;
        }
    }

    public static PacketWriter WriteValues(PacketWriter writer, IReadOnlyDictionary<string, object> values)
    {
        writer.WriteUInt16((ushort)values.Count);
        foreach ((string name, object value) in values)
        {
            if (!Index.TryGetValue(name, out int index))
                throw new InvalidOperationException($"property '{name}' is not in data\\game\\properties.json");
            writer.WriteUInt16((ushort)index);
            WriteValue(writer, Definitions[index].Type, value);
        }
        return writer;
    }

    public static PacketWriter WriteValue(PacketWriter writer, PropertyType type, object value)
    {
        switch (type)
        {
            case PropertyType.U8:
                writer.WriteByte(unchecked((byte)Convert.ToInt64(value)));
                break;
            case PropertyType.U16:
                writer.WriteUInt16(unchecked((ushort)Convert.ToInt64(value)));
                break;
            case PropertyType.U32:
                writer.WriteUInt32(unchecked((uint)Convert.ToInt64(value)));
                break;
            case PropertyType.Float:
                writer.WriteSingle(Convert.ToSingle(value));
                break;
            case PropertyType.String:
                writer.WriteShortString(Convert.ToString(value) ?? "");
                break;
            default:
                writer.WriteLongString(Convert.ToString(value) ?? "");
                break;
        }
        return writer;
    }
}
