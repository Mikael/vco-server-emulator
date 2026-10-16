using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class FleetCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "BuyShip", "ChangeShip", "QueryRebuildShip", "RebuildShip", "RebuildStatuary", "ShipDye", "SailDye", "Repair", "NextAmmo",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "BuyShip":
                if (words.Length < 3 || !int.TryParse(words[1], out int ship) || !int.TryParse(words[2], out int flagship)) return false;
                session.BuyShip(ship, flagship != 0, string.Join(' ', words.Skip(3)));
                return true;
            case "ChangeShip":
                if (!Arguments.Numbers(words, 1, out int[] change)) return false;
                session.ChangeShip(change[0]);
                return true;
            case "QueryRebuildShip":
                if (!Arguments.Numbers(words, 2, out int[] query)) return false;
                session.QueryRebuildShip(query[1]);
                return true;
            case "RebuildShip":
                if (!Arguments.Numbers(words, 2, out int[] rebuild)) return false;
                session.RebuildShip(rebuild[1]);
                return true;
            case "RebuildStatuary":
                if (!Arguments.Numbers(words, 1, out _)) return false;
                session.SystemMessage("The shipwright has no figureheads to fit.");
                return true;
            case "ShipDye" or "SailDye":
                if (words.Length < 2) return false;
                session.FleetDye(words[0] == "SailDye", string.Join(' ', words.Skip(1)));
                return true;
            case "Repair":
                if (words.Length == 1)
                {
                    session.AskShipRepair();
                    return true;
                }
                if (!Arguments.Numbers(words, 3, out int[] repair)) return false;
                session.RepairItem(repair[0], repair[1], repair[2]);
                return true;
            case "NextAmmo":
                if (words.Length != 3 || words[1] is not ("L" or "R") || !int.TryParse(words[2], out int ammunition)) return false;
                session.SelectAmmunitionSide(words[1] == "L", ammunition);
                return true;
            default:
                return false;
        }
    }
}
