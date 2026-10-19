using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Sessions;

// Fishing (Halieutics). The client has no fishing window or button: the net is equipment ("Necessity for fishing,
// unrepairable"), the stunts raise the energy (Fisherman Stamina), the ship load (Fishing Practice) and the net's life (Net
// Maintenance). So a ship with a net out catches fish into the hold while it is at sea, at the cost of energy (SP).
// CLIENT: the catches are the fish trade goods (Fish, Shrimp, Salmon, Lobster, Australian lobster: ItemType 14).
// GUESS: the interval, odds, catch by skill level and the energy cost.
public sealed partial class ClientSession
{
    private const int FishingSeconds = 20;
    private const int FishingSpCost = 3;
    private const string BetterNet = "IDName00003396";

    private static readonly (int Level, string Item)[] Catches =
    [
        (1, "IDName00003422"), (10, "IDName_Tw0159"), (20, "IDName_Tw0117"), (30, "IDName00003429"), (45, "IDName_Tw0192"),
    ];

    private long _nextCatchAt;
    private long _fishingNoticeAt;

    // Called by the sea tick for every sailor.
    public void FishTick(long now)
    {
        if (!AtSea || SeaSunk || _oceanBattle != null || Mover == null) return;
        if (_nextCatchAt == 0) _nextCatchAt = now + FishingSeconds * 1000L;
        if (now < _nextCatchAt) return;
        _nextCatchAt = now + FishingSeconds * 1000L;
        if (SkillLevel("SKILL_HALIEUTICS") <= 0 || EquippedBboTool("SKILL_HALIEUTICS") == null) return;
        if (Sp < FishingSpCost)
        {
            if (now - _fishingNoticeAt > 120_000)
            {
                _fishingNoticeAt = now;
                SystemMessage("Your crew is too tired to haul the fishing net. Rest to recover energy.", SystemMessageKind.Prompt);
            }
            return;
        }
        int level = SkillLevel("SKILL_HALIEUTICS");
        bool better = Character.Bags?.GetValueOrDefault(ViewId.Equipped)?.Values.Any(item => item.Name == BetterNet) == true;
        int chance = Math.Clamp(45 + level + (better ? 15 : 0), 10, 95);
        SetSp(Sp - FishingSpCost);
        if (Random.Shared.Next(100) >= chance)
        {
            GainSkillExperience("SKILL_HALIEUTICS", 10);
            return;
        }
        (int Level, string Item)[] open = Catches.Where(entry => level >= entry.Level).ToArray();
        string fish = open[Math.Max(0, open.Length - 1 - (Random.Shared.Next(100) < 60 ? 0 : Random.Shared.Next(open.Length)))].Item;
        int amount = 1 + Random.Shared.Next(better ? 3 : 2);
        if (!GiveItem(new ItemStack { Name = fish, Photo = Context.Data.PhotoOf(fish), Amount = amount }, "(fishing catch)"))
        {
            SystemMessage("Your hold is full: the catch slips back into the sea.", SystemMessageKind.Prompt);
            return;
        }
        ItemObtained(fish, amount, "hauled in");
        GainSkillExperience("SKILL_HALIEUTICS", 30 * (Array.FindIndex(Catches, entry => entry.Item == fish) + 1));
        Save();
        Log.Debug(Tag, $"{Name} catches {amount} x {fish}");
    }
}
