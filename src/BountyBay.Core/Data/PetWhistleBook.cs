using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class PetWhistleDefinition
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Model { get; set; }
    public int Image { get; set; }
}

public sealed class PetAutoSkillDefinition
{
    public string Key { get; set; } = "";
    public int Kind { get; set; }
    public string Skill { get; set; } = "";
    public int Level { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<string> Text { get; set; } = [];
    public string Image { get; set; } = "";
    public int Damage { get; set; }
    public int CooldownSeconds { get; set; }
}

public sealed class PetWhistleFile
{
    public List<PetWhistleDefinition> Whistles { get; set; } = [];
}

public sealed class PetAutoSkillFile
{
    public List<PetAutoSkillDefinition> Skills { get; set; } = [];
}

public sealed class PetWhistleBook
{
    private static readonly object Gate = new();
    private static PetWhistleBook? _current;

    private PetWhistleBook(List<PetWhistleDefinition> whistles, List<PetAutoSkillDefinition> skills)
    {
        Whistles = whistles;
        Skills = skills;
    }

    public IReadOnlyList<PetWhistleDefinition> Whistles { get; }

    public IReadOnlyList<PetAutoSkillDefinition> Skills { get; }

    public PetWhistleDefinition? Whistle(string key)
        => Whistles.FirstOrDefault(whistle => whistle.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public PetAutoSkillDefinition? Skill(string key)
        => Skills.FirstOrDefault(skill => skill.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static PetWhistleBook For(EmulatorSettings settings)
    {
        lock (Gate)
        {
            return _current ??= new PetWhistleBook(
                Read<PetWhistleFile>(settings.Resolve("data\\game\\PetWhistles.json"))?.Whistles ?? [],
                Read<PetAutoSkillFile>(settings.Resolve("data\\game\\PetAutoSkills.json"))?.Skills ?? []);
        }
    }

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            Log.Warn("Pets", $"game data file is missing: {path}");
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), EmulatorSettings.Json);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Pets", $"Could not read {path}: {exception.Message}");
            return null;
        }
    }
}
