namespace BountyBay.Core.Protocol.Packets;

public static class CharacterPackets
{
    public const int TitleTable = 67;
    public const int OutfitCount = 5;
    public const int OutfitPositions = 14;

    public static string Cell(string? text)
    {
        string cleaned = (text ?? "").Replace("\r", "").Replace('\n', ' ').Replace(UiPackets.FieldSeparator, ' ').Trim();
        return cleaned.Length == 0 ? "-" : cleaned;
    }

    public static byte[] ClearCustomTable(int table) => UiPackets.Custom($"ct {table}");

    public static byte[] CustomTableRows(int table, IEnumerable<string> cells)
        => UiPackets.Custom($"ut {table}" + string.Concat(cells.Select(cell => UiPackets.FieldSeparator + Cell(cell))));

    public static byte[] AddOutfitPiece(int suit, int position, string itemId) => UiPackets.Custom($"AddOneKeySuit {suit} {position} {itemId}");

    public static byte[] DeleteOutfitPiece(int suit, int position) => UiPackets.Custom($"DelOneKeySuit {suit} {position}");
}
