using System.Text.Json;
using BountyBay.Core.Configuration;

namespace BountyBay.Core.Data;

public sealed class MallGoods
{
    public string Item { get; set; } = "";
    public string Name { get; set; } = "";
    public int Price { get; set; } = 100;
    public int Category { get; set; }
}

public sealed class MallData
{
    public List<MallGoods> Goods { get; set; } = [];
}

public static class ItemMall
{
    private static readonly object Gate = new();
    private static MallData? _data;

    public static MallData Load(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_data != null) return _data;
            string path = settings.Resolve("data\\game\\bbo\\mall.json");
            try
            {
                _data = File.Exists(path)
                    ? JsonSerializer.Deserialize<MallData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new MallData()
                    : new MallData();
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                _data = new MallData();
            }
            _data.Goods.RemoveAll(goods => goods.Item.Length == 0 || goods.Price <= 0 || ItemCatalog.Find(goods.Item) == null);
            return _data;
        }
    }
}
