using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class BoothSearchClass
{
    public int Class { get; set; }
    public string Label { get; set; } = "";
    public List<int> ItemTypes { get; set; } = [];
    public List<string> GoodsTypes { get; set; } = [];
    public bool Rest { get; set; }
    public bool All { get; set; }
}

public sealed class BoothSearchData
{
    public int PageSize { get; set; } = 7;
    public bool WholeServer { get; set; }
    public List<BoothSearchClass> Classes { get; set; } = [];
}

public static class BoothSearchCatalog
{
    private static readonly object Gate = new();
    private static BoothSearchData? _data;

    public static BoothSearchData Get(EmulatorSettings settings)
    {
        lock (Gate)
        {
            if (_data != null) return _data;
            string path = settings.Resolve("data\\game\\BoothSearchClasses.json");
            try
            {
                _data = File.Exists(path)
                    ? JsonSerializer.Deserialize<BoothSearchData>(File.ReadAllText(path), EmulatorSettings.Json) ?? new BoothSearchData()
                    : new BoothSearchData();
            }
            catch (JsonException error)
            {
                Log.Warn("boothsearch", $"{path}: {error.Message}");
                _data = new BoothSearchData();
            }
            return _data;
        }
    }
}
