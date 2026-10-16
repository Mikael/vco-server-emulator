using BountyBay.Core.Data;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int ArmSuitView = 44;
    public const int NewArmUpdateView = 59;
    public const int ArmRankView = 74;

    private const int NewArmMaterialSlots = 6;
    private const int SuitMaterialSlots = 5;
    private const int SuitResultIndex = 6;

    private readonly List<string> _suitGroups = [];
    private readonly HashSet<int> _suitShown = [];
    private string _suitGroup = "";
    private bool _suitOpen;
    private bool _newArmOpen;
    private (int View, int Index)? _rankBox;

    public void OpenNewArmUpdate(int mode)
    {
        if (!_newArmOpen)
        {
            _newArmOpen = true;
            Send(ViewPackets.Create(NewArmUpdateView, 2), "(equipment upgrade view)");
        }
        Send(ArmUpgradePackets.ClearNewArmUpdate(), "(equipment upgrade reset)");
        Send(ArmUpgradePackets.OpenNewUpdate(mode), "(open equipment upgrade)");
    }

    public void CloseNewArmUpdate()
    {
        if (!_newArmOpen) return;
        _newArmOpen = false;
        Send(ViewPackets.Delete(NewArmUpdateView), "(close equipment upgrade view)");
    }

    public void SelectUpdateType(int option, string name, string itemId)
    {
        if (NewArmTarget(option, name, itemId) is not { } target) return;
        if (!_newArmOpen)
        {
            _newArmOpen = true;
            Send(ViewPackets.Create(NewArmUpdateView, 2), "(equipment upgrade view)");
        }
        Send(ViewPackets.RemoveItem(NewArmUpdateView, 1), "(upgrade source)");
        Send(ViewPackets.RemoveItem(NewArmUpdateView, 2), "(upgrade result)");
        Send(ViewPackets.AddItem(NewArmUpdateView, 1, ItemProperties(target.Found.Item, 1)), "(upgrade source)");
        var result = new ItemStack { Name = target.Path.Result, Photo = Context.Data.PhotoOf(target.Path.Result), Upgrade = target.Found.Item.Upgrade?.Clone() };
        Send(ViewPackets.AddItem(NewArmUpdateView, 2, ItemProperties(result, 2)), "(upgrade result)");
        List<ArmUpgradeAmount> cost = ArmBook.Recipe(target.Path.Recipe);
        List<(long, long)> materials = cost
            .Where(entry => !ArmBook.IsSilver(entry.Item) && !entry.Item.Equals(target.Found.Item.Name, StringComparison.OrdinalIgnoreCase))
            .Take(NewArmMaterialSlots).Select(entry => (ArmHave(entry.Item), (long)entry.Amount)).ToList();
        while (materials.Count < NewArmMaterialSlots) materials.Add((0, 0));
        long silver = cost.Where(entry => ArmBook.IsSilver(entry.Item)).Sum(entry => (long)entry.Amount);
        Send(ArmUpgradePackets.NewStuffAmount(materials, Money, silver), "(upgrade materials)");
    }

    public void NewArmUpdate(int option, string name, string itemId)
    {
        if (NewArmTarget(option, name, itemId) is not { } target) return;
        ArmUpgradePath path = target.Path;
        ItemStack source = target.Found.Item;
        if (path.NeedProp.Length > 0 && ArmUpgradeValue(source, path.NeedProp) < path.NeedValue)
        {
            SystemMessage(path.NeedProp == "StarProp"
                ? $"The equipment needs {path.NeedValue} stars before it can be upgraded."
                : $"The equipment must evolve to stage {path.NeedValue} before it can be upgraded.", SystemMessageKind.Prompt);
            return;
        }
        if (!MayWear(path.Result)) return;
        List<ArmUpgradeAmount> cost = ArmBook.Recipe(path.Recipe);
        if (!ArmAfford(cost, source.Name)) return;
        ArmPay(cost, source.Name);
        var result = new ItemStack { Name = path.Result, Photo = Context.Data.PhotoOf(path.Result), Amount = 1, Upgrade = source.Upgrade?.Clone() };
        if (result.Upgrade != null && path.NeedProp == "EvolProp") result.Upgrade.Evol = 0;
        ReplaceArmItem(target.Found.View, target.Found.Index, result, "(upgraded equipment)");
        Send(ArmUpgradePackets.ClearNewArmUpdate(), "(equipment upgrade reset)");
        SystemMessage("The equipment was upgraded successfully.");
        ArmUpgradeDone($"upgrades {source.Name} into {result.Name} (option {option})");
    }

    private (ArmUpgradePath Path, (int View, int Index, ItemStack Item) Found)? NewArmTarget(int option, string name, string itemId)
    {
        string key = itemId.Length > 0 && itemId != "-" ? itemId : name;
        if ((ArmUpgradeItemNamed(key) ?? ArmUpgradeItemNamed(name)) is not { } item)
        {
            SystemMessage("Put the equipment you want to upgrade in your bag first.", SystemMessageKind.Prompt);
            return null;
        }
        ArmUpgradePath? path = ArmBook.Path(item.Item.Name, option);
        if (path == null)
        {
            SystemMessage("This equipment cannot be upgraded that way.", SystemMessageKind.Prompt);
            return null;
        }
        return (path, item);
    }

    private static int ArmUpgradeValue(ItemStack item, string property) => property switch
    {
        "EvolProp" => item.Upgrade?.Evol ?? 0,
        "StarProp" => item.Upgrade?.Stars ?? 0,
        _ => ItemCatalog.Number(item.Name, property),
    };

    public void OpenSuitExchange()
    {
        if (!_suitOpen)
        {
            _suitOpen = true;
            Send(ViewPackets.Create(ArmSuitView, SuitResultIndex), "(suit exchange view)");
        }
        ClearSuitSlots();
        _suitGroups.Clear();
        _suitGroups.AddRange(ArmBook.Data.Suits.SelectMany(suit => suit.Members).Distinct());
        _suitGroup = "";
        Send(ArmUpgradePackets.SuitSystem(_suitGroups), "(suit list)");
    }

    public void CloseSuitExchange()
    {
        _suitGroup = "";
        if (!_suitOpen) return;
        _suitOpen = false;
        _suitShown.Clear();
        Send(ViewPackets.Delete(ArmSuitView), "(close suit exchange view)");
    }

    public void OpenSubAssembly(int group, string name)
    {
        _suitGroup = group >= 0 && group < _suitGroups.Count ? _suitGroups[group] : name;
        ClearSuitSlots();
        List<ArmUpgradeSuitPiece> pieces = SuitPieces(_suitGroup);
        if (pieces.Count == 0) SystemMessage("No pieces of this suit can be exchanged here.", SystemMessageKind.Prompt);
        Send(ArmUpgradePackets.InitAssembly(pieces.Select(piece => (piece.Name, BagTotal(piece.Source) > 0 || ArmUpgradeItemNamed(piece.Source) != null)),
            Enumerable.Range(1, ArmBook.Rules.SuitChanceMaterialPercent.Count)), "(suit pieces)");
    }

    public void SelectAssembly(int group, int piece, string name, int chance)
    {
        if (SuitPiece(group, piece, name) is not { } chosen) return;
        ClearSuitSlots();
        if (ArmUpgradeItemNamed(chosen.Source) is { } owned) ShowSuitSlot(1, owned.Item);
        else ShowSuitSlot(1, new ItemStack { Name = chosen.Source, Photo = Context.Data.PhotoOf(chosen.Source) });
        List<ArmUpgradeAmount> cost = SuitCost(chosen, chance);
        int slot = 2;
        foreach (ArmUpgradeAmount material in cost.Where(entry => !ArmBook.IsSilver(entry.Item)).Take(SuitMaterialSlots - 1))
            ShowSuitSlot(slot++, new ItemStack { Name = material.Item, Photo = Context.Data.PhotoOf(material.Item), Amount = material.Amount });
        ShowSuitSlot(SuitResultIndex, new ItemStack { Name = chosen.Result, Photo = Context.Data.PhotoOf(chosen.Result) });
        var amounts = new List<(long, long)> { (ArmUpgradeItemNamed(chosen.Source) != null ? 1 : 0, 1) };
        amounts.AddRange(cost.Where(entry => !ArmBook.IsSilver(entry.Item)).Take(SuitMaterialSlots - 1)
            .Select(material => (ArmHave(material.Item), (long)material.Amount)));
        while (amounts.Count < SuitMaterialSlots) amounts.Add((0, 0));
        Send(ArmUpgradePackets.UpdateAmount(amounts), "(suit materials)");
    }

    public void EnsureSuit(int piece, int chance, int group, string name)
    {
        if (SuitPiece(group, piece, name) is not { } entry) return;
        if (ArmUpgradeItemNamed(entry.Source) is not { } owned)
        {
            SystemMessage("You do not have the suit piece to exchange.", SystemMessageKind.Prompt);
            return;
        }
        List<ArmUpgradeAmount> cost = SuitCost(entry, chance);
        if (!ArmAfford(cost)) return;
        ArmPay(cost);
        List<int> tiers = ArmBook.Rules.SuitChanceMaterialPercent;
        int percent = chance >= 1 && chance <= tiers.Count ? tiers[chance - 1] : Math.Clamp(chance, 0, 100);
        if (!ArmRoll(percent))
        {
            SystemMessage("The exchange failed. The materials were used up.", SystemMessageKind.Prompt);
            SelectAssembly(group, piece, name, chance);
            ArmUpgradeDone($"fails to exchange {owned.Item.Name}");
            return;
        }
        var result = new ItemStack { Name = entry.Result, Photo = Context.Data.PhotoOf(entry.Result), Amount = 1, Upgrade = owned.Item.Upgrade?.Clone() };
        ReplaceArmItem(owned.View, owned.Index, result, "(exchanged suit piece)");
        ClearSuitSlots();
        SystemMessage("The suit piece was exchanged successfully.");
        ArmUpgradeDone($"exchanges {owned.Item.Name} into {result.Name}");
    }

    private List<ArmUpgradeSuitPiece> SuitPieces(string group)
        => ArmBook.Data.SuitPieces.TryGetValue(group, out List<ArmUpgradeSuitPiece>? pieces) ? pieces : [];

    private ArmUpgradeSuitPiece? SuitPiece(int group, int piece, string name)
    {
        string key = group >= 0 && group < _suitGroups.Count ? _suitGroups[group] : _suitGroup;
        List<ArmUpgradeSuitPiece> pieces = SuitPieces(key);
        ArmUpgradeSuitPiece? entry = piece >= 1 && piece <= pieces.Count ? pieces[piece - 1]
            : pieces.FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entry == null) SystemMessage("Choose a suit piece first.", SystemMessageKind.Prompt);
        return entry;
    }

    private List<ArmUpgradeAmount> SuitCost(ArmUpgradeSuitPiece piece, int chance)
    {
        int factor = Math.Clamp(chance, 1, Math.Max(1, ArmBook.Rules.SuitChanceMaterialPercent.Count));
        return piece.Materials.Select(entry => new ArmUpgradeAmount { Item = entry.Item, Amount = entry.Amount * factor }).ToList();
    }

    private void ShowSuitSlot(int index, ItemStack item)
    {
        if (!_suitOpen) return;
        if (_suitShown.Remove(index)) Send(ViewPackets.RemoveItem(ArmSuitView, index), "(suit slot)");
        _suitShown.Add(index);
        Send(ViewPackets.AddItem(ArmSuitView, index, ItemProperties(item, index)), "(suit slot)");
    }

    private void ClearSuitSlots()
    {
        foreach (int index in _suitShown.Order().ToList()) Send(ViewPackets.RemoveItem(ArmSuitView, index), "(suit slot)");
        _suitShown.Clear();
    }

    public void OpenArmRankView()
    {
        _rankBox = null;
        Send(ViewPackets.Create(ArmRankView, 1), "(rank equipment view)");
    }

    public void CloseArmRankView()
    {
        _rankBox = null;
        Send(ViewPackets.Delete(ArmRankView), "(close rank equipment view)");
    }

    public bool ArmRankPut(int view, int index)
    {
        if (ArmUpgradeItem(view, index) is not { } found) return false;
        DropHand();
        if (!IsEquipment(found.Item.Name))
        {
            SystemMessage("Only equipment can be upgraded here.", SystemMessageKind.Prompt);
            return true;
        }
        if (_rankBox != null) Send(ViewPackets.RemoveItem(ArmRankView, 1), "(rank equipment slot)");
        _rankBox = (found.View, found.Index);
        Send(ViewPackets.AddItem(ArmRankView, 1, RankBoxProperties(found.Item)), "(rank equipment slot)");
        SendRankCosts(found.Item);
        return true;
    }

    public void ArmRankTake()
    {
        DropHand();
        if (_rankBox == null) return;
        _rankBox = null;
        Send(ViewPackets.RemoveItem(ArmRankView, 1), "(rank equipment slot)");
    }

    public void ArmRankUp(bool quality)
    {
        if (_rankBox is not { } box || ArmUpgradeItem(box.View, box.Index) is not { } found)
        {
            SystemMessage("Put the equipment in the upgrade slot first.", SystemMessageKind.Prompt);
            return;
        }
        ArmUpgradeRules rules = ArmBook.Rules;
        ArmUpgradeState state = ArmState(found.Item);
        if (quality ? state.RankQuality >= rules.RankQualityMax : state.RankLevel >= rules.RankLevelMax)
        {
            SystemMessage("The equipment has reached the max level.", SystemMessageKind.Prompt);
            return;
        }
        List<ArmUpgradeAmount> cost = RankCost(state, quality);
        if (!ArmAfford(cost)) return;
        ArmPay(cost);
        string property = ArmMainProperty(found.Item.Name);
        if (quality) state.RankQuality++;
        else state.RankLevel++;
        state.Bonus[property] = state.Bonus.GetValueOrDefault(property) + EvolGain(found.Item.Name);
        RefreshArmItem(found.View, found.Index, "(rank equipment upgraded)");
        Send(ViewPackets.UpdateItem(ArmRankView, 1, RankBoxProperties(found.Item)), "(rank equipment slot)");
        SendRankCosts(found.Item);
        SystemMessage(quality ? $"The equipment quality rose to {state.RankQuality}." : $"The equipment rose to level {state.RankLevel}.");
        ArmUpgradeDone($"raises {(quality ? "quality" : "level")} of {found.Item.Name}");
    }

    private PropertyBag RankBoxProperties(ItemStack item)
    {
        PropertyBag bag = ItemProperties(item, 1);
        ArmUpgradeState state = ArmState(item);
        if (PropertyTable.Has("Level")) bag["Level"] = state.RankLevel;
        if (PropertyTable.Has("QualityLevel")) bag["QualityLevel"] = state.RankQuality;
        if (PropertyTable.Has("DarkQuality")) bag["DarkQuality"] = state.RankQuality;
        return bag;
    }

    private List<ArmUpgradeAmount> RankCost(ArmUpgradeState state, bool quality)
    {
        ArmUpgradeRules rules = ArmBook.Rules;
        int step = quality ? state.RankQuality + 1 : state.RankLevel + 1;
        var cost = (quality ? rules.RankQualityMaterials : rules.RankMaterials)
            .Select(entry => new ArmUpgradeAmount { Item = entry.Item, Amount = entry.Amount * step }).ToList();
        if (!quality) cost.Add(new ArmUpgradeAmount { Item = ArmBook.Data.SilverItem, Amount = rules.RankSilverPerLevel * step });
        return cost;
    }

    private void SendRankCosts(ItemStack item)
    {
        ArmUpgradeState state = ArmState(item);
        List<ArmUpgradeAmount> level = RankCost(state, false);
        Send(ArmUpgradePackets.ShowRankArmUp(level.Where(entry => ArmBook.IsSilver(entry.Item)).Sum(entry => (long)entry.Amount),
            level.Where(entry => !ArmBook.IsSilver(entry.Item)).Select(entry => (entry.Item, entry.Amount))), "(rank level cost)");
        Send(ArmUpgradePackets.ShowRankStuff(RankCost(state, true).Where(entry => !ArmBook.IsSilver(entry.Item))
            .Select(entry => (entry.Item, ArmHave(entry.Item), (long)entry.Amount))), "(rank quality cost)");
    }
}
