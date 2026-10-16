using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class RecipeIngredient
{
    public string Name { get; set; } = "";
    public int Amount { get; set; } = 1;
    public int Level { get; set; } = 1;
}

public sealed class RecipeDefinition
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Photo { get; set; }
    public int Amount { get; set; } = 1;
    public int Seconds { get; set; } = 3;
    public int SkillLevel { get; set; } = 1;
    public List<RecipeIngredient> Ingredients { get; set; } = [];
}

public sealed class RecipeBook
{
    private static readonly object Gate = new();
    private static RecipeBook? _current;

    private RecipeBook(List<RecipeDefinition> recipes)
    {
        All = recipes;
        ByName = recipes.ToDictionary(recipe => recipe.Name);
    }

    public IReadOnlyList<RecipeDefinition> All { get; }

    public IReadOnlyDictionary<string, RecipeDefinition> ByName { get; }

    public static RecipeBook For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\bbo\\recipes.json"));
    }

    private static RecipeBook Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("Recipes", $"game data file is missing: {path}");
            return new RecipeBook([]);
        }
        try
        {
            List<RecipeDefinition> recipes = JsonSerializer.Deserialize<List<RecipeDefinition>>(File.ReadAllText(path), EmulatorSettings.Json) ?? [];
            recipes = recipes.Where(recipe => recipe.Name.Length > 0).DistinctBy(recipe => recipe.Name).ToList();
            Log.Info("Recipes", $"{recipes.Count} recipe(s) loaded");
            return new RecipeBook(recipes);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Recipes", $"Could not read {path}: {exception.Message}");
            return new RecipeBook([]);
        }
    }
}
