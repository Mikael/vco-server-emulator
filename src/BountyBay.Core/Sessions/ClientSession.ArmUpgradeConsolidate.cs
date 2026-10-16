using BountyBay.Core.Data;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int StarMaterialSlots = 4;
    private const int EvolMaterialSlots = 6;
    private const int StarBarRange = 200;

    public void ArmPlace(int view, int index)
    {
        if (ArmUpgradeItem(view, index) is not { } found) return;
        Send(ArmUpgradePackets.TakeOffThing(found.View, found.Index), "(item to the consolidation window)");
    }

    public void OpenEvolView(int view, int index)
    {
        if (ArmEvolTarget(view, index) is not { } found) return;
        ArmUpgradeState state = ArmState(found.Item);
        string property = ArmMainProperty(found.Item.Name);
        Send(ArmUpgradePackets.EvolInfo(property, EvolGain(found.Item.Name)), "(evolution attribute)");
        List<ArmUpgradeAmount> cost = EvolCost(found.Item.Name, state.Evol);
        Send(ArmUpgradePackets.EvolStuffList(
            cost.Where(entry => !ArmBook.IsSilver(entry.Item)).Take(EvolMaterialSlots).Select(entry => (ArmHave(entry.Item), (long)entry.Amount)),
            Money, cost.Where(entry => ArmBook.IsSilver(entry.Item)).Sum(entry => (long)entry.Amount)), "(evolution materials)");
    }

    public void Evolve(int view, int index)
    {
        if (ArmEvolTarget(view, index) is not { } found) return;
        ArmUpgradeRules rules = ArmBook.Rules;
        ArmUpgradeState state = ArmState(found.Item);
        if (state.Evol >= rules.EvolMax)
        {
            SystemMessage("This equipment cannot evolve any further.", SystemMessageKind.Prompt);
            return;
        }
        List<ArmUpgradeAmount> cost = EvolCost(found.Item.Name, state.Evol);
        if (!ArmAfford(cost)) return;
        ArmPay(cost);
        if (!ArmRoll(rules.EvolChancePercent))
        {
            SystemMessage("The evolution failed. The materials were used up.", SystemMessageKind.Prompt);
            OpenEvolView(found.View, found.Index);
            ArmUpgradeDone($"fails to evolve {found.Item.Name}");
            return;
        }
        string property = ArmMainProperty(found.Item.Name);
        state.Evol++;
        state.Bonus[property] = state.Bonus.GetValueOrDefault(property) + EvolGain(found.Item.Name);
        RefreshArmItem(found.View, found.Index, "(evolved equipment)");
        Send(ArmUpgradePackets.SucMake(ArmUpgradePackets.EvolSuccess), "(evolution succeeded)");
        SystemMessage($"The equipment evolved to stage {state.Evol}.");
        OpenEvolView(found.View, found.Index);
        ArmUpgradeDone($"evolves {found.Item.Name} to stage {state.Evol}");
    }

    public void OpenStarView(int view, int index)
    {
        if (ArmStarTarget(view, index) is not { } found) return;
        ArmUpgradeState state = ArmState(found.Item);
        SendStarInfo(found.Item, state);
    }

    public void MakeStar(int view, int index)
    {
        if (ArmStarTarget(view, index) is not { } found) return;
        ArmUpgradeRules rules = ArmBook.Rules;
        ArmUpgradeState state = ArmState(found.Item);
        if (state.Stars >= rules.StarMax)
        {
            SystemMessage("This equipment already has the most stars.", SystemMessageKind.Prompt);
            return;
        }
        List<ArmUpgradeAmount> cost = StarCost(found.Item.Name, state.Stars);
        if (!ArmAfford(cost)) return;
        ArmPay(cost);
        if (!ArmRoll(StarChance(state)))
        {
            state.StarLuck = Math.Min(rules.StarLuckMax, state.StarLuck + rules.StarLuckPerFailure);
            SystemMessage("The star forging failed, but the equipment gained blessing.", SystemMessageKind.Prompt);
            RefreshArmItem(found.View, found.Index, "(star forging failed)");
            SendStarInfo(found.Item, state);
            ArmUpgradeDone($"fails to add a star to {found.Item.Name}");
            return;
        }
        string property = ArmMainProperty(found.Item.Name);
        state.Stars++;
        state.StarLuck = 0;
        state.PropPoints += rules.PropPointsPerStar;
        state.Bonus[property] = state.Bonus.GetValueOrDefault(property) + rules.StarGainPerStar;
        RefreshArmItem(found.View, found.Index, "(star added)");
        Send(ArmUpgradePackets.SucMake(ArmUpgradePackets.StarSuccess), "(star forging succeeded)");
        SystemMessage($"The equipment now has {state.Stars} star(s).");
        SendStarInfo(found.Item, state);
        ArmUpgradeDone($"adds star {state.Stars} to {found.Item.Name}");
    }

    public void SaveItemMemory(int view, int index, string properties)
    {
        if (ArmUpgradeItem(view, index) is not { } found || !IsEquipment(found.Item.Name)) return;
        ArmUpgradeState state = ArmState(found.Item);
        List<string> chosen = ArmPropertyList(properties);
        if (chosen.Count == 0) return;
        ArmUpgradeRules rules = ArmBook.Rules;
        var cost = new List<ArmUpgradeAmount>();
        if (rules.MemoryCostItem.Length > 0 && rules.MemoryCostAmount > 0)
            cost.Add(new ArmUpgradeAmount { Item = rules.MemoryCostItem, Amount = rules.MemoryCostAmount * chosen.Count });
        if (!ArmAfford(cost)) return;
        ArmPay(cost);
        foreach (string property in chosen) state.Memory[property] = state.Refine.GetValueOrDefault(property);
        RefreshArmItem(found.View, found.Index, "(item memory saved)");
        SystemMessage("The attribute values were kept in the equipment's memory.");
        ArmUpgradeDone($"saves item memory of {found.Item.Name}: {string.Join(',', chosen)}");
    }

    public void LoadItemMemory(int view, int index, string properties, int row)
    {
        if (ArmUpgradeItem(view, index) is not { } found || found.Item.Upgrade is not { } state) return;
        List<string> chosen = ArmPropertyList(properties).Where(state.Memory.ContainsKey).ToList();
        if (chosen.Count == 0)
        {
            SystemMessage("Nothing is kept in the equipment's memory.", SystemMessageKind.Prompt);
            return;
        }
        foreach (string property in chosen) state.Refine[property] = state.Memory[property];
        RefreshArmItem(found.View, found.Index, "(item memory restored)");
        SystemMessage("The kept attribute values were restored.");
        ArmUpgradeDone($"restores item memory of {found.Item.Name} (row {row}): {string.Join(',', chosen)}");
    }

    private static List<string> ArmPropertyList(string text)
        => text.Replace('\u0003', ';').Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

    private (int View, int Index, ItemStack Item)? ArmEvolTarget(int view, int index)
    {
        if (ArmUpgradeItem(view, index) is not { } found) return null;
        if (!IsEquipment(found.Item.Name) || ItemCatalog.Text(found.Item.Name, "JewelryProp").Length > 0)
        {
            SystemMessage("This article cannot evolve.", SystemMessageKind.Prompt);
            return null;
        }
        return found;
    }

    private (int View, int Index, ItemStack Item)? ArmStarTarget(int view, int index)
    {
        if (ArmUpgradeItem(view, index) is not { } found) return null;
        if (!IsEquipment(found.Item.Name))
        {
            SystemMessage("Stars can only be added to equipment.", SystemMessageKind.Prompt);
            return null;
        }
        return found;
    }

    private int EvolGain(string name)
    {
        ArmUpgradeRules rules = ArmBook.Rules;
        int basis = ItemCatalog.Number(name, ArmMainProperty(name));
        return Math.Max(rules.EvolMinimumGain, basis * rules.EvolGainPercent / 100);
    }

    private List<ArmUpgradeAmount> EvolCost(string name, int evol)
    {
        ArmUpgradeRules rules = ArmBook.Rules;
        int level = ItemCatalog.Number(name, "NeedWorkLevel", 160);
        List<string> materials = ArmBook.EvolMaterials(level, ArmTypeOf(name), evol + 1);
        if (materials.Count == 0) materials = ArmBook.EvolMaterials(160, ArmTypeOf(name), evol + 1);
        int amount = rules.EvolMaterialBase + rules.EvolMaterialPerStep * evol;
        var cost = materials.Where(item => !ArmBook.IsSilver(item))
            .Select(item => new ArmUpgradeAmount { Item = item, Amount = amount }).ToList();
        cost.Add(new ArmUpgradeAmount { Item = ArmBook.Data.SilverItem, Amount = rules.EvolSilverPerStep * (evol + 1) });
        return cost;
    }

    private bool ArmIsSea(string name) => ItemCatalog.Number(name, "AddCannonHR") > 0 || ItemCatalog.Number(name, "ShipArmType") > 0;

    private List<ArmUpgradeAmount> StarCost(string name, int stars)
    {
        ArmUpgradeRules rules = ArmBook.Rules;
        bool sea = ArmIsSea(name);
        int amount = rules.StarMaterialBase + rules.StarMaterialPerTier * ArmBook.StarTier(sea, stars);
        var cost = ArmBook.StarMaterials(sea, ArmTypeOf(name), stars)
            .Select(item => new ArmUpgradeAmount { Item = item, Amount = amount }).ToList();
        cost.Add(new ArmUpgradeAmount { Item = ArmBook.Data.SilverItem, Amount = rules.StarSilverPerStar * (stars + 1) });
        return cost;
    }

    private int StarChance(ArmUpgradeState state)
    {
        ArmUpgradeRules rules = ArmBook.Rules;
        int chance = Math.Max(rules.StarChanceMinimumPercent, rules.StarChanceStartPercent - rules.StarChanceLossPerStar * state.Stars);
        return Math.Min(100, chance + state.StarLuck / 2);
    }

    private void SendStarInfo(ItemStack item, ArmUpgradeState state)
    {
        ArmUpgradeRules rules = ArmBook.Rules;
        int baseChance = Math.Max(rules.StarChanceMinimumPercent, rules.StarChanceStartPercent - rules.StarChanceLossPerStar * state.Stars);
        Send(ArmUpgradePackets.SetStarInfo(false, state.StarLuck, Math.Clamp(StarBarRange - 2 * baseChance, 0, StarBarRange)), "(star information)");
        List<ArmUpgradeAmount> cost = StarCost(item.Name, state.Stars);
        Send(ArmUpgradePackets.StuffList(cost.Where(entry => !ArmBook.IsSilver(entry.Item)).Take(StarMaterialSlots)
            .Select(entry => (ArmHave(entry.Item), (long)entry.Amount))), "(star materials)");
    }
}
