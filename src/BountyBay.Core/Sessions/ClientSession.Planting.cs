using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Planting: with a hoe in hand, use a seed on a farm (around the Farmer); the plant grows for a few minutes; click it when it
// is ripe (or use the Harvesting stunt, US_PLANTING3) to harvest the crop, sometimes with a Normal / High-level / Quality
// Crop. The crop grade needs Planting level (grade - 1) * 10 + 1 (quest text: "planting skill must be level 21 ... plant
// level 3 crops") and Botany Study level grade - 1 ("upgrade the Special Skill before planting Crops of higher Levels").
// The plant is a land goods object (kind 8) with one of the crop models 4000548-4000553. GUESS: see planting.json.
public sealed class CropPlot
{
    public int Id { get; init; }
    public int Scene { get; init; }
    public string Owner { get; init; } = "";
    public SeedDefinition Seed { get; init; } = new();
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public long RipeAt { get; init; }

    public bool Ripe => Environment.TickCount64 >= RipeAt;
}

public sealed partial class ClientSession
{
    private const int FirstCropId = 0x6f000000;
    private const int MaxPlotsPerPlayer = 6;
    private const float HarvestStuntReach = 15f;

    private static readonly Dictionary<int, CropPlot> CropPlots = [];
    private static int _nextCropId = FirstCropId;

    private PlantingBook Planting => PlantingBook.For(Context.Settings);

    private bool PlantSeed(int index, ItemStack item)
    {
        if (Planting.Seed(item.Name) is not { } seed) return false;
        if (AtSea || Mover == null)
        {
            SystemMessage("Seeds are planted on a farm on land.", SystemMessageKind.Prompt);
            return true;
        }
        (float x, float z) = Mover.Now();
        if (Planting.FarmAt(Scene, x, z) == null)
        {
            SystemMessage("You can only plant on a farm. Find the Farmer in the outskirts of the city.", SystemMessageKind.Prompt);
            return true;
        }
        int level = SkillLevel("SKILL_PLANTING");
        if (level <= 0)
        {
            SystemMessage("You have not learned Planting yet.", SystemMessageKind.Prompt);
            return true;
        }
        if (level < (seed.Grade - 1) * 10 + 1 || UniqueSkillLevel("US_PLANTING2") < seed.Grade - 1)
        {
            SystemMessage($"Grade {seed.Grade} crops need Planting level {(seed.Grade - 1) * 10 + 1} and Botany Study level {seed.Grade - 1}.", SystemMessageKind.Prompt);
            return true;
        }
        lock (CropPlots)
        {
            if (CropPlots.Values.Count(plot => plot.Owner == Name) >= MaxPlotsPerPlayer)
            {
                SystemMessage($"You already tend {MaxPlotsPerPlayer} plants. Harvest some first.", SystemMessageKind.Prompt);
                return true;
            }
        }
        float px = x, pz = z, py = Height;
        if (!BeginBboGathering("SKILL_PLANTING", 3, () => FinishPlanting(seed, px, py, pz))) return true;
        TakeFromBag(index, 1, "(seed planted)");
        return true;
    }

    private void FinishPlanting(SeedDefinition seed, float x, float y, float z)
    {
        var plot = new CropPlot
        {
            Id = Interlocked.Increment(ref _nextCropId), Scene = Scene, Owner = Name, Seed = seed,
            X = x + 1.2f, Y = y, Z = z, RipeAt = Environment.TickCount64 + seed.GrowSeconds * 1000L,
        };
        lock (CropPlots) CropPlots[plot.Id] = plot;
        foreach (ClientSession other in Context.World.Sessions.Where(other => other.InWorld && other.Scene == plot.Scene && !other.AtSea).ToList())
            other.ShowCropPlot(plot);
        GainSkillExperience("SKILL_PLANTING", Planting.ExperiencePerGrade * seed.Grade / 4);
        SystemMessage($"You planted {seed.Name}. It will be ripe in about {Math.Max(1, seed.GrowSeconds / 60)} minute(s).", SystemMessageKind.Information);
        string owner = Name;
        Sea.Later(seed.GrowSeconds, () =>
        {
            if (Context.World.Sessions.FirstOrDefault(session => session.InWorld && session.Name == owner) is { } farmer)
                farmer.SystemMessage($"Your {seed.Name} is ripe. Harvest it before someone tramples it!", SystemMessageKind.Prompt);
        });
        Log.Info(Tag, $"{Name} plants {seed.Seed} in scene {Scene}");
    }

    public void SendCropPlots()
    {
        if (AtSea) return;
        List<CropPlot> plots;
        lock (CropPlots) plots = CropPlots.Values.Where(plot => plot.Scene == Scene).ToList();
        foreach (CropPlot plot in plots) ShowCropPlot(plot);
    }

