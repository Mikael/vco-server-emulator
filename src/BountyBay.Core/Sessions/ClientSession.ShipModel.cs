using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int ShipModelMergeOption = 500;
    public const int ShipModelInnerOption = 501;

    private const string ShipModelSuffix = " Model";
    private const string ShipModelDrawing = "Drawing";
    private const int ShipModelInnerFee = 1000;

    private static readonly (string Property, int Least, int Most)[] ShipModelGains =
    [
        ("S/MaxHP", 5, 20),
        ("S/MaxSailHP", 5, 15),
        ("NumSailorMax", 1, 3),
    ];

    private readonly Dictionary<int, ItemStack> _shipModelSlots = [];
    private readonly HashSet<int> _shipModelInnerShown = [];
    private List<(string Name, int Model, int Photo)>? _shipModelChoices;
    private bool _shipModelMergeOpen;
    private bool _shipModelInnerOpen;

    public bool HandleShipModelOption(int target, int option)
    {
        switch (option)
        {
            case ShipModelMergeOption:
                OpenShipModelMerge();
                return true;
            case ShipModelInnerOption:
                OpenShipModelInner();
                return true;
            default:
                return false;
        }
    }

    public void OpenShipModelMerge()
    {
        if (_shipModelMergeOpen) CloseShipModelMerge();
        _shipModelMergeOpen = true;
        _shipModelChoices = null;
        Send(ViewPackets.Create(ShipModelPackets.MergeView, ShipModelPackets.MergeMainSlot), "(ship model merge)");
        Log.Debug(Tag, $"{Name} opens the ship model merge");
    }

    public bool ShipModelSelect(int view, int how, int index)
    {
        if (view != ShipModelPackets.MergeView) return false;
        if (!_shipModelMergeOpen || index is < 1 or > ShipModelPackets.MergeMainSlot) return true;
        if (Hand != null && HandSource() is { } from)
        {
            DropHand();
            ItemStack held = MailBagOf(from.View)![from.Index];
            if (!IsShipModelItem(held.Name))
            {
                SystemMessage("Only ship models can be merged.", SystemMessageKind.Prompt);
                return true;
            }
            if (index != ShipModelPackets.MergeMainSlot && !_shipModelSlots.ContainsKey(index)
                && _shipModelSlots.Keys.Count(slot => slot != ShipModelPackets.MergeMainSlot) >= ShipModelPackets.MostVice)
            {
                SystemMessage("Not more than 3 vice Ship Models at one time.", SystemMessageKind.Prompt);
                return true;
            }
            ReturnShipModelSlot(index);
            var one = new ItemStack { Name = held.Name, Photo = held.Photo, Amount = 1, Cost = held.Cost };
            TakeFrom(from.View, from.Index, 1, "(ship model placed)");
            _shipModelSlots[index] = one;
            Send(ViewPackets.AddItem(ShipModelPackets.MergeView, index, ShipModelItem(one, index)), "(ship model placed)");
            return true;
        }
        DropHand();
        ReturnShipModelSlot(index);
        return true;
    }

    public void MixShipModels()
    {
        if (!_shipModelMergeOpen) return;
        if (!_shipModelSlots.TryGetValue(ShipModelPackets.MergeMainSlot, out ItemStack? main)
            || _shipModelSlots.Keys.All(slot => slot == ShipModelPackets.MergeMainSlot))
        {
            SystemMessage("Put a main ship model and at least one vice ship model in the window.", SystemMessageKind.Prompt);
            Send(ShipModelPackets.CanShipMerge(), "(merge again)");
            return;
        }
        ShipModelMergeRecord record = ShipModelRecord(main.Name);
        if (record.Times >= ShipModelPackets.MostMerges)
        {
            SystemMessage("This ship model cannot be merged any more.", SystemMessageKind.Prompt);
            Send(ShipModelPackets.CanShipMerge(), "(merge again)");
            return;
        }
        var changes = new List<(string Property, int Before, int Added)>();
        List<int> vices = _shipModelSlots.Keys.Where(slot => slot != ShipModelPackets.MergeMainSlot).Order().ToList();
        foreach ((string property, int least, int most) in ShipModelGains)
        {
            int before = record.Bonus.GetValueOrDefault(property);
            int added = vices.Sum(_ => Random.Shared.Next(least, most + 1));
            record.Bonus[property] = before + added;
            changes.Add((property, before, added));
        }
        record.Times++;
        var choices = new List<(string Name, int Model, int Photo)>();
        foreach (ItemStack stack in new[] { main }.Concat(vices.Select(slot => _shipModelSlots[slot])))
        {
            if (ShipModelType(stack.Name) is not { } type || choices.Any(choice => choice.Model == type.Model)) continue;
            choices.Add((type.Label, type.Model, type.Photo));
        }
        foreach (int slot in vices)
        {
            _shipModelSlots.Remove(slot);
            Send(ViewPackets.RemoveItem(ShipModelPackets.MergeView, slot), "(vice ship model used)");
        }
        _shipModelChoices = choices.Take(ShipModelPackets.MostChoices).ToList();
        Save();
        Send(ShipModelPackets.MergeResult(changes, _shipModelChoices.Select(choice => (choice.Name, choice.Photo)).ToList()), "(merge result)");
        Send(ShipModelPackets.CanShipMerge(), "(merge again)");
        Log.Info(Tag, $"{Name} merges {vices.Count} ship model(s) into {main.Name} ({record.Times}/{ShipModelPackets.MostMerges})");
    }

    public void ChooseShipModel(int choice)
    {
        if (!_shipModelMergeOpen || _shipModelChoices == null || !_shipModelSlots.TryGetValue(ShipModelPackets.MergeMainSlot, out ItemStack? main)) return;
        ShipModelMergeRecord record = ShipModelRecord(main.Name);
        if (choice >= 1 && choice <= _shipModelChoices.Count)
        {
            (string name, int model, _) = _shipModelChoices[choice - 1];
            record.MixType = model;
            record.MixName = name;
            Character.ShipModelCover = main.Name;
            SystemMessage($"Your ship model now carries the shape of the {name}.");
        }
        _shipModelChoices = null;
        Save();
        CloseShipModelMerge();
    }

    public void CloseShipModelMerge()
    {
        if (!_shipModelMergeOpen) return;
        foreach (int slot in _shipModelSlots.Keys.ToList()) ReturnShipModelSlot(slot);
        _shipModelMergeOpen = false;
        _shipModelChoices = null;
        Send(ShipModelPackets.HideShipMerge(), "(merge closed)");
        Send(ViewPackets.Delete(ShipModelPackets.MergeView), "(merge closed)");
        Save();
    }

    public void ChangeShipModel()
    {
        Character.ShowShipModel = Character.ShowShipModel == 0 ? 1 : 0;
        Save();
        Send(ViewPackets.UpdateItem(ViewId.Ship, 1, SeaPackets.Known(new PropertyBag { ["ShowShipModel"] = Character.ShowShipModel })), "(ship model view)");
        if (!AtSea) return;
        int model = ShipModelCoverModel() is { } cover && Character.ShowShipModel == 1 ? cover : ActiveShipModel;
        var look = SeaPackets.Known(new PropertyBag { ["Model"] = model, ["ShipModel"] = model });
        Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, look), "(ship shape)");
        ToOthers(ScenePackets.ObjectProperties(ObjectId, look), $"({Name} ship shape)");
    }

    public void ShipModelDetails(PropertyBag details)
    {
        int bits = 0;
        foreach (int part in Character.ShipModelInnerParts.Keys) bits |= ShipModelPackets.InnerBit(part);
        details["RebuildBit"] = bits;
        if (PropertyTable.Has("ShowShipModel")) details["ShowShipModel"] = Character.ShowShipModel;
    }

    public void OpenShipModelInner()
    {
        _shipModelInnerOpen = true;
        RefreshShipModelInner();
        Send(ShipModelPackets.ShowRebuildView(), "(ship inner rebuild)");
    }

    public void ShowShipModelInner()
    {
        RefreshShipModelInner();
        Log.Debug(Tag, $"{Name} looks inside the ship");
    }

    public void InstallShipModelInner(int number)
    {
        if (!_shipModelInnerOpen)
        {
            SystemMessage("A ship engineer can retrofit your ship.", SystemMessageKind.Prompt);
            return;
        }
        var parts = new List<(int View, int Slot, ItemStack Stack)>();
        foreach (int view in BboBagViews)
        {
            foreach ((int at, ItemStack stack) in MailBagOf(view)!.OrderBy(pair => pair.Key))
            {
                if (ShipModelInnerPart(stack.Name) >= 0) parts.Add((view, at, stack));
            }
        }
        int pick = parts.FindIndex(entry => entry.Slot == number);
        if (pick < 0 && number >= 1 && number <= parts.Count) pick = number - 1;
        if (pick < 0)
        {
            SystemMessage("Choose a ship part to install first.", SystemMessageKind.Prompt);
            return;
        }
        (int View, int Slot, ItemStack Stack) part = parts[pick];
        if (Money < ShipModelInnerFee)
        {
            SystemMessage($"Installing a ship part costs {ShipModelInnerFee} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        int slot = ShipModelInnerPart(part.Stack.Name);
        var installed = new ItemStack { Name = part.Stack.Name, Photo = part.Stack.Photo, Amount = 1, Cost = part.Stack.Cost };
        TakeFrom(part.View, part.Slot, 1, "(ship part installed)");
        if (Character.ShipModelInnerParts.TryGetValue(slot, out ItemStack? old)) GiveItem(old, "(ship part removed)");
        Character.ShipModelInnerParts[slot] = installed;
        SetMoney(Money - ShipModelInnerFee);
        RefreshLoad();
        Save();
        RefreshShipModelInner();
        SystemMessage($"The {ShipModelLabel(installed.Name)} has been installed in your ship.");
        Log.Info(Tag, $"{Name} installs {installed.Name} in inner slot {slot}");
    }

    public void KeepShipModelInner(int slot)
    {
        int part = slot - ShipModelPackets.FirstInnerSlot;
        if (!Character.ShipModelInnerParts.TryGetValue(part, out ItemStack? item))
        {
            SystemMessage("There is no part in this slot.", SystemMessageKind.Prompt);
            return;
        }
        int fee = Math.Max(100, Context.Data.PriceOf(item.Name) / 10);
        if (Money < fee)
        {
            SystemMessage($"The enhancement costs {fee} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        SetMoney(Money - fee);
        Save();
        SystemMessage($"The {ShipModelLabel(item.Name)} has been enhanced for {fee} silver coins.");
    }

    public void SplitShipModelInner(int slot)
    {
        int part = slot - ShipModelPackets.FirstInnerSlot;
        if (!Character.ShipModelInnerParts.Remove(part, out ItemStack? item)) return;
        if (_shipModelInnerShown.Remove(slot)) Send(ViewPackets.RemoveItem(SeaPackets.ShipEquipmentView, slot), "(ship part split)");
        Save();
        RefreshShipModelInner();
        SystemMessage($"The {ShipModelLabel(item.Name)} was split and destroyed.");
    }

    private void RefreshShipModelInner()
    {
        EnsureShipState();
        SendShipViews();
        foreach ((int part, ItemStack item) in Character.ShipModelInnerParts)
        {
            int slot = ShipModelPackets.FirstInnerSlot + part;
            var values = new PropertyBag
            {
                ["Name"] = item.Name,
                ["Index"] = slot,
                ["Amount"] = 1,
                ["Photo"] = item.Photo,
                ["Kind"] = 0,
                ["ItemType"] = SeaPackets.ItemTypeShipEquipment,
                ["ShipArmType"] = ShipModelPackets.FirstInnerArmType + part,
                ["DFG"] = 100,
                ["DFGMax"] = 100,
            };
            ItemCatalog.Extend(values, item.Name);
            values["Index"] = slot;
            if (_shipModelInnerShown.Add(slot)) Send(ViewPackets.AddItem(SeaPackets.ShipEquipmentView, slot, SeaPackets.Known(values)), "(ship part)");
            else Send(ViewPackets.UpdateItem(SeaPackets.ShipEquipmentView, slot, SeaPackets.Known(values)), "(ship part)");
        }
        var details = new PropertyBag();
        ShipModelDetails(details);
        Send(ViewPackets.UpdateItem(ViewId.Ship, 1, SeaPackets.Known(details)), "(ship inner parts)");
    }

    private void ReturnShipModelSlot(int index)
    {
        if (!_shipModelSlots.Remove(index, out ItemStack? item)) return;
        Send(ViewPackets.RemoveItem(ShipModelPackets.MergeView, index), "(ship model taken back)");
        if (!GiveItem(item, "(ship model returned)")) SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
    }

    private PropertyBag ShipModelItem(ItemStack item, int index)
    {
        PropertyBag values = ItemProperties(item, index);
        if (Character.ShipModelMerges.TryGetValue(item.Name, out ShipModelMergeRecord? record))
        {
            values["ModelMixTimes"] = record.Times;
            values["ModelMixType"] = record.MixType;
            values["ModelMixName"] = record.MixName;
        }
        return SeaPackets.Known(values);
    }

    private ShipModelMergeRecord ShipModelRecord(string item)
    {
        if (!Character.ShipModelMerges.TryGetValue(item, out ShipModelMergeRecord? record))
        {
            record = new ShipModelMergeRecord { Item = item };
            Character.ShipModelMerges[item] = record;
        }
        return record;
    }

    private int? ShipModelCoverModel()
        => Character.ShipModelCover.Length > 0 && Character.ShipModelMerges.TryGetValue(Character.ShipModelCover, out ShipModelMergeRecord? record)
            && record.MixType > 0 ? record.MixType : null;

    private string ShipModelLabel(string name)
        => Context.Data.Items.Goods.TryGetValue(name, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : name;

    private bool IsShipModelItem(string name)
    {
        string label = ShipModelLabel(name);
        return label.Contains(ShipModelSuffix.Trim(), StringComparison.Ordinal) && !label.Contains(ShipModelDrawing, StringComparison.Ordinal);
    }

    private ShipDefinition? ShipModelType(string name)
    {
        string label = ShipModelLabel(name).Replace(ShipModelSuffix, "", StringComparison.Ordinal).Trim();
        return Yard.Ships.FirstOrDefault(ship => ship.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
            ?? Yard.Ships.FirstOrDefault(ship => label.Length > 0 && ship.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase));
    }

    private static int ShipModelInnerPart(string name)
    {
        int type = ItemCatalog.Number(name, "ShipArmType");
        return type >= ShipModelPackets.FirstInnerArmType && type < ShipModelPackets.FirstInnerArmType + ShipModelPackets.InnerSlots
            ? type - ShipModelPackets.FirstInnerArmType
            : -1;
    }
}
