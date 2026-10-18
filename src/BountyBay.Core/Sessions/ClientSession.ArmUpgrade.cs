using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class ArmUpgradeOption
{
    public const int First = 391;
    public const int NewArmUpdate = 391;
    public const int SuitExchange = 392;
    public const int Leave = 399;
    public const int Last = 399;
}

public sealed partial class ClientSession
{
    private static readonly string[] ArmUpgradeStatNames =
        ["AddAT", "AddDF", "AddHitRatio", "AddDodgeRatio", "AddMaxHP", "AddMaxSP", "AddMaxLoad"];

    private ArmUpgradeBook ArmBook => ArmUpgradeBook.For(Context.Settings);

    public static void ArmUpgradeProperties(PropertyBag bag, ItemStack item)
    {
        if (item.Upgrade is not { } state) return;
        foreach ((string name, int bonus) in state.Bonus)
        {
            if (bonus == 0 || !PropertyTable.Has(name)) continue;
            bag[name] = (bag.TryGetValue(name, out object? value) && value is not string ? Convert.ToInt32(value) : 0) + bonus;
        }
        foreach ((string name, int value) in state.Refine)
        {
            if (PropertyTable.Has(name)) bag[name] = value;
        }
        if (state.Evol > 0 && PropertyTable.Has("EvolProp")) bag["EvolProp"] = state.Evol;
        if (state.Stars > 0 && PropertyTable.Has("StarProp")) bag["StarProp"] = $"{state.Stars} {state.StarLuck}";
        if (state.Memory.Count > 0 && PropertyTable.Has("MemoryInfo"))
            bag["MemoryInfo"] = string.Join(';', state.Memory.Select(pair => $"{pair.Key} {pair.Value}"));
        foreach ((string name, int value) in state.PointValues)
        {
            if (PropertyTable.Has(name)) bag[name] = (bag.TryGetValue(name, out object? old) && old is not string ? Convert.ToInt32(old) : 0) + value;
        }
        if (state.PropPoints > 0 && PropertyTable.Has("LevelPoint")) bag["LevelPoint"] = state.PropPoints;
    }

    public static int ArmUpgradeBonus(ItemStack item, string property)
        => item.Upgrade is not { } state ? 0 : state.Bonus.GetValueOrDefault(property) + state.PointValues.GetValueOrDefault(property);

    public bool TalkArmUpgrade(NpcDefinition npc)
    {
        if (ArmBook.Rules.UpgradeMasters.Contains(npc.Name, StringComparer.OrdinalIgnoreCase))
        {
            TalkingTo = npc.Id;
            ShowArmUpgradeMenu(npc.Id);
            return true;
        }
        if (!OpenArmExchange(npc.Name)) return false;
        TalkingTo = npc.Id;
        // The client window holds the forging materials; the staff's other exchanges stay in the dialogue beside it.
        if (ExchangesOf(npc.Id) is { } groups) ShowExchangeGroups(npc.Id, groups);
        else Send(UiPackets.CloseMenu(), "(exchange opened)");
        return true;
    }

    public bool HandleArmUpgradeOption(int target, int option)
    {
        if (option < ArmUpgradeOption.First || option > ArmUpgradeOption.Last) return false;
        switch (option)
        {
            case ArmUpgradeOption.NewArmUpdate:
                OpenNewArmUpdate(0);
                return true;
            case ArmUpgradeOption.SuitExchange:
                OpenSuitExchange();
                return true;
            default:
                CloseNpcDialogue();
                return true;
        }
    }

    private void ShowArmUpgradeMenu(int target)
    {
        Send(ChatPackets.NpcDialogue(target,
        [
            (DialogueEntry.Text, 0, "Bring me your equipment and the materials, and I will make it stronger."),
            (DialogueEntry.Option, ArmUpgradeOption.NewArmUpdate, "Upgrade equipment"),
            (DialogueEntry.Option, ArmUpgradeOption.SuitExchange, "Exchange suit equipment"),
            (DialogueEntry.Option, ArmUpgradeOption.Leave, "Goodbye"),
        ]), "(upgrade master)");
    }