    private void ShowCropPlot(CropPlot plot)
    {
        Send(ScenePackets.CreateObject(plot.Id, ObjectKind.Goods, plot.Seed.Model), "(crop)");
        Send(ScenePackets.ObjectProperties(plot.Id, new PropertyBag { ["X"] = plot.X, ["Y"] = plot.Y, ["Z"] = plot.Z, ["Orient"] = 0f }), "(crop)");
        Send(ScenePackets.ShowObject(plot.Id), "(crop)");
    }

    public bool OnCropClicked(int objectId)
    {
        CropPlot? plot;
        lock (CropPlots) plot = CropPlots.GetValueOrDefault(objectId);
        if (plot == null || plot.Scene != Scene || Mover == null) return false;
        if (plot.Owner != Name)
        {
            SystemMessage($"This {plot.Seed.Name} belongs to {plot.Owner}.", SystemMessageKind.Prompt);
            return true;
        }
        if (!plot.Ripe)
        {
            long left = Math.Max(1, (plot.RipeAt - Environment.TickCount64) / 1000);
            SystemMessage($"Your {plot.Seed.Name} is still growing: ripe in {left / 60}:{left % 60:D2}.", SystemMessageKind.Information);
            return true;
        }
        if (EquippedBboTool("SKILL_PLANTING") == null)
        {
            SystemMessage("You need a hoe in hand to harvest.", SystemMessageKind.Prompt);
            return true;
        }
        (float x, float z) = Mover.Now();
        float dx = plot.X - x, dz = plot.Z - z, distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance <= Planting.HarvestReach)
        {
            BeginBboGathering("SKILL_PLANTING", Planting.HarvestSeconds, () => HarvestPlot(plot));
            return true;
        }
        float stop = (distance - Planting.HarvestReach * 0.6f) / distance;
        MoveTo(x + dx * stop, z + dz * stop);
        Sea.Later(TimeToDestination().TotalSeconds + 0.2, () =>
        {
            if (Mover == null || Scene != plot.Scene) return;
            BeginBboGathering("SKILL_PLANTING", Planting.HarvestSeconds, () => HarvestPlot(plot));
        });
        return true;
    }

    private void HarvestPlot(CropPlot plot)
    {
        lock (CropPlots)
        {
            if (!CropPlots.Remove(plot.Id)) return;
        }
        foreach (ClientSession other in Context.World.Sessions.Where(other => other.InWorld && other.Scene == plot.Scene && !other.AtSea).ToList())
            other.Send(ScenePackets.RemoveObject(plot.Id), "(crop harvested)");
        SeedDefinition seed = plot.Seed;
        int amount = seed.Yield.Length >= 2 ? Random.Shared.Next(seed.Yield[0], seed.Yield[1] + 1) : 1;
        amount += UniqueSkillLevel("US_PLANTING2") / 3;
        if (GiveItem(new ItemStack { Name = seed.Crop, Photo = Context.Data.PhotoOf(seed.Crop), Amount = amount }, "(harvest)"))
            ItemObtained(seed.Crop, amount, "harvested");
        else SystemMessage("Your bag is full: the harvest is lost.", SystemMessageKind.Prompt);
        foreach (ByProduct extra in Planting.ByProducts.Where(extra => seed.Grade >= extra.Grade))
        {
            int chance = extra.Grade <= 1 ? 50 : extra.Grade <= 4 ? 25 : 15;
            if (Random.Shared.Next(100) >= chance) continue;
            if (GiveItem(new ItemStack { Name = extra.Item, Photo = Context.Data.PhotoOf(extra.Item), Amount = 1 }, "(harvest)"))
                ItemObtained(extra.Item, 1, "harvested");
        }
        GainSkillExperience("SKILL_PLANTING", Planting.ExperiencePerGrade * seed.Grade);
        Save();
        Log.Info(Tag, $"{Name} harvests {amount} x {seed.Crop}");
    }

    // Harvesting (US_PLANTING3): every level harvests one more of your ripe plants within reach.
    private bool HarvestNearby(int level)
    {
        if (Mover == null || AtSea) return false;
        (float x, float z) = Mover.Now();
        List<CropPlot> ripe;
        lock (CropPlots)
            ripe = CropPlots.Values.Where(plot => plot.Owner == Name && plot.Scene == Scene && plot.Ripe
                && (plot.X - x) * (plot.X - x) + (plot.Z - z) * (plot.Z - z) <= HarvestStuntReach * HarvestStuntReach).Take(Math.Max(1, level)).ToList();
        if (ripe.Count == 0)
        {
            SystemMessage("None of your plants nearby is ripe.", SystemMessageKind.Prompt);
            return false;
        }
        foreach (CropPlot plot in ripe) HarvestPlot(plot);
        return true;
    }
}
