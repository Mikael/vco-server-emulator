using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public static class SeaPackets
{
    public const int AmmunitionView = 9;
    public const int ShipEquipmentView = 10;
    public const int YardEquipmentView = 18;
    public const int DockView = 19;
    public const int YardAmmunitionView = 20;
    public const int YardShipView = 22;
    public const int RebuildView = 28;

    public const int LeftCannonSlot = 4;
    public const int RightCannonSlot = 5;
    public const int CannonArmType = 6;

    public const int ItemTypeEquipment = 10;
    public const int ItemTypeMaterial = 11;
    public const int ItemTypeTool = 12;
    public const int ItemTypeShipEquipment = 13;
    public const int ItemTypeGoods = 14;
    public const int ItemTypeResource = 15;
    public const int ItemTypeAmmunition = 16;
    public const int ItemTypeShip = 17;

    public static PropertyBag Known(PropertyBag bag)
    {
        var known = new PropertyBag();
        foreach ((string name, object value) in bag)
        {
            if (PropertyTable.Has(name)) known[name] = value;
        }
        return known;
    }

    public static byte[] Broadside(int shipId, int targetId, int side, int shellType, int balls, float targetX, float targetZ,
        float scatter, float ballSpeed, int seed)
    {
        float centre = MathF.Max(1f, MathF.Truncate(scatter)) / 4f;
        return ChatPackets.Script(shipId, string.Create(CultureInfo.InvariantCulture,
            $"3Dfd {side} {shellType} {balls} {targetX + centre:F2} 0.00 {targetZ + centre:F2} {MathF.Max(1f, scatter):F2} {MathF.Max(1f, ballSpeed):F2} {seed}"), targetId);
    }

    public static float HitChance(int hits, int balls) => balls <= 0 ? 0f : Math.Clamp((hits + 0.5f) / balls, 0f, 1f);

    public static byte[] Volley(int victimId, int shellType, int balls, float hitFraction, float fromX, float fromZ,
        string? text = null, uint colour = 0xff0000)
    {
        string script = "3DA\n";
        if (!string.IsNullOrEmpty(text)) script += $"info {text}\ncolo {colour & 0xffffff:x6}\n";
        script += string.Create(CultureInfo.InvariantCulture,
            $"weff cannon\ntype {Math.Clamp(shellType, 1, 7)}\nammonum {balls}\nchance {Math.Clamp(hitFraction, 0f, 1f):F3}\nx {fromX:F2}\ny 0.00\nz {fromZ:F2}\n");
        return ChatPackets.Script(victimId, script);
    }

    public static byte[] CannonReload(int side, int milliseconds)
        => ChatPackets.Script(0, $"CannonReload {(side == 0 ? "L" : "R")} {milliseconds}");

    public static byte[] RepairShipInfo(string text) => ChatPackets.Script(0, "RepairShipInfo " + OneWord(text));

    public static byte[] SailorPrice(int price) => ChatPackets.Script(0, $"SailorPrice {price}");

    public static byte[] ShowFireSailor(int sailors) => ChatPackets.Script(0, $"ShowFireSailor {sailors}");

    public static byte[] ShowRenameShip(string command, string prompt)
        => ChatPackets.Script(0, $"ShowRenameShip {command} {OneWord(prompt)}");

    public static byte[] ShowShipDye() => ChatPackets.Script(0, "ShowShipDye");

    public static string OneWord(string text) => UiPackets.Token(text);
}
