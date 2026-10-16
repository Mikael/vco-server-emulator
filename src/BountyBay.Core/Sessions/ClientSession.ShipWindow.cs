using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int ShipStorageSlots = 36;

    private PropertyBag BboShipDetails(PropertyBag details)
    {
        details["CannonHR"] = CombatRules.SeaHitPercent(Sea.Data.Rules, UniqueSkillLevel("US_CANNON"), 0);
        details["CannonDR"] = 0;
        details["CannonDB"] = 0;
        details["RebuildBit"] = 0;
        return SeaPackets.Known(details);
    }

    private PropertyBag ShipWindowProperties() => SeaPackets.Known(new PropertyBag
    {
        ["ShipRotate"] = (float)ActiveShipType.Turning,
        ["BumpPower"] = 0,
        ["NcDDBump"] = 0,
        ["CannonKeyDF"] = 0,
        ["HurtSailor"] = 0f,
        ["SailorProtect"] = 0f,
        ["NcIncShip"] = 0,
        ["NcDDShip"] = 0,
        ["SeaDBInc"] = 0,
        ["SeaKeyDFInc"] = 0,
        ["TorpedoAT"] = 0,
        ["TorpedoDF"] = 0,
        ["CabinCannonL"] = FleetCannonsPerSide,
        ["CabinCannonR"] = FleetCannonsPerSide,
    });

    private void AddShipWindowProperties()
    {
        foreach ((string name, object value) in ShipWindowProperties()) Properties[name] = value;
    }

    private void RefreshShipWindowProperties()
    {
        foreach ((string name, object value) in ShipWindowProperties()) SetProperty(name, value);
    }

    private void SendShipStorageViews()
    {
        Send(ViewPackets.Create(6, ShipStorageSlots), "(ship equipment storage)");
        Send(ViewPackets.Create(7, ShipStorageSlots), "(ship material storage)");
    }
}
