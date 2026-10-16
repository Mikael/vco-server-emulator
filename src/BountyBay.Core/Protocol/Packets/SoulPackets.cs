namespace BountyBay.Core.Protocol.Packets;

public static class SoulPackets
{
    public const string Empty = "0";

    public static byte[] ViewArm(IReadOnlyList<string> slots, int chart, int grade, int skillLevel)
        => UiPackets.Custom($"scViewArmFightSoul {string.Join(' ', slots.Select(Field))} {chart + 1} {grade} {skillLevel}");

    public static byte[] PropertyList(IReadOnlyList<string> rows)
        => UiPackets.Custom($"scFightSoulPropList {rows.Count} {List(rows)}");

    public static byte[] ViewSoul(string id, int level, int quality, int exp, int moneyRow, int expMax, IReadOnlyList<string> rows)
        => UiPackets.Custom($"scViewFightSoul {Field(id)} {level} {quality} {exp} {moneyRow} {expMax} {rows.Count} {List(rows)}");

    public static byte[] StartFreshChart(int grade, int chart, int skillLevel)
        => UiPackets.Custom($"scStartFreshStartMap {grade} {chart + 1} {skillLevel}");

    public static byte[] GiveUpFreshChart() => UiPackets.Custom("scGiveupFreshMap");

    public static byte[] SaveSkill(int chart, int grade, int skillLevel)
        => UiPackets.Custom($"scFreshSaveSkill {chart + 1} {grade} {skillLevel}");

    public static byte[] ViewChange(string id, int quality, int level, IReadOnlyList<string> rows, string materials)
        => UiPackets.Custom($"scViewChangeFightSoul {Field(id)} {quality} {level} {List(rows)} {Field(materials)}");

    public static byte[] ChangeResult(IReadOnlyList<string> rows) => UiPackets.Custom($"scViewCFSResult {List(rows)}");

    public static byte[] CancelChange() => UiPackets.Custom("scCancleChangeFightSoul");

    public static byte[] CloseChange() => UiPackets.Custom("scCloseChangeFightSoul");

    public static byte[] CloseChangeList() => UiPackets.Custom("scCloseCFightSoulList");

    public static byte[] ViewUpQuality(string id, string next, int quality, int nextQuality, int level, int nextLevel,
        IReadOnlyList<string> rows, IReadOnlyList<string> nextRows, string materials)
        => UiPackets.Custom($"scViewFSUpQuality {Field(id)} {Field(next)} {quality} {nextQuality} {level} {nextLevel} "
            + $"{List(rows)} {List(nextRows)} {Field(materials)}");

    public static byte[] DownInfo(IReadOnlyList<(string Item, int Amount)> yields)
        => UiPackets.Custom("scDownInfo" + string.Concat(yields.Where(entry => entry.Amount > 0).Select(entry => $" {Field(entry.Item)} {entry.Amount}")));

    public static byte[] Effect(int number) => UiPackets.Custom($"scEffect{number}");

    public static byte[] EnableSkill(string skill, bool enabled) => UiPackets.Custom($"scEnableSoulSkill {Field(skill)} {(enabled ? 1 : 0)}");

    public static string Materials(IReadOnlyList<(string Item, int Amount)> items, long money)
        => (items.Count == 0 ? Empty : string.Join(';', items.Take(3).Select(entry => $"{entry.Item}-{entry.Amount}"))) + $",{money}";

    private static string List(IReadOnlyList<string> rows) => rows.Count == 0 ? Empty : string.Join(';', rows.Select(Field));

    private static string Field(string text) => string.IsNullOrWhiteSpace(text) ? Empty : UiPackets.Token(text);
}
