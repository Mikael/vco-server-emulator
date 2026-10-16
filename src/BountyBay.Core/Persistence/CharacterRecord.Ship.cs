namespace BountyBay.Core.Persistence;

public sealed partial class CharacterRecord
{
    public string ShipName { get; set; } = "";
    public int ShipHull { get; set; } = -1;
    public int ShipSail { get; set; } = -1;
    public int SailorsWounded { get; set; }
    public int SailCloth { get; set; } = 2900000;
    public int CannonsLeft { get; set; } = -1;
    public int CannonsRight { get; set; } = -1;
    public string CannonName { get; set; } = "";
    public string AmmunitionLeft { get; set; } = "";
    public string AmmunitionRight { get; set; } = "";
    public Dictionary<string, int>? Ammunition { get; set; }
    public int ShipsSunk { get; set; }
}
