using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private static readonly string[] WorkNames = ["", "Royal Millitary Officer", "Imperial Guardian", "Carribean Pirate", "Treasure Hunter", "Armed Businessman"];

    private const string NoWorkName = "Adventurer";

    private static readonly string[] PublicWorkProperties = ["WorkType", "WorkLevel", "WorkName"];

    private static readonly string[] WorkPools = ["SeaPoints", "LandPoints", "MakePoints"];

    private static readonly string[] HonorProperties = ["HonorLevel", "HonorExp"];

    private const int LookModes = 3;
    private const int SkillLevelsPerWorkPoint = 10;
    private const int HighestDamageReduction = 90;

    public static void AddBboItemId(PropertyBag bag, ItemStack item)
    {
        if (item.Name.Length > 0 && PropertyTable.Has("ItemID")) bag["ItemID"] = item.Name;
    }

    public void ApplyBboCharacterProperties()
    {
        PutBboProperty("ShowNouse", Character.ShowNouse);
        ApplyFlagHonorProperty();
        SyncWorkLevel();
        foreach ((string name, object value) in WorkProperties()) PutBboProperty(name, value);
        foreach (string name in HonorProperties.Where(name => !Properties.ContainsKey(name))) PutBboProperty(name, 0);
    }

    public void EnterWorldBboCharacter() => SendBboTitles();

    public void SendBboTitles()
    {
        Send(CharacterPackets.ClearCustomTable(CharacterPackets.TitleTable), "(clear title list)");
        if (Character.Titles.Count > 0)
            Send(CharacterPackets.CustomTableRows(CharacterPackets.TitleTable, Character.Titles), "(title list)");
    }

    public void ChangeLookMode(int shown)
    {
        int next = ((shown % LookModes + LookModes) % LookModes + 1) % LookModes;
        Character.ShowNouse = next;
        if (PropertyTable.Has("ShowNouse")) SetProperty("ShowNouse", next);
        Save();
        Log.Info(Tag, $"{Name} switches the look mode from {shown} to {next}");
    }

    public void SendOutfit(int suit)
    {
        if (!ValidOutfit(suit) || !Character.Outfits.TryGetValue(suit, out Dictionary<int, string>? pieces)) return;
        foreach ((int position, string itemId) in pieces.OrderBy(pair => pair.Key))
            Send(CharacterPackets.AddOutfitPiece(suit, position, itemId), "(outfit piece)");
    }

    public void SetOutfitPiece(int suit, int position, string itemId)
    {
        if (!ValidOutfit(suit) || position < 0 || position >= CharacterPackets.OutfitPositions) return;
        if (!OwnsGear(itemId))
        {
            SystemMessage("Only equipment you own can be put into an outfit.", SystemMessageKind.Prompt);
            return;
        }
        if (!Character.Outfits.TryGetValue(suit, out Dictionary<int, string>? pieces))
        {
            pieces = [];
            Character.Outfits[suit] = pieces;
        }
        pieces[position] = itemId;
        Save();
        Send(CharacterPackets.AddOutfitPiece(suit, position, itemId), "(outfit piece)");
        Log.Info(Tag, $"{Name} puts {itemId} into outfit {suit + 1} at {position}");
    }

    public void ClearOutfitPiece(int suit, int position)
    {
        if (!ValidOutfit(suit) || !Character.Outfits.TryGetValue(suit, out Dictionary<int, string>? pieces) || !pieces.Remove(position)) return;
        if (pieces.Count == 0) Character.Outfits.Remove(suit);
        Save();
        Send(CharacterPackets.DeleteOutfitPiece(suit, position), "(outfit piece removed)");
    }

    public void WearOutfit(int suit)
    {
        if (!ValidOutfit(suit) || !Character.Outfits.TryGetValue(suit, out Dictionary<int, string>? pieces) || pieces.Count == 0)
        {
            SystemMessage("That outfit is empty. Drag equipment onto it first.", SystemMessageKind.Prompt);
            return;
        }
        int changed = 0;
        int missing = 0;
        foreach (string itemId in pieces.OrderBy(pair => pair.Key).Select(pair => pair.Value).Distinct().ToList())
        {
            if (Bags[ViewId.Equipped].Values.Any(item => item.Name == itemId)) continue;
            int index = Bags[ViewId.BagEquip].FirstOrDefault(pair => pair.Value.Name == itemId).Key;
            if (index == 0)
            {
                missing++;
                continue;
            }
            MoveGear(ViewId.BagEquip, index);
            changed++;
        }
        if (missing > 0) SystemMessage($"{missing} piece(s) of outfit {suit + 1} are not in your equipment bag.", SystemMessageKind.Prompt);
        else if (changed == 0) SystemMessage($"You are already wearing outfit {suit + 1}.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} changes into outfit {suit + 1} ({changed} worn, {missing} missing)");
    }

    public void SelectWork(int work)
    {
        if (work < 1 || work >= WorkNames.Length) return;
        if (Character.WorkType == work)
        {
            SystemMessage($"You already follow the {WorkNames[work]} career.", SystemMessageKind.Prompt);
            return;
        }
        Character.WorkType = work;
        Character.WorkLevel = Math.Max(1, Character.WorkLevel);
        SyncWorkLevel();
        Character.WorkPointsSpent.Clear();
        Character.UniqueSkills.RemoveAll(entry => entry.Key.StartsWith("WORK_", StringComparison.Ordinal));
        Save();
        SendWorkProperties();
        SendSkillState();
        SystemMessage($"You are now a {WorkNames[work]}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} chooses the career {WorkNames[work]}");
    }

    public void LearnWorkSkill(string key, int requested)
    {
        if (Character.WorkType == 0)
        {
            SystemMessage("Choose a career first.", SystemMessageKind.Prompt);
            return;
        }
        if (!key.StartsWith("WORK_", StringComparison.Ordinal)) return;
        UniqueSkillEntry? entry = Character.UniqueSkills.FirstOrDefault(candidate => candidate.Key == key);
        int next = (entry?.Level ?? 0) + 1;
        if (next > Rule.Skills.StuntLevelMax)
        {
            SystemMessage("That career skill is already at its highest level.", SystemMessageKind.Prompt);
            return;
        }
        string pool = WorkPoolFor(key);
        if (WorkPointsLeft(pool) < 1)
        {
            SystemMessage("You do not have enough points for that career skill.", SystemMessageKind.Prompt);
            return;
        }
        Character.WorkPointsSpent[pool] = Character.WorkPointsSpent.GetValueOrDefault(pool) + 1;
        if (entry == null) Character.UniqueSkills.Add(new UniqueSkillEntry { Key = key, Level = 1 });
        else entry.Level = next;
        Save();
        SendWorkProperties();
        SendSkillState();
        RefreshStats();
        Log.Info(Tag, $"{Name} raises {key} to {next} with {pool} (asked {requested})");
    }

    private void ComputeBboStats()
    {
        int passive = Rule.Combat.PassiveChancePercentPerLevel;
        string fight = FightingSkill();
        var values = new Dictionary<string, int>
        {
            ["SkillHap"] = WornSum("SkillHap"),
            ["DoubleDamage"] = WornSum("DoubleDamage") + (fight == "SKILL_SWORD" ? passive * UniqueSkillLevel("US_SWORD2") : 0),
            ["DoubleDamage0_"] = WornSum("DoubleDamage0"),
            ["AddDamageScope"] = WornSum("AddDamageScope"),
            ["AddDamageSco0_"] = WornSum("AddDamageScope0"),
            ["Ward"] = WornSum("Ward") + WornSum("AddWardRatio") + (fight == "SKILL_AXE" ? passive * UniqueSkillLevel("US_AXE2") : 0),
            ["Acute"] = WornSum("Acute"),
            ["Physique"] = WornSum("Physique"),
            ["Response"] = WornSum("Response"),
            ["Backbone"] = WornSum("Backbone"),
        };
        foreach ((string name, int value) in values)
        {
            if (!PropertyTable.Has(name) || Properties.Int(name, -1) == Math.Max(0, value)) continue;
            if (InWorld) SetProperty(name, Math.Max(0, value));
            else Properties[name] = Math.Max(0, value);
        }
        if ((SyncWorkLevel() || Character.WorkType == 0) && InWorld) SendWorkProperties();
        FoldOnlineRewardTime();
    }

    private int BboOutgoingDamage(int dealt, ref uint colour)
    {
        int bonus = Math.Max(0, WornSum("AddDamageScope"));
        if (bonus > 0) dealt = dealt * (100 + bonus) / 100;
        if (CombatRules.Chance(WornSum("DoubleDamage")))
        {
            dealt = dealt * (Rule.Combat.CriticalDamagePercent + Math.Max(0, WornSum("Response"))) / 100;
            colour = 0xff8000;
        }
        return Math.Max(1, dealt);
    }

    private int BboIncomingDamage(int taken)
    {
        int reduction = Math.Clamp(WornSum("AddDamageScope0"), 0, HighestDamageReduction);
        if (reduction > 0) taken = taken * (100 - reduction) / 100;
        if (CombatRules.Chance(WornSum("Ward") + WornSum("AddWardRatio"))) taken = taken * Rule.Combat.BlockDamagePercent / 100;
        return Math.Max(1, taken);
    }

    private IEnumerable<(string Name, object Value)> WorkProperties()
    {
        int work = Character.WorkType > 0 && Character.WorkType < WorkNames.Length ? Character.WorkType : 0;
        yield return ("WorkType", work);
        // The overhead label (Alt) shows "WorkName WorkLevel" and nothing at all without a profession; the client has no name for
        // profession 0, so characters without one show "Adventurer" and their character level.
        yield return ("WorkLevel", work == 0 ? Math.Max(1, TotalSkillLevel) : Math.Max(1, Character.WorkLevel));
        // The bar above the shortcut keys draws MultWorkExp * 2147483647 + WorkExp against the same pair of maxima, and only when
        // all four are known. The level beside the portrait is the total skill level, so the bar shows the experience gathered
        // in the skills against what they need for their next level.
        (long experience, long needed) = LevelProgress();
        yield return ("MultWorkExp", (int)(experience / int.MaxValue));
        yield return ("WorkExp", (int)(experience % int.MaxValue));
        yield return ("MultWorkExpMax", (int)(needed / int.MaxValue));
        yield return ("WorkExpMax", (int)(needed % int.MaxValue));
        yield return ("WorkName", work == 0 ? NoWorkName : WorkNames[work]);
        foreach (string pool in WorkPools) yield return (pool, WorkPointsLeft(pool));
    }

    private (long Experience, long Needed) LevelProgress()
    {
        long experience = 0, needed = 0;
        foreach (string key in SkillKeys())
        {
            int level = SkillLevel(key);
            if (level <= 0 || level >= SkillCap(key)) continue;
            experience += Character.SkillExperience.GetValueOrDefault(key);
            needed += Rule.Skills.ExperienceToNext(level);
        }
        return (experience, Math.Max(1, needed));
    }

    public void SendLevelProgress() => SendWorkProperties();

    private void SendWorkProperties()
    {
        foreach ((string name, object value) in WorkProperties())
        {
            if (PropertyTable.Has(name) && !Equals(Properties.GetValueOrDefault(name), value)) SetProperty(name, value, PublicWorkProperties.Contains(name));
        }
    }

    private bool SyncWorkLevel()
    {
        int level = Character.WorkType == 0 ? Character.WorkLevel : Math.Max(Math.Max(1, Character.WorkLevel), TotalSkillLevel);
        if (level == Character.WorkLevel) return false;
        Character.WorkLevel = level;
        return true;
    }

    private int WorkPointsLeft(string pool)
        => Character.WorkType == 0 ? 0 : Math.Max(0, TotalSkillLevel / SkillLevelsPerWorkPoint - Character.WorkPointsSpent.GetValueOrDefault(pool));

    private string WorkPoolFor(string key)
    {
        if (key.StartsWith("WORK_MAKE_", StringComparison.Ordinal)) return "MakePoints";
        return WorkPointsLeft("LandPoints") >= WorkPointsLeft("SeaPoints") ? "LandPoints" : "SeaPoints";
    }

    private void PutBboProperty(string name, object value)
    {
        if (PropertyTable.Has(name)) Properties[name] = value;
    }

    private static bool ValidOutfit(int suit) => suit >= 0 && suit < CharacterPackets.OutfitCount;

    private bool OwnsGear(string itemId)
        => (Hand != null && Hand.Name == itemId)
            || Bags[ViewId.BagEquip].Values.Concat(Bags[ViewId.Equipped].Values).Any(item => item.Name == itemId);
}
