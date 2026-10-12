using System.Text.Json;
using VoyageCentury.Core.Configuration;
using VoyageCentury.Core.Protocol;

namespace VoyageCentury.Core.Data;

public static class ItemCatalog
{
    private static readonly object Gate = new();
    private static Dictionary<string, Dictionary<string, object>>? _items;
    private static IReadOnlyList<PropertyDefinition>? _typesFrom;
    private static Dictionary<string, PropertyType> _types = [];

    public static void Load(EmulatorSettings settings)
    {
        lock (Gate) _items = Read(settings.Resolve("data\\game\\itemprops.json"));
    }

    public static IReadOnlyDictionary<string, object>? Find(string itemName)
        => Items().TryGetValue(itemName, out Dictionary<string, object>? properties) ? properties : null;

    public static int Number(string itemName, string property, int fallback = 0)
        => Find(itemName) is { } properties && properties.TryGetValue(property, out object? value) && value is not string
            ? Convert.ToInt32(value)
            : fallback;

    public static PropertyBag Extend(PropertyBag bag, string itemName)
    {
        if (Find(itemName) is not { } properties) return bag;
        foreach ((string name, object value) in properties)
        {
            if (Fits(name, value)) bag[name] = value;
        }
        return bag;
    }

    private static bool Fits(string name, object value)
    {
        if (!PropertyTable.Has(name)) return false;
        Dictionary<string, PropertyType> types;
        lock (Gate)
        {
            if (!ReferenceEquals(_typesFrom, PropertyTable.Definitions))
            {
                _typesFrom = PropertyTable.Definitions;
                _types = [];
                foreach (PropertyDefinition definition in _typesFrom) _types[definition.Name] = definition.Type;
            }
            types = _types;
        }
        if (!types.TryGetValue(name, out PropertyType type)) return false;
        bool text = type is PropertyType.String or PropertyType.LongString;
        return text || value is not string;
    }

    private static Dictionary<string, Dictionary<string, object>> Items()
    {
        lock (Gate)
        {
            if (_items != null) return _items;
            try
            {
                _items = Read(EmulatorSettings.Load().Resolve("data\\game\\itemprops.json"));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                _items = [];
            }
            return _items;
        }
    }

    private static Dictionary<string, Dictionary<string, object>> Read(string path)
    {
        var items = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        if (!File.Exists(path)) return items;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object) return items;
        foreach (JsonProperty item in document.RootElement.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Object) continue;
            var properties = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (JsonProperty property in item.Value.EnumerateObject())
            {
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Number:
                        properties[property.Name] = property.Value.TryGetInt64(out long integer) ? integer : property.Value.GetDouble();
                        break;
                    case JsonValueKind.String:
                        properties[property.Name] = property.Value.GetString() ?? "";
                        break;
                    case JsonValueKind.True or JsonValueKind.False:
                        properties[property.Name] = property.Value.GetBoolean() ? 1L : 0L;
                        break;
                }
            }
            items[item.Name] = properties;
        }
        return items;
    }
}
