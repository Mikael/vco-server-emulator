using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands.Commands;

public sealed class ShipServiceCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "BuySailor", "FireSailor", "Repair", "RepairShip", ClientSession.RenameShipCommand, "SailDye", "ShipDye",
        "BuyShip", "ChangeShip", "QueryRebuildShip", "RebuildShip",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "BuySailor" or "FireSailor":
                if (words.Length != 2 || !int.TryParse(words[1], out int count) || count <= 0) return false;
                if (words[0] == "BuySailor") session.HireShipSailors(count);
                else session.DismissShipSailors(count);
                return true;
            case "RepairShip":
                session.RepairShipHull();
                return true;
            case "Repair":
                if (words.Length == 1 || (Arguments.Numbers(words, 3, out int[] repair) && repair[1] is ViewId.Ship or 10))
                    session.AskShipRepair();
                else
                    session.SystemMessage("That item does not need repairing.");
                return true;
            case ClientSession.RenameShipCommand:
                if (words.Length < 2) return false;
                return session.RenameShip(string.Join(' ', words.Skip(1)));
            case "SailDye":
                if (words.Length < 2) return false;
                if (!session.DyeSail(string.Join(' ', words.Skip(1))))
                    session.SystemMessage("The shipwright has no such sail cloth.");
                return true;
            case "ShipDye":
                session.SystemMessage("The shipyard cannot repaint this hull.");
                return true;
            case "BuyShip":
                if (words.Length < 3 || !int.TryParse(words[1], out _) || !int.TryParse(words[2], out _)) return false;
                session.SystemMessage("The shipyard has no hulls for sale at the moment.");
                return true;
            case "ChangeShip":
                if (!Arguments.Numbers(words, 1, out _)) return false;
                session.SystemMessage("You have no other ship in the dock.");
                return true;
            case "QueryRebuildShip" or "RebuildShip":
                if (!Arguments.Numbers(words, 2, out _)) return false;
                session.SystemMessage("The shipyard cannot rebuild ships yet.");
                return true;
            default:
                return false;
        }
    }
}