    private (int View, int Index, ItemStack Item)? ArmUpgradeItem(int view, int index)
    {
        if (view == ViewId.Hand && HandSource() is { } from) (view, index) = from;
        Dictionary<int, ItemStack>? slots = view switch
        {
            ViewId.BagEquip or ViewId.BagArticle or ViewId.Equipped => Bags[view],
            ViewId.BagItems => Items,
            _ => null,
        };
        if (slots == null || !slots.TryGetValue(index, out ItemStack? item)) return null;
        return (view, index, item);
    }

    private (int View, int Index, ItemStack Item)? ArmUpgradeItemNamed(string name)
    {
        foreach (int view in new[] { ViewId.BagEquip, ViewId.Equipped })
        {
            foreach ((int slot, ItemStack item) in Bags[view].OrderBy(pair => pair.Key))
            {
                if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return (view, slot, item);
            }
        }
        return null;
    }

    private static ArmUpgradeState ArmState(ItemStack item) => item.Upgrade ??= new ArmUpgradeState();

    private static bool IsEquipment(string name) => ItemCatalog.Number(name, "ItemType") == SeaPackets.ItemTypeEquipment;

    private static int ArmTypeOf(string name) => ItemCatalog.Number(name, "ArmType");

    private static string ArmMainProperty(string name) => ArmTypeOf(name) switch
    {
        1 or 2 or 3 => "AddAT",
        4 or 5 => "AddDF",
        _ => "AddMaxHP",
    };

    private void RefreshArmItem(int view, int index, string note)
    {
        Dictionary<int, ItemStack>? slots = view == ViewId.BagItems ? Items : Bags.GetValueOrDefault(view);
        if (slots == null || !slots.TryGetValue(index, out ItemStack? item)) return;
        Send(ViewPackets.UpdateItem(view, index, ItemProperties(item, index)), note);
        if (view == ViewId.Equipped) RefreshStats();
    }

    private void ReplaceArmItem(int view, int index, ItemStack item, string note)
    {
        Dictionary<int, ItemStack> slots = view == ViewId.BagItems ? Items : Bags[view];
        slots[index] = item;
        Send(ViewPackets.RemoveItem(view, index), note);
        Send(ViewPackets.AddItem(view, index, ItemProperties(item, index)), note);
        if (view is ViewId.BagEquip or ViewId.Equipped) RefreshDurability(view, index);
        if (view == ViewId.Equipped)
        {
            RefreshAppearance();
            RefreshStats();
        }
    }

    private string ArmLabel(string item)
        => Context.Data.Items.Goods.TryGetValue(item, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : item;

    private long ArmHave(string item) => ArmBook.IsSilver(item) ? Money : BagTotal(item);

    private bool ArmAfford(IEnumerable<ArmUpgradeAmount> cost, string? except = null)
    {
        foreach (ArmUpgradeAmount entry in cost)
        {
            if (except != null && entry.Item.Equals(except, StringComparison.OrdinalIgnoreCase)) continue;
            if (ArmHave(entry.Item) >= entry.Amount) continue;
            SystemMessage(ArmBook.IsSilver(entry.Item)
                ? $"You need {entry.Amount} silver coins."
                : $"You need {entry.Amount} x {ArmLabel(entry.Item)}.", SystemMessageKind.Prompt);
            return false;
        }
        return true;
    }

    private void ArmPay(IEnumerable<ArmUpgradeAmount> cost, string? except = null)
    {
        foreach (ArmUpgradeAmount entry in cost)
        {
            if (except != null && entry.Item.Equals(except, StringComparison.OrdinalIgnoreCase)) continue;
            if (ArmBook.IsSilver(entry.Item)) SetMoney(Money - entry.Amount);
            else TakeFromBags(entry.Item, entry.Amount);
        }
    }

    private static bool ArmRoll(int percent) => percent >= 100 || Random.Shared.Next(100) < Math.Max(0, percent);

    private void ArmUpgradeDone(string what)
    {
        RefreshLoad();
        Save();
        Log.Info(Tag, $"{Name} {what}");
    }
}
