namespace VoyageCentury.Core.Persistence;

public sealed class OwnedShip
{
    public int Model { get; set; } = 2011001;
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Color { get; set; }
    public int Hull { get; set; } = -1;
    public int SailCloth { get; set; } = 2900000;
    public int CannonsLeft { get; set; }
    public int CannonsRight { get; set; }
    public string CannonName { get; set; } = "";
    public string Port { get; set; } = "";
}

public sealed partial class CharacterRecord
{
    public List<OwnedShip>? Fleet { get; set; }
    public int ActiveShip { get; set; }
    public Dictionary<string, int>? Wear { get; set; }
}
