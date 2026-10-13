using System.Text.Json;
using VoyageCentury.Core.Configuration;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;

namespace VoyageCentury.Core.Data;

public sealed class CreationChoice
{
    public int Id { get; set; }
    public int Sex { get; set; }
}

public sealed class CreationData
{
    public List<CreationChoice> Roles { get; set; } = [];
    public List<CreationChoice> Faces { get; set; } = [];
    public List<CreationChoice> Bodies { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public List<int> Cities { get; set; } = [];
}

public sealed class GoodsDefinition
{
    public int Photo { get; set; }
    public int Price { get; set; }
    public int Heal { get; set; }
    public int Sp { get; set; }
    public bool Instant { get; set; }
}

public sealed class ItemData
{
    public Dictionary<string, GoodsDefinition> Goods { get; set; } = [];
    public List<ItemStack> StarterItems { get; set; } = [];
    public List<ItemStack> StarterEquipment { get; set; } = [];
}

public sealed class DialogueOption
{
    public DialogueAction Action { get; set; }
    public string Text { get; set; } = "";
}

public sealed class NpcDefinition
{
    public int Id { get; set; }
    public int Scene { get; set; }
    public int Model { get; set; }
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public string Greeting { get; set; } = "Good day.";
    public List<DialogueOption> Options { get; set; } = [];
    public Dictionary<int, List<string>>? Shop { get; set; }
    public int StuntCap { get; set; } = 10;
}

public sealed class MonsterDefinition
{
    public int Id { get; set; }
    public int Scene { get; set; }
    public int Model { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Hp { get; set; }
    public int Attack { get; set; } = 3;
    public int Defence { get; set; }
    public int Hit { get; set; }
    public int Dodge { get; set; }
    public bool Aggressive { get; set; }
    public bool Flees { get; set; }
    public int Gold { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public sealed class BotData
{
    public List<string> Names { get; set; } = ["Anna"];
    public List<string> Lines { get; set; } = ["Good day, captain."];
    public List<int> Emotes { get; set; } = [];
}

public sealed class GameData
{
    public CreationData Creation { get; private init; } = new();
    public ItemData Items { get; private init; } = new();
    public Dictionary<int, NpcDefinition> Npcs { get; private init; } = [];
    public Dictionary<int, MonsterDefinition> Monsters { get; private init; } = [];
    public BotData Bots { get; private init; } = new();
    public SceneFiles Scenes { get; private init; } = null!;
    public ActionTable Actions { get; private init; } = null!;
    public GameRules Rules { get; private init; } = new();

    public static GameData Load(EmulatorSettings settings)
    {
        string folder = settings.Resolve("data\\game");
        PropertyTable.Define(Read<List<PropertyDefinition>>(folder, "properties.json"));
        return new GameData
        {
            Creation = Read<CreationData>(folder, "creation.json"),
            Items = Read<ItemData>(folder, "items.json"),
            Npcs = Read<List<NpcDefinition>>(folder, "npcs.json").ToDictionary(npc => npc.Id),
            Monsters = Read<List<MonsterDefinition>>(folder, "monsters.json").ToDictionary(monster => monster.Id),
            Bots = Read<BotData>(folder, "bots.json"),
            Scenes = new SceneFiles(settings.ResourcePath),
            Actions = ActionTable.Load(settings.ResourcePath),
            Rules = GameRules.Load(settings),
        };
    }

    public int PhotoOf(string name) => Items.Goods.TryGetValue(name, out GoodsDefinition? goods) ? goods.Photo : 0;

    public int PriceOf(string name) => Items.Goods.TryGetValue(name, out GoodsDefinition? goods) ? goods.Price : 10;

    public int HealOf(string name) => Items.Goods.TryGetValue(name, out GoodsDefinition? goods) ? goods.Heal : 0;

    public int SpOf(string name) => Items.Goods.TryGetValue(name, out GoodsDefinition? goods) ? goods.Sp : 0;

    public bool InstantOf(string name) => Items.Goods.TryGetValue(name, out GoodsDefinition? goods) && goods.Instant;

    private static T Read<T>(string folder, string file) where T : new()
    {
        string path = Path.Combine(folder, file);
        if (!File.Exists(path)) throw new FileNotFoundException($"game data file is missing: {path}");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), EmulatorSettings.Json) ?? new T();
    }
}
