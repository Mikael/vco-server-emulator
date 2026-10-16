using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class CraftingTables
{
    public static readonly TableDefinition MakingItem = new(15, "MakingItem",
    [
        new("Name", PropertyType.String),
        new("Kind", PropertyType.String),
    ]);

    public static readonly TableDefinition[] All = [MakingItem];
}

public sealed partial class ClientSession
{
    public const int RecipeView = 27;

    private bool _recipeViewOpen;
    private bool _recipeShown;
    private RecipeDefinition? _craftJob;

    private RecipeBook Recipes => RecipeBook.For(Context.Settings);

    public void SendRecipes()
    {
        List<object[]> rows = Recipes.All.Select(recipe => new object[] { recipe.Name, recipe.Category }).ToList();
        if (rows.Count > 0) Send(TablePackets.Rows(CraftingTables.MakingItem, rows), "(recipes)");
    }

    public void QueryRecipe(string name)
    {
        if (!_recipeViewOpen)
        {
            _recipeViewOpen = true;
            Send(ViewPackets.Create(RecipeView, 1), "(recipe view)");
        }
        if (_recipeShown)
        {
            _recipeShown = false;
            Send(ViewPackets.RemoveItem(RecipeView, 1), "(recipe cleared)");
        }
        if (name.Length == 0) return;
        if (!Recipes.ByName.TryGetValue(name, out RecipeDefinition? recipe))
        {
            SystemMessage("Nobody here knows how to make that.");
            return;
        }
        _recipeShown = true;
        Send(ViewPackets.AddItem(RecipeView, 1, ItemProperties(CraftProduct(recipe), 1)), $"(recipe {recipe.Name})");
        SendRecipeNeeds(recipe);
    }

    public void Craft(string name)
    {
        if (!Recipes.ByName.TryGetValue(name, out RecipeDefinition? recipe))
        {
            SystemMessage("Nobody here knows how to make that.");
            return;
        }
        if (_craftJob != null)
        {
            SystemMessage("You are already making something.");
            return;
        }
        if (!CraftPossible(recipe)) return;
        _craftJob = recipe;
        int milliseconds = Math.Max(1, recipe.Seconds) * 1000;
        Send(ChatPackets.Script(0, $"MakingItemTimer {milliseconds}"), "(crafting timer)");
        Log.Info(Tag, $"{Name} starts making {recipe.Name}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(milliseconds);
            lock (Context.World.Sync) FinishCraft(recipe);
        });
    }

    private void FinishCraft(RecipeDefinition recipe)
    {
        if (_craftJob != recipe) return;
        _craftJob = null;
        if (_closed || !InWorld) return;
        Send(ChatPackets.Script(0, "MakingItemTimer 0"), "(crafting timer done)");
        if (!CraftPossible(recipe))
        {
            SystemMessage("The making failed and the materials were lost.");
            return;
        }
        foreach (RecipeIngredient ingredient in recipe.Ingredients)
        {
            int needed = ingredient.Amount;
            foreach (int slot in Items.Where(pair => pair.Value.Name == ingredient.Name).Select(pair => pair.Key).Order().ToList())
            {
                if (needed <= 0) break;
                int used = Math.Min(needed, Items[slot].Amount);
                TakeFromBag(slot, used, "(crafting material)");
                needed -= used;
            }
        }
        CraftStore(CraftProduct(recipe));
        SystemMessage("The item was made successfully.");
        GainSkillExperience(recipe.Category, Rule.Skills.WorkExperienceAt(Math.Max(1, recipe.SkillLevel)));
        if (_recipeShown) SendRecipeNeeds(recipe);
        Save();
        Log.Info(Tag, $"{Name} makes {recipe.Amount} x {recipe.Name}");
    }

    private bool CraftPossible(RecipeDefinition recipe)
    {
        if (recipe.Category.Length > 0 && SkillLevel(recipe.Category) < recipe.SkillLevel)
        {
            SystemMessage($"Composition requires Skill Level {recipe.SkillLevel}. Have not learned the required Skill.");
            return false;
        }
        RecipeIngredient? missing = recipe.Ingredients.FirstOrDefault(ingredient => CraftCarried(ingredient.Name) < ingredient.Amount);
        if (missing != null)
        {
            SystemMessage("You do not have all the materials for that.");
            return false;
        }
        bool room = Items.Count < ViewId.BagSlots
            || Items.Values.Any(item => item.Name == recipe.Name)
            || recipe.Ingredients.Any(ingredient => CraftCarried(ingredient.Name) == ingredient.Amount);
        if (!room) SystemMessage("Your bag is full.");
        return room;
    }

    private int CraftCarried(string name) => Items.Values.Where(item => item.Name == name).Sum(item => item.Amount);

    private ItemStack CraftProduct(RecipeDefinition recipe) => new()
    {
        Name = recipe.Name,
        Photo = recipe.Photo != 0 ? recipe.Photo : Context.Data.PhotoOf(recipe.Name),
        Amount = Math.Max(1, recipe.Amount),
    };

    private void CraftStore(ItemStack product)
    {
        foreach ((int used, ItemStack stack) in Items)
        {
            if (stack.Name != product.Name) continue;
            stack.Amount += product.Amount;
            Send(ViewPackets.UpdateItem(ViewId.BagItems, used, new PropertyBag { ["Amount"] = stack.Amount }), "(crafted item)");
            return;
        }
        int slot = FreeSlot(Items, ViewId.BagSlots);
        if (slot == 0) return;
        Items[slot] = product;
        Send(ViewPackets.AddItem(ViewId.BagItems, slot, ItemProperties(product, slot)), "(crafted item)");
    }

    private void SendRecipeNeeds(RecipeDefinition recipe)
    {
        IEnumerable<string> needs = recipe.Ingredients.Select(ingredient =>
            $"{ingredient.Level} {ingredient.Amount} {ingredient.Name} {Math.Max(0, ingredient.Amount - CraftCarried(ingredient.Name))}");
        Send(ChatPackets.Script(0, ("MIRes " + string.Join(' ', needs)).TrimEnd()), "(recipe materials)");
    }
}
