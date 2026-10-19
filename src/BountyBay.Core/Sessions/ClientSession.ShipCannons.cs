using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// Mounted cannons are a count per side (ship equipment view 10, slots 4 and 5). Right-clicking a side takes its cannons
// off into the bag as cannon items; right-clicking cannon items in the bag mounts them again.
public sealed partial class ClientSession
{
    public bool UnmountCannons(int view, int index)
    {
        if (view != SeaPackets.ShipEquipmentView || index is not (SeaPackets.LeftCannonSlot or SeaPackets.RightCannonSlot)) return false;
        EnsureShipState();
        bool left = index == SeaPackets.LeftCannonSlot;
        int count = left ? Character.CannonsLeft : Character.CannonsRight;
        if (count <= 0) return true;
        string name = Character.CannonName.Length > 0 ? Character.CannonName : ShipCannon.Name;
        if (!GiveItem(new ItemStack { Name = name, Photo = ShipCannon.Photo, Amount = count }, "(cannons taken off)"))
        {
            SystemMessage("Your bag is full.");
            return true;
        }
        if (left) SetShipCannons(0, Character.CannonsRight);
        else SetShipCannons(Character.CannonsLeft, 0);
        Send(UiPackets.Custom("PlaySound " + ItemSound(name)), "(cannon sound)");
        Save();
        Log.Info(Tag, $"{Name} takes {count} x {name} off the {(left ? "port" : "starboard")} side");
        return true;
    }

    private bool MountCannonItem(int index, ItemStack item)
    {
        if (Sea.Data.FindCannon(item.Name) is not { } cannon) return false;
        EnsureShipState();
        if (cannon.Grade > Math.Max(1, UniqueSkillLevel("US_CANNON")))
        {
            SystemMessage($"That cannon needs Cannon Fight Level {cannon.Grade}.");
            return true;
        }
        if (cannon.Grade > Math.Max(1, ShipTier))
        {
            SystemMessage($"That cannon can only be installed on a ship of level {cannon.Grade}.");
            return true;
        }
        int left = Character.CannonsLeft, right = Character.CannonsRight;
        if (cannon.Name != Character.CannonName && left + right > 0)
        {
            SystemMessage("Take the mounted cannons off first: a ship carries one type of cannon.");
            return true;
        }
        int most = FleetCannonsPerSide, mounted = 0;
        while (mounted < item.Amount && (left < most || right < most))
        {
            if (left <= right && left < most) left++;
            else right++;
            mounted++;
        }
        if (mounted == 0)
        {
            SystemMessage("Every gun port of your ship already holds a cannon.");
            return true;
        }
        TakeFromBag(index, mounted, "(cannons mounted)");
        Character.CannonName = cannon.Name;
        SetShipCannons(left, right);
        Send(UiPackets.Custom("PlaySound " + ItemSound(cannon.Name)), "(cannon sound)");
        Save();
        Log.Info(Tag, $"{Name} mounts {mounted} x {cannon.Name}");
        return true;
    }
}
