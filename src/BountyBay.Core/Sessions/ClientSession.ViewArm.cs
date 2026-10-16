using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int ViewArmRoleView = 34;
    private const int ViewArmShipView = 35;
    private const int ViewArmRoleSlots = 12;
    private const int ViewArmShipSlots = 10;
    private const int ViewArmShipItem = 10;
    private const int ViewArmFemaleBodies = 3402;

    private static readonly string[] ViewArmLookProperties =
        ["Name", "Level", "LandModel", "BodyType", "TexColor", "Hat", "CapColor", "LeftHand", "RightHand"];

    private ClientSession? _viewArmTarget;
    private bool _viewArmOpen;

    public void QueryArms()
    {
        ClientSession? target = Context.World.Sessions.FirstOrDefault(other => other.ObjectId == ExchangeTargetId && other.InWorld && other != this);
        if (target == null || target.Scene != Scene)
        {
            SystemMessage("Select a player nearby first.", SystemMessageKind.Prompt);
            return;
        }
        _viewArmTarget = target;
        if (_viewArmOpen)
        {
            Send(ViewPackets.Delete(ViewArmRoleView), "(equipment view refresh)");
            Send(ViewPackets.Delete(ViewArmShipView), "(ship equipment view refresh)");
        }
        _viewArmOpen = true;
        Send(ViewPackets.Create(ViewArmShipView, ViewArmShipSlots), "(other ship equipment)");
        foreach (int slot in new[] { SeaPackets.LeftCannonSlot, SeaPackets.RightCannonSlot })
        {
            if (target.ViewArmCannon(slot) is { } cannon) Send(ViewPackets.AddItem(ViewArmShipView, slot, cannon), "(other cannons)");
        }
        PropertyBag ship = target.ShipDetails();
        ship["Index"] = ViewArmShipItem;
        Send(ViewPackets.AddItem(ViewArmShipView, ViewArmShipItem, SeaPackets.Known(ship)), "(other ship)");
        Send(ViewPackets.Create(ViewArmRoleView, ViewArmRoleSlots), "(other equipment)");
        var look = new PropertyBag();
        foreach (string name in ViewArmLookProperties)
        {
            if (target.Properties.TryGetValue(name, out object? value) && PropertyTable.Has(name)) look[name] = value;
        }
        if (look.Count > 0) Send(ViewPackets.Properties(ViewArmRoleView, look), "(other look)");
        foreach ((int slot, ItemStack item) in target.Bags[ViewId.Equipped].OrderBy(pair => pair.Key))
        {
            if (slot < 1 || slot > ViewArmRoleSlots) continue;
            Send(ViewPackets.AddItem(ViewArmRoleView, slot, ItemProperties(item, slot)), $"(other equipment {item.Name})");
        }
        Log.Debug(Tag, $"{Name} looks at the equipment of {target.Name}");
    }

    public void ViewOtherArm(int view, int slot)
    {
        if (view != ViewArmRoleView || _viewArmTarget is not { } target || !target.Bags[ViewId.Equipped].TryGetValue(slot, out ItemStack? item))
            return;
        string label = Context.Data.Items.Goods.TryGetValue(item.Name, out GoodsDefinition? goods) && goods.Label.Length > 0 ? goods.Label : item.Name;
        SystemMessage($"{target.Name}: {label}, level {ItemCatalog.Number(item.Name, "Level")}.");
    }

    public void QueryWearInfo(int view, int index)
    {
        string? item = ViewArmItemAt(view, index);
        if (item == null)
        {
            Log.Debug(Tag, $"{Name} try-on of unknown article {view}/{index}");
            return;
        }
        string part = ItemCatalog.Text(item, "WearProp");
        bool female = Character.Body / 1000 == ViewArmFemaleBodies;
        int model = ItemCatalog.Number(item, female ? "WearModelF" : "WearModel");
        if (part.Length == 0 || model == 0)
        {
            SystemMessage("This article cannot be tried on.", SystemMessageKind.Prompt);
            return;
        }
        string text = $"scWearInfo ArmType,{ItemCatalog.Number(item, "ArmType")},{part},{model}";
        string colour = part switch { "BodyType" => "TexColor", "Hat" => "CapColor", _ => "" };
        if (colour.Length > 0) text += $",{colour},{ItemCatalog.Number(item, "WearColor")}";
        Send(ChatPackets.Script(0, text), "(try on)");
    }

    private string? ViewArmItemAt(int view, int index)
    {
        if (view == ViewArmRoleView)
            return _viewArmTarget?.Bags[ViewId.Equipped].GetValueOrDefault(index)?.Name;
        if (view == ViewId.Booth)
            return _browsing != null && _browsing._stall.TryGetValue(index, out StallEntry? entry) ? entry.Name : null;
        if (view == BoothSearchView)
            return index >= 1 && index <= _boothSearchRows.Count && _boothSearchRows[index - 1].Seller._stall.TryGetValue(_boothSearchRows[index - 1].Slot, out StallEntry? found)
                ? found.Name
                : null;
        if (MailBagOf(view) is { } bag) return bag.GetValueOrDefault(index)?.Name;
        return Bags.TryGetValue(view, out Dictionary<int, ItemStack>? slots) ? slots.GetValueOrDefault(index)?.Name : null;
    }

    private PropertyBag? ViewArmCannon(int slot)
    {
        int count = slot == SeaPackets.LeftCannonSlot ? Character.CannonsLeft : Character.CannonsRight;
        if (count <= 0) return null;
        CannonDefinition cannon = ShipCannon;
        var item = new PropertyBag
        {
            ["Name"] = cannon.Name,
            ["Index"] = slot,
            ["Amount"] = count,
            ["Photo"] = cannon.Photo,
            ["Kind"] = 0,
            ["ItemType"] = SeaPackets.ItemTypeShipEquipment,
            ["ShipArmType"] = SeaPackets.CannonArmType,
        };
        return SeaPackets.Known(ItemCatalog.Extend(item, cannon.Name));
    }
}
