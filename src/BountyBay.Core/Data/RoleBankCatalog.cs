using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class RoleBankArticle
{
    public string Item { get; set; } = "";
    public string Label { get; set; } = "";
    public int Photo { get; set; }
}

public sealed class RoleBankData
{
    public int Table { get; set; } = 111;
    public int Indexes { get; set; } = 6;
    public int EntriesPerIndex { get; set; } = 90;
    public int NameLength { get; set; } = 3;
    public List<RoleBankArticle> Items { get; set; } = [];

    private Dictionary<string, RoleBankArticle>? _byItem;

    public RoleBankArticle? Find(string item)
    {
        _byItem ??= Items.GroupBy(article => article.Item).ToDictionary(group => group.Key, group => group.First());
        return _byItem.TryGetValue(item, out RoleBankArticle? article) ? article : null;
    }
}

public static class RoleBankCatalog
{
    private static readonly object Gate = new();
    private static RoleBankData? _data;

    public static RoleBankData Get(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_data != null) return _data;
            string path = settings.Resolve("data\\game\\RoleBankItems.json");
            try
            {
                _data = File.Exists(path)
                    ? JsonSerializer.Deserialize<RoleBankData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new RoleBankData()
                    : new RoleBankData();
            }
            catch (JsonException error)
            {
                Log.Warn("rolebank", $"{path}: {error.Message}");
                _data = new RoleBankData();
            }
            return _data;
        }
    }
}
