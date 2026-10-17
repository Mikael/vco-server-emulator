using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private static readonly object MarkerGate = new();
    private static Dictionary<(int Scene, string Key), (float X, float Z)>? _markers;

    public void AskGoToObj(string[] words)
    {
        if (words.Length < 3 || !TryParseSceneCode(words[1], out int scene))
        {
            SystemMessage("That place cannot be found.", SystemMessageKind.Prompt);
            return;
        }
        string key = words[2];
        (float X, float Z)? target = FindGoToTarget(scene, key);
        string label = Context.Data.Npcs.Values.FirstOrDefault(npc => npc.NameKey == key)?.Name ?? key;
        if (target == null)
        {
            SystemMessage($"{label} cannot be found on the map.", SystemMessageKind.Prompt);
            return;
        }
        if (scene != Scene)
        {
            SystemMessage($"{label} is in another area. Travel there first, then click the link again.", SystemMessageKind.Prompt);
            return;
        }
        Send(BboSeaPackets.GoTo(ProtocolConstants.SelfObjectId, target.Value.X, target.Value.Z), "(go to object)");
        Log.Info(Tag, $"{Name} auto-paths to {key} in scene {scene}");
    }

    private static bool TryParseSceneCode(string code, out int scene)
    {
        scene = 0;
        if (code.StartsWith("CJ", StringComparison.OrdinalIgnoreCase) && int.TryParse(code[2..], out int number))
        {
            scene = number < 1000000 ? 1000000 + number : number;
            return true;
        }
        return int.TryParse(code, out scene);
    }

    private (float X, float Z)? FindGoToTarget(int scene, string key)
    {
        NpcDefinition? npc = Context.Data.Npcs.Values.FirstOrDefault(candidate => candidate.Scene == scene && candidate.NameKey == key);
        if (npc != null) return (npc.X, npc.Z);
        MonsterDefinition? monster = Context.Data.Monsters.Values.FirstOrDefault(candidate => candidate.Scene == scene && candidate.Key == key);
        if (monster != null) return (monster.X, monster.Z);
        return Markers(Context.Settings).TryGetValue((scene, key), out (float X, float Z) marker) ? marker : null;
    }

    private static Dictionary<(int Scene, string Key), (float X, float Z)> Markers(EmulatorSettings settings)
    {
        lock (MarkerGate)
        {
            if (_markers != null) return _markers;
            _markers = [];
            string path = settings.Resolve("data\\game\\bbo\\world_markers.json");
            if (!File.Exists(path)) return _markers;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("Markers", out JsonElement scenes)) return _markers;
            foreach (JsonProperty scene in scenes.EnumerateObject())
            {
                if (!int.TryParse(scene.Name, out int id)) continue;
                foreach (JsonElement marker in scene.Value.EnumerateArray())
                {
                    if (!marker.TryGetProperty("key", out JsonElement key) || !marker.TryGetProperty("x", out JsonElement x)
                        || !marker.TryGetProperty("z", out JsonElement z) || x.ValueKind != JsonValueKind.Number) continue;
                    _markers.TryAdd((id, key.GetString() ?? ""), (x.GetSingle(), z.GetSingle()));
                }
            }
            return _markers;
        }
    }
}
