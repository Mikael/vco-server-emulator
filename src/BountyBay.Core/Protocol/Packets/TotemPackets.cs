using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public static class TotemPackets
{
    public const int OperationView = 91;
    public const int BoxView = 92;
    public const string SilverKey = "IDName00004390";

    public static readonly string[] Modes = ["ShowGrow", "ShowUpLevel", "ShowTotemClean", "ShowTotemSkill", "ShowTotemReset"];

    public static byte[] Mode(int mode) => UiPackets.Custom(Modes[Math.Clamp(mode, 0, Modes.Length - 1)]);

    public static byte[] AppraiseCost(int silver) => UiPackets.Custom($"scGrowTotemNeedItem {SilverKey} {Number(silver)}");

    public static byte[] AdvanceNeeds(IEnumerable<(string Item, int Amount)> needs)
        => UiPackets.Custom(string.Join(' ', needs.Take(5).Select(need => $"{UiPackets.Token(need.Item)} {Number(need.Amount)}").Prepend("scUpTotemNeedItem")));

    public static byte[] DismantleReturn(string item, int amount) => UiPackets.Custom($"scCleanItemList {UiPackets.Token(item)} {Number(amount)}");

    public static byte[] Appraised() => UiPackets.Custom("scGrowRateScuss");

    public static byte[] Advanced() => UiPackets.Custom("scAddQLevelScuss");

    public static byte[] CombatAttributes(IReadOnlyList<int> values) => UiPackets.Custom("scTotemDamage " + Numbers(values, 6));

    public static byte[] RemodelAttributes(IReadOnlyList<int> values) => UiPackets.Custom("scTotemDamage2 " + Numbers(values, 6));

    public static byte[] RemodelResult(IReadOnlyList<int> inherit, int growRate, IReadOnlyList<int> attributes)
        => UiPackets.Custom($"scResetTotemValue {Numbers(inherit, 5)} {Number(growRate)} {Numbers(attributes, 6)}");

    public static byte[] RemodelSaved() => UiPackets.Custom("scTotemSaveSuccess");

    public static byte[] RemodelCancelled() => UiPackets.Custom("scTotemCanncelSuccess");

    public static byte[] EnableSkill(string key, bool enabled) => UiPackets.Custom($"scEnableTotemSkill {UiPackets.Token(key)} {(enabled ? 1 : 0)}");

    public static byte[] CloseTotemSystem() => UiPackets.Custom("CloseTotemSystem");

    private static string Number(int value) => Math.Max(0, value).ToString(CultureInfo.InvariantCulture);

    private static string Numbers(IReadOnlyList<int> values, int count)
        => string.Join(' ', Enumerable.Range(0, count).Select(i => Number(i < values.Count ? values[i] : 0)));
}
