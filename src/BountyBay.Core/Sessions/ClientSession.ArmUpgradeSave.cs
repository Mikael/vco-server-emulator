using BountyBay.Core.Data;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int ArmExchangeView = 38;
    public const int ArmSoulSaveTable = 96;
    private const int CharmArmType = 10;
    private const int TotemArmType = 11;

    private List<ArmUpgradeExchange> _armExchange = [];
    private bool _armExchangeOpen;

    public void OpenSaveSkill() => SendSavedSkills();

    public void SaveCharmSkill(int slot)
    {
        if (!ArmSaveSlot(slot, ArmBook.Rules.SkillSaveSlots) || ArmCharm() is not { } charm) return;
        int skill = charm.Item.Upgrade?.CharmSkill ?? 0;
        if (skill <= 0)
        {
            SystemMessage("The fight soul has no skill to keep.", SystemMessageKind.Prompt);
            return;
        }
        if (!ArmPaySave()) return;
        Character.ArmSavedSkills[slot] = $"{charm.Item.Name}_{skill}";
        SendSavedSkills();
        ArmUpgradeDone($"keeps fight soul skill {skill} in slot {slot}");
    }

    public void RestoreCharmSkill(int slot)
    {
        if (!ArmSaveSlot(slot, ArmBook.Rules.SkillSaveSlots) || ArmCharm() is not { } charm) return;
        if (!Character.ArmSavedSkills.TryGetValue(slot, out string? saved)
            || !int.TryParse(saved[(saved.LastIndexOf('_') + 1)..], out int skill))
        {
            SystemMessage("Nothing is kept in that slot.", SystemMessageKind.Prompt);
            return;
        }
        ArmState(charm.Item).CharmSkill = skill;
        RefreshArmItem(charm.View, charm.Index, "(fight soul skill restored)");
        SendSavedSkills();
        ArmUpgradeDone($"restores fight soul skill {skill} from slot {slot}");
    }

    public void CleanCharmSkill(int slot)
    {
        if (!ArmSaveSlot(slot, ArmBook.Rules.SkillSaveSlots)) return;
        Character.ArmSavedSkills.Remove(slot);
        SendSavedSkills();
        ArmUpgradeDone($"clears fight soul skill slot {slot}");
    }

    public void OpenSaveSoul(int row) => SendSavedSouls();

    public void SaveCharmStar(int row, int slot)
    {
        if (!ArmSaveSlot(slot, ArmBook.Rules.SoulSaveSlots) || ArmCharm() is not { } charm) return;
        ArmSavedSoul? star = charm.Item.Upgrade?.CharmStars.GetValueOrDefault(row);
        if (star == null)
        {
            SystemMessage("That star has no attributes to keep.", SystemMessageKind.Prompt);
            return;
        }
        if (!ArmPaySave()) return;
        ArmSavedSoul kept = star.Clone();
        kept.Row = row;
        Character.ArmSavedSouls[slot] = kept;
        SendSavedSouls();
        ArmUpgradeDone($"keeps fight soul star {row} in slot {slot}");
    }

    public void RestoreCharmStar(int row, int slot)
    {
        if (!ArmSaveSlot(slot, ArmBook.Rules.SoulSaveSlots) || ArmCharm() is not { } charm) return;
        if (!Character.ArmSavedSouls.TryGetValue(slot, out ArmSavedSoul? saved))
        {
            SystemMessage("Nothing is kept in that slot.", SystemMessageKind.Prompt);
            return;
        }
        ArmSavedSoul restored = saved.Clone();
        restored.Row = row;
        ArmState(charm.Item).CharmStars[row] = restored;
        RefreshArmItem(charm.View, charm.Index, "(fight soul star restored)");
        SendSavedSouls();
        ArmUpgradeDone($"restores fight soul star {row} from slot {slot}");
    }

    public void CleanCharmStar(int row, int slot)
    {
        if (!ArmSaveSlot(slot, ArmBook.Rules.SoulSaveSlots)) return;
        Character.ArmSavedSouls.Remove(slot);
        SendSavedSouls();
        ArmUpgradeDone($"clears fight soul star slot {slot} (row {row})");
    }

    private void SendSavedSkills()
        => Send(UiPackets.Custom("scShowSaveSkill " + string.Join(';',
            Character.ArmSavedSkills.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}-{UiPackets.Token(pair.Value)}"))), "(kept fight soul skills)");

    private void SendSavedSouls()
    {
        Send(GuildPackets.Clear(ArmSoulSaveTable), "(clear kept fight soul stars)");
        List<string[]> rows = Character.ArmSavedSouls.OrderBy(pair => pair.Key)
            .Select(pair => new[] { pair.Key.ToString(), pair.Value.Row.ToString(), pair.Value.Star, pair.Value.First, pair.Value.Second })
            .ToList();
        foreach (byte[] packet in GuildPackets.Rows(ArmSoulSaveTable, rows)) Send(packet, "(kept fight soul stars)");
    }

    private bool ArmSaveSlot(int slot, int count)
    {
        if (slot >= 0 && slot < count) return true;
        SystemMessage("Choose a slot first.", SystemMessageKind.Prompt);
        return false;
    }

    private bool ArmPaySave()
    {
        ArmUpgradeRules rules = ArmBook.Rules;
        if (rules.SaveCostItem.Length == 0 || rules.SaveCostAmount <= 0) return true;
        var cost = new List<ArmUpgradeAmount> { new() { Item = rules.SaveCostItem, Amount = rules.SaveCostAmount } };
        if (!ArmAfford(cost)) return false;
        ArmPay(cost);
        return true;
    }

    private (int View, int Index, ItemStack Item)? ArmCharm() => ArmWorn(CharmArmType, "Equip the fight soul first.");

    private (int View, int Index, ItemStack Item)? ArmWorn(int armType, string missing)
    {
        foreach (int view in new[] { ViewId.Equipped, ViewId.BagEquip })
        {
            foreach ((int slot, ItemStack item) in Bags[view].OrderBy(pair => pair.Key))
            {
                if (ArmTypeOf(item.Name) == armType) return (view, slot, item);
            }
        }
        SystemMessage(missing, SystemMessageKind.Prompt);
        return null;
    }

    public void AddItemPropPoints(IReadOnlyList<(int Index, int Points)> added)
    {
        if (added.Count == 0) return;
        if (ArmWorn(TotemArmType, "Equip the totem first.") is not { } totem) return;
        ArmUpgradeState state = ArmState(totem.Item);
        int spend = added.Sum(entry => Math.Max(0, entry.Points));
        if (spend > state.PropPoints)
        {
            SystemMessage("You do not have enough attribute points.", SystemMessageKind.Prompt);
            return;
        }
        foreach ((int index, int points) in added)
        {
            ArmUpgradeTotemProp? prop = ArmBook.TotemProp(index);
            if (prop == null || points <= 0) continue;
            int cap = Math.Min(prop.MaxPoint.Count > 0 ? prop.MaxPoint[^1] : prop.Values.Count - 1, prop.Values.Count - 1);
            int now = state.Points.GetValueOrDefault(index);
            int next = Math.Min(cap, now + points);
            if (next <= now) continue;
            state.Points[index] = next;
            state.PropPoints -= next - now;
            state.PointValues[prop.Prop] = prop.Values[next];
        }
        RefreshArmItem(totem.View, totem.Index, "(attribute points added)");
        ArmUpgradeDone($"adds attribute points to {totem.Item.Name}: {string.Join(' ', added.Select(entry => $"{entry.Index}+{entry.Points}"))}");
    }

    public bool OpenArmExchange(string npc)
    {
        List<ArmUpgradeExchange> list = ArmBook.Exchanges(npc);
        if (list.Count == 0) return false;
        CloseArmExchange();
        _armExchange = list;
        _armExchangeOpen = true;
        Send(ViewPackets.Create(ArmExchangeView, list.Count), "(exchange list)");
        for (int i = 0; i < list.Count; i++)
        {
            ArmUpgradeExchange entry = list[i];
            PropertyBag item = ItemProperties(new ItemStack { Name = entry.Item, Photo = Context.Data.PhotoOf(entry.Item), Amount = entry.Amount }, i + 1);
            if (PropertyTable.Has("ExchangeDesc")) item["ExchangeDesc"] = UiPackets.Line(entry.Desc);
            Send(ViewPackets.AddItem(ArmExchangeView, i + 1, item), "(exchange entry)");
        }
        Send(UiPackets.Custom("scExchangeType 0 0"), "(exchange options)");
        return true;
    }

    public void CloseArmExchange()
    {
        if (!_armExchangeOpen) return;
        _armExchangeOpen = false;
        _armExchange = [];
        Send(ViewPackets.Delete(ArmExchangeView), "(close exchange list)");
    }

    public void ArmExchange(int view, int selected)
    {
        if (!_armExchangeOpen || view != ArmExchangeView || selected < 0 || selected >= _armExchange.Count)
        {
            Send(UiPackets.Custom("ExchangeFail"), "(exchange failed)");
            return;
        }
        ArmUpgradeExchange entry = _armExchange[selected];
        if (!ArmAfford(entry.Cost))
        {
            Send(UiPackets.Custom("ExchangeFail"), "(exchange failed)");
            return;
        }
        var bought = new ItemStack { Name = entry.Item, Photo = Context.Data.PhotoOf(entry.Item), Amount = entry.Amount };
        if (!GiveItem(bought, "(exchanged article)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            Send(UiPackets.Custom("ExchangeFail"), "(exchange failed)");
            return;
        }
        ArmPay(entry.Cost);
        Send(UiPackets.Custom("ExchangeOk"), "(exchange done)");
        ArmUpgradeDone($"exchanges for {entry.Amount} x {entry.Item}");
    }
}
