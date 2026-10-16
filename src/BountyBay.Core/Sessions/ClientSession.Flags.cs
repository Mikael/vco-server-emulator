using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int FireFlagView = 63;
    public const int FireFlagArmType = 14;

    private const int FlagViewCapacity = 10;
    private const int FlagCurrentIndex = 1;
    private const int FlagNextIndex = 2;
    private const int FlagForgeStuffIndex = 3;
    private const int FlagRemakeStuffIndex = 7;
    private const int FlagStuffSlots = 3;
    private const int FlagUpgradePage = 1;
    private const int FlagRemakePage = 2;

    private static readonly List<FlagMaterial> NoFlagMaterials = [];

    private readonly HashSet<int> _flagShown = [];
    private bool _flagViewOpen;

    public int FlagHonor => Character.FlagHonor < 0 ? FireFlagRules.For(Context.Settings).StartingHonor : Character.FlagHonor;

    public static void AddFireFlagItemProperties(PropertyBag bag, ItemStack item)
    {
        if (!FireFlagRules.TryParse(item.Name, out _, out _)) return;
        if (PropertyTable.Has("NeedWorkLevel") && !bag.ContainsKey("NeedWorkLevel")) bag["NeedWorkLevel"] = 1;
        if (PropertyTable.Has("ArmType")) bag["ArmType"] = FireFlagArmType;
    }

    public void ApplyFlagHonorProperty() => PutBboProperty("FlagHonorExp", FlagHonor);

    public void AddFlagHonor(int amount)
    {
        Character.FlagHonor = (int)Math.Clamp((long)FlagHonor + amount, 0, int.MaxValue);
        if (PropertyTable.Has("FlagHonorExp")) SetProperty("FlagHonorExp", Character.FlagHonor);
    }

    public void OpenFlagView()
    {
        if (_flagViewOpen) Send(ViewPackets.Delete(FireFlagView), "(fire flag view)");
        _flagViewOpen = true;
        _flagShown.Clear();
        Send(ViewPackets.Create(FireFlagView, FlagViewCapacity), "(fire flag view)");
        if (PropertyTable.Has("FlagHonorExp")) SetProperty("FlagHonorExp", FlagHonor);
    }

    public void CloseFlagView()
    {
        if (!_flagViewOpen) return;
        _flagViewOpen = false;
        _flagShown.Clear();
        Send(ViewPackets.Delete(FireFlagView), "(close fire flag view)");
    }

    public void ClearFlagView()
    {
        if (!_flagViewOpen) return;
        foreach (int index in _flagShown.Order().ToList()) Send(ViewPackets.RemoveItem(FireFlagView, index), "(fire flag removed)");
        _flagShown.Clear();
    }

    public void StartForge(string name, string itemId)
    {
        if (!_flagViewOpen) OpenFlagView();
        if (FindFlag(name, itemId) is not { } found)
        {
            ClearFlagView();
            SystemMessage("Please put a battle flag from your bag in this column.", SystemMessageKind.Prompt);
            return;
        }
        ShowFireFlag(found.View, found.Slot);
    }

    public void ForgeFlag(string name, string itemId)
    {
        FireFlagRules rules = FireFlagRules.For(Context.Settings);
        if (FindFlag(name, itemId) is not { } found)
        {
            SystemMessage("Please put a battle flag from your bag in this column.", SystemMessageKind.Prompt);
            return;
        }
        ItemStack stack = MailBagOf(found.View)![found.Slot];
        FireFlagRules.TryParse(stack.Name, out int type, out int grade);
        int level = FireFlagRules.LevelOf(grade);
        FireFlagLevel rule = rules.Level(level);
        FireFlagState state = FlagState(stack.Name, type, level);
        string? promoted = FireFlagRules.Promoted(stack.Name);
        if (state.Index >= rule.MaxIndex && promoted == null)
        {
            SystemMessage("This battle flag cannot be improved any further.", SystemMessageKind.Prompt);
            FlagRestore(found, FlagUpgradePage);
            return;
        }
        if (!PayFlagCost(rule.ForgeMaterials, rule.ForgeHonor, rule.ForgeSilver))
        {
            FlagRestore(found, FlagUpgradePage);
            return;
        }
        if (state.Index < rule.MaxIndex)
        {
            state.Index++;
            SystemMessage($"The {FlagLabel(stack.Name)} was forged ({state.Index}/{rule.MaxIndex}).", SystemMessageKind.Upgrade);
            Log.Info(Tag, $"{Name} forges {stack.Name} to step {state.Index}");
        }
        else
        {
            PromoteFlag(found.View, found.Slot, stack, promoted!, type, state);
        }
        Save();
        Send(FlagFusePackets.ForgeReturn(found.View, found.Slot), "(fire flag forged)");
    }

    public void RemakeFlag(string name, string itemId, int oldProp, int newProp)
    {
        FireFlagRules rules = FireFlagRules.For(Context.Settings);
        if (FindFlag(name, itemId) is not { } found)
        {
            SystemMessage("Please put a battle flag from your bag in this column.", SystemMessageKind.Prompt);
            return;
        }
        ItemStack stack = MailBagOf(found.View)![found.Slot];
        FireFlagRules.TryParse(stack.Name, out int type, out int grade);
        int level = FireFlagRules.LevelOf(grade);
        FireFlagState state = FlagState(stack.Name, type, level);
        int slot = state.Props.IndexOf(oldProp);
        if (slot < 0 || !FireFlagRules.Owns(type, newProp) || rules.Prop(newProp) == null || state.Props.Contains(newProp))
        {
            SystemMessage("Choose one attribute of the flag and one new attribute to replace it.", SystemMessageKind.Prompt);
            FlagRestore(found, FlagRemakePage);
            return;
        }
        FireFlagLevel rule = rules.Level(level);
        if (!PayFlagCost(rule.RemakeMaterials, rule.RemakeHonor, rule.RemakeSilver))
        {
            FlagRestore(found, FlagRemakePage);
            return;
        }
        state.Props[slot] = newProp;
        if (state.Pending == newProp) state.Pending = PickFlagProp(type, state.Props);
        Save();
        SystemMessage($"The {FlagLabel(stack.Name)} now carries {rules.Prop(newProp)!.Name} instead of {rules.Prop(oldProp)?.Name ?? "the old attribute"}.",
            SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} remakes {stack.Name}: attribute {oldProp} -> {newProp}");
        Send(FlagFusePackets.ForgeReturn(found.View, found.Slot), "(fire flag remade)");
    }

    private void PromoteFlag(int view, int slot, ItemStack stack, string promoted, int type, FireFlagState state)
    {
        FireFlagRules rules = FireFlagRules.For(Context.Settings);
        string before = stack.Name;
        Character.FireFlags.Remove(before);
        FireFlagRules.TryParse(promoted, out _, out int grade);
        int level = FireFlagRules.LevelOf(grade);
        state.Index = 0;
        int wanted = Math.Clamp(rules.Level(level).Props, 1, FireFlagRules.PropsPerType);
        while (state.Props.Count < wanted)
        {
            int extra = state.Pending >= 0 && !state.Props.Contains(state.Pending) && FireFlagRules.Owns(type, state.Pending)
                ? state.Pending
                : PickFlagProp(type, state.Props);
            if (extra < 0) break;
            state.Props.Add(extra);
            state.Pending = -1;
        }
        state.Pending = PickFlagProp(type, state.Props);
        Character.FireFlags[promoted] = state;
        stack.Name = promoted;
        stack.Photo = Context.Data.PhotoOf(promoted);
        Send(ViewPackets.RemoveItem(view, slot), "(battle flag promoted)");
        ShowBagSlot(view, slot);
        SystemMessage($"The {FlagLabel(before)} was promoted to {FlagLabel(promoted)}.", SystemMessageKind.Upgrade);
        Log.Info(Tag, $"{Name} promotes {before} to {promoted}");
    }

    private void ShowFireFlag(int view, int slot)
    {
        FireFlagRules rules = FireFlagRules.For(Context.Settings);
        ClearFlagView();
        ItemStack stack = MailBagOf(view)![slot];
        FireFlagRules.TryParse(stack.Name, out int type, out int grade);
        int level = FireFlagRules.LevelOf(grade);
        FireFlagLevel rule = rules.Level(level);
        FireFlagState state = FlagState(stack.Name, type, level);
        ShowFlagItem(FlagCurrentIndex, FlagItem(stack, FlagCurrentIndex, type, level, state.Index, state.Props, rule.MaxIndex));
        string? promoted = FireFlagRules.Promoted(stack.Name);
        bool forgeable = state.Index < rule.MaxIndex || promoted != null;
        if (state.Index < rule.MaxIndex)
        {
            ShowFlagItem(FlagNextIndex, FlagItem(stack, FlagNextIndex, type, level, state.Index + 1, state.Props, rule.MaxIndex));
        }
        else if (promoted != null)
        {
            FireFlagRules.TryParse(promoted, out _, out int nextGrade);
            int nextLevel = FireFlagRules.LevelOf(nextGrade);
            List<int> nextProps = [.. state.Props];
            if (nextProps.Count < rules.Level(nextLevel).Props && state.Pending >= 0 && !nextProps.Contains(state.Pending)) nextProps.Add(state.Pending);
            var next = new ItemStack { Name = promoted, Photo = Context.Data.PhotoOf(promoted), Amount = 1 };
            ShowFlagItem(FlagNextIndex, FlagItem(next, FlagNextIndex, type, nextLevel, 0, nextProps, rules.Level(nextLevel).MaxIndex));
        }
        ShowFlagStuff(FlagForgeStuffIndex, forgeable ? rule.ForgeMaterials : NoFlagMaterials);
        ShowFlagStuff(FlagRemakeStuffIndex, rule.RemakeMaterials);
        string forge = FlagAmounts(forgeable ? rule.ForgeMaterials : NoFlagMaterials, forgeable ? rule.ForgeHonor : 0, forgeable);
        string remake = FlagAmounts(rule.RemakeMaterials, rule.RemakeHonor, true);
        Send(FlagFusePackets.FlagStuffAmount(forge, remake), "(fire flag materials)");
    }

    private PropertyBag FlagItem(ItemStack stack, int index, int type, int level, int flagIndex, List<int> props, int maxIndex)
    {
        FireFlagRules rules = FireFlagRules.For(Context.Settings);
        PropertyBag bag = ItemProperties(stack, index);
        AddFireFlagItemProperties(bag, stack);
        FlagPut(bag, "Level", level);
        FlagPut(bag, "QualityLevel", level);
        FlagPut(bag, "FlagType", type);
        FlagPut(bag, "FlagIndex", flagIndex);
        FlagPut(bag, "FlagFinal", flagIndex >= maxIndex ? 1 : 0);
        FlagPut(bag, "FlagProp", string.Join(' ', props));
        for (int slot = 0; slot < props.Count; slot++)
        {
            if (rules.Prop(props[slot]) is not { } prop) continue;
            int value = prop.Value(level, FireFlagRules.Steps(flagIndex, slot, props.Count));
            FlagPut(bag, prop.Prop, prop.Float ? (object)(float)value : value);
        }
        return bag;
    }

    private void ShowFlagStuff(int first, List<FlagMaterial> materials)
    {
        for (int i = 0; i < Math.Min(FlagStuffSlots, materials.Count); i++)
        {
            FlagMaterial material = materials[i];
            var stuff = new ItemStack { Name = material.Name, Photo = Context.Data.PhotoOf(material.Name), Amount = material.Amount };
            ShowFlagItem(first + i, ItemProperties(stuff, first + i));
        }
    }

    private void ShowFlagItem(int index, PropertyBag bag)
    {
        if (_flagShown.Remove(index)) Send(ViewPackets.RemoveItem(FireFlagView, index), "(fire flag slot)");
        _flagShown.Add(index);
        Send(ViewPackets.AddItem(FireFlagView, index, bag), "(fire flag slot)");
    }

    private string FlagAmounts(List<FlagMaterial> materials, int honor, bool possible)
    {
        var parts = new List<string>();
        for (int i = 0; i < FlagStuffSlots; i++)
        {
            parts.Add(possible && i < materials.Count
                ? $"{BagTotal(materials[i].Name)},{materials[i].Amount}"
                : FlagFusePackets.NoAmount);
        }
        parts.Add(possible && honor > 0 ? $"{FlagHonor},{honor}" : FlagFusePackets.NoAmount);
        return string.Join(',', parts);
    }

    private bool PayFlagCost(List<FlagMaterial> materials, int honor, int silver)
    {
        foreach (FlagMaterial material in materials.Take(FlagStuffSlots))
        {
            if (BagTotal(material.Name) >= material.Amount) continue;
            SystemMessage($"You need {material.Amount} x {FlagLabel(material.Name)}.", SystemMessageKind.Prompt);
            return false;
        }
        if (FlagHonor < honor)
        {
            SystemMessage($"You need {honor} Honor Of The Battle Field.", SystemMessageKind.Prompt);
            return false;
        }
        if (Money < silver)
        {
            SystemMessage($"You need {silver} silver coins.", SystemMessageKind.Prompt);
            return false;
        }
        foreach (FlagMaterial material in materials.Take(FlagStuffSlots)) TakeFromBags(material.Name, material.Amount);
        if (honor > 0) AddFlagHonor(-honor);
        if (silver > 0) SetMoney(Money - silver);
        if (materials.Count > 0) RefreshLoad();
        return true;
    }

    private void FlagRestore((int View, int Slot) found, int page)
    {
        Send(FlagFusePackets.ForgeRestore(page), "(fire flag restore)");
        Send(FlagFusePackets.ForgeReturn(found.View, found.Slot), "(fire flag reload)");
    }

    private FireFlagState FlagState(string key, int type, int level)
    {
        FireFlagRules rules = FireFlagRules.For(Context.Settings);
        if (!Character.FireFlags.TryGetValue(key, out FireFlagState? state))
        {
            state = new FireFlagState();
            Character.FireFlags[key] = state;
        }
        state.Props = state.Props.Where(prop => FireFlagRules.Owns(type, prop) && rules.Prop(prop) != null).Distinct().ToList();
        int wanted = Math.Clamp(rules.Level(level).Props, 1, FireFlagRules.PropsPerType);
        while (state.Props.Count < wanted)
        {
            int extra = PickFlagProp(type, state.Props);
            if (extra < 0) break;
            state.Props.Add(extra);
        }
        if (state.Props.Count > wanted) state.Props = state.Props.Take(wanted).ToList();
        state.Index = Math.Clamp(state.Index, 0, Math.Max(0, rules.Level(level).MaxIndex));
        if (state.Pending < 0 || state.Props.Contains(state.Pending) || !FireFlagRules.Owns(type, state.Pending))
            state.Pending = PickFlagProp(type, state.Props);
        return state;
    }

    private int PickFlagProp(int type, List<int> taken)
    {
        FireFlagRules rules = FireFlagRules.For(Context.Settings);
        List<int> free = rules.Props.Select(prop => prop.Index)
            .Where(index => FireFlagRules.Owns(type, index) && !taken.Contains(index)).ToList();
        return free.Count == 0 ? -1 : free[Random.Shared.Next(free.Count)];
    }

    private (int View, int Slot)? FindFlag(string name, string itemId)
    {
        string key = FireFlagRules.TryParse(name, out _, out _) ? name : itemId;
        if (!FireFlagRules.TryParse(key, out _, out _)) return null;
        foreach (int view in BboBagViews)
        {
            foreach ((int slot, ItemStack stack) in MailBagOf(view)!.OrderBy(pair => pair.Key))
            {
                if (stack.Name == key) return (view, slot);
            }
        }
        return null;
    }

    private string FlagLabel(string key)
        => Context.Data.Items.Goods.TryGetValue(key, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : key;

    private static void FlagPut(PropertyBag bag, string name, object value)
    {
        if (PropertyTable.Has(name)) bag[name] = value;
    }
}
