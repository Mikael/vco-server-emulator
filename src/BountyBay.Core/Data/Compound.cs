using System.Text.Json;
using BountyBay.Core.Configuration;
using BountyBay.Core.Logging;

namespace BountyBay.Core.Data;

public sealed class CompoundIngredient
{
    public string Name { get; set; } = "";
    public int Amount { get; set; } = 1;
}

public sealed class CompoundRecipe
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Result { get; set; } = "";
    public int Amount { get; set; } = 1;
    public int Fee { get; set; }
    public List<CompoundIngredient> Ingredients { get; set; } = [];
}

public sealed class CompoundBook
{
    private static readonly object Gate = new();
    private static CompoundBook? _current;

    private CompoundBook(List<CompoundRecipe> recipes) => All = recipes;

    public IReadOnlyList<CompoundRecipe> All { get; }

    public CompoundRecipe? Find(string key)
        => All.FirstOrDefault(recipe => recipe.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static CompoundBook For(EmulatorSettings settings)
    {
        lock (Gate) return _current ??= Read(settings.Resolve("data\\game\\bbo\\compound.json"));
    }

    private static CompoundBook Read(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("Compound", $"game data file is missing: {path}");
            return new CompoundBook([]);
        }
        try
        {
            List<CompoundRecipe> recipes = JsonSerializer.Deserialize<List<CompoundRecipe>>(File.ReadAllText(path), EmulatorSettings.Json) ?? [];
            recipes = recipes
                .Where(recipe => recipe.Result.Length > 0 && recipe.Amount > 0 && recipe.Ingredients.Count > 0
                    && recipe.Ingredients.All(ingredient => ingredient.Name.Length > 0 && ingredient.Amount > 0))
                .ToList();
            Log.Info("Compound", $"{recipes.Count} combination(s) loaded");
            return new CompoundBook(recipes);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Log.Error("Compound", $"Could not read {path}: {exception.Message}");
            return new CompoundBook([]);
        }
    }
}
