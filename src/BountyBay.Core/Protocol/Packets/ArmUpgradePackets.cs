namespace BountyBay.Core.Protocol.Packets;

public static class ArmUpgradePackets
{
    public const int EvolSuccess = 2;
    public const int StarSuccess = 1;

    public static byte[] StuffList(IEnumerable<(long Have, long Need)> materials)
        => UiPackets.Custom("scStuffList " + string.Join(' ', materials.Select(pair => $"{pair.Have},{pair.Need}")));

    public static byte[] SetStarInfo(bool extra, int luck, int difficulty)
        => UiPackets.Custom($"scSetStarInfo {(extra ? 1 : 0)} {luck} {difficulty}");

    public static byte[] TakeOffThing(int view, int index) => UiPackets.Custom($"scTakeOffThing {view} {index}");

    public static byte[] EvolStuffList(IEnumerable<(long Have, long Need)> materials, long silverHave, long silverNeed)
        => UiPackets.Custom("scEvolStuffList "
            + string.Join(' ', materials.Select(pair => $"{pair.Have},{pair.Need}").Append($"{silverHave},{silverNeed}")));

    public static byte[] EvolInfo(string property, int value) => UiPackets.Custom($"scEvolInfo {UiPackets.Token(property)} {value}");

    public static byte[] SucMake(int kind) => UiPackets.Custom($"csSucMake {kind}");

    public static byte[] SuitSystem(IReadOnlyCollection<string> names)
        => UiPackets.Custom($"SuitSystem {names.Count}" + string.Concat(names.Select(name => " " + UiPackets.Token(name))));

    public static byte[] InitAssembly(IEnumerable<(string Name, bool Flag)> pieces, IEnumerable<int> chances)
    {
        string list = string.Join(',', pieces.Select(piece => $"{UiPackets.Token(piece.Name)},{(piece.Flag ? 1 : 0)}"));
        string tiers = string.Join(',', chances);
        return UiPackets.Custom($"InitAssembly {(list.Length > 0 ? list : "-")} {(tiers.Length > 0 ? tiers : "0")}");
    }

    public static byte[] UpdateAmount(IEnumerable<(long Have, long Need)> materials)
        => UiPackets.Custom("UpdateAmount " + string.Join(' ', materials.Select(pair => $"{pair.Have} {pair.Need}")));

    public static byte[] OpenNewUpdate(int mode) => UiPackets.Custom($"scOpenNewUpdate {mode}");

    public static byte[] NewStuffAmount(IEnumerable<(long Have, long Need)> materials, long silverHave, long silverNeed)
        => UiPackets.Custom("scNewStuffAmount "
            + string.Join(' ', materials.Select(pair => $"{pair.Have} {pair.Need}").Append($"{silverHave} {silverNeed}")));

    public static byte[] CloseSuitView() => UiPackets.Custom("scCloseSuitView 2");

    public static byte[] ClearNewArmUpdate() => UiPackets.Custom("scClearNewArmUpdate");

    public static byte[] ShowRankArmUp(long silver, IEnumerable<(string Item, int Amount)> materials)
        => UiPackets.Custom($"scShowRankArmUp {silver}" + string.Concat(materials.Select(pair => $" {UiPackets.Token(pair.Item)} {pair.Amount}")));

    public static byte[] ShowRankStuff(IEnumerable<(string Item, long Have, long Need)> materials)
        => UiPackets.Custom("scShowRankStuff" + string.Concat(materials.Select(entry => $" {UiPackets.Token(entry.Item)} {entry.Have} {entry.Need}")));
}
