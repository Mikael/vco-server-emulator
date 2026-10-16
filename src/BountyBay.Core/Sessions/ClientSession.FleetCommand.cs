using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public void AppointFleetCaptain()
    {
        EnsureFleet();
        List<OwnedShip> fleet = Character.Fleet!;
        Log.Info(Tag, $"{Name} asks to appoint fleet captains ({fleet.Count} ship(s))");
        if (fleet.Count <= 1)
        {
            SystemMessage("You have no other ships in your fleet to appoint a captain to.", SystemMessageKind.Prompt);
            return;
        }
        IEnumerable<string> others = fleet.Where((_, index) => index != Character.ActiveShip)
            .Select(ship => ship.Name.Length > 0 ? ship.Name : ShipTypeOf(ship.Model).Label);
        SystemMessage($"You command the {ShipName}. Your other ships wait in port: {string.Join(", ", others)}. Visit the dock to change your flagship.");
    }
}
