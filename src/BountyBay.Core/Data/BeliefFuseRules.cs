using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class BeliefFuseEntry
{
    public int Index { get; set; }
    public string Item { get; set; } = "";
    public int Type { get; set; }
    public Dictionary<string, int> Props { get; set; } = [];

    public string FuseInfo => string.Join(";", Props.Where(pair => pair.Value != 0).Select(pair => $"{pair.Key} {pair.Value}"));
}

public sealed class BeliefFuseRules
{
    public const int BeliefArmType = 11;

    private static readonly object Gate = new();
    private static BeliefFuseRules? _current;

    public bool ConsumeTarget { get; set; } = true;
    public Dictionary<int, int> Fees { get; set; } = [];
    public List<BeliefFuseEntry> Beliefs { get; set; } = [];

    public static BeliefFuseRules For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\fuse.json"));
    }

    public BeliefFuseEntry? Find(string item)
        => Beliefs.FirstOrDefault(entry => entry.Item.Equals(item, StringComparison.OrdinalIgnoreCase));

    public int Fee(int type) => Fees.TryGetValue(type, out int fee) ? fee : 0;

    private static BeliefFuseRules Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("BeliefFuse", $"game data file is missing: {path}");
            return new BeliefFuseRules();
        }
        try
        {
            BeliefFuseRules rules = JsonSerializer.Deserialize<BeliefFuseRules>(File.ReadAllText(path), EmulatorSettings.Json) ?? new BeliefFuseRules();
            rules.Beliefs = rules.Beliefs.Where(entry => entry.Item.Length > 0).ToList();
            Log.Info("BeliefFuse", $"{rules.Beliefs.Count} fusable faith(s) loaded");
            return rules;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("BeliefFuse", $"Could not read {path}: {exception.Message}");
            return new BeliefFuseRules();
        }
    }
}
