namespace BountyBay.Core.Protocol.Packets;

public static class RankArmPackets
{
    public const int UpgradeView = 74;
    public const int WornView = 75;
    public const int TemperView = 77;
    public const int WornSlots = 7;

    public static byte[] PropList(IReadOnlyCollection<(string Prop, int Value)> totals)
        => UiPackets.Custom($"scRankArmPropList {totals.Count} " + (totals.Count == 0 ? "-" : string.Concat(totals.Select(total => $"{total.Prop}-{total.Value};"))));

    public static byte[] Slots(IEnumerable<(int Slot, string Prop, int Value, int Level, int MaxLevel)> slots)
        => UiPackets.Custom("scShowWarkProp" + string.Concat(slots.Select(slot => $" {slot.Slot} {slot.Prop} {slot.Value} {slot.Level} {slot.MaxLevel}")));

    public static byte[] SlotLevel(int slot, int level, int maxLevel, int value)
        => UiPackets.Custom($"scShowUpWake {slot} {level} {maxLevel} {value}");

    public static byte[] Saves(IEnumerable<(int Slot, string Prop, int Value, int Level)> saves)
        => UiPackets.Custom("scShowSaveProp" + string.Concat(saves.Select(save => $" {save.Slot} {save.Level} {save.Prop} {save.Value}")));

    public static byte[] ClearSave(int slot) => UiPackets.Custom($"scShowSaveProp {slot}");

    public static byte[] Stuff(IEnumerable<(string Key, int Have, int Need)> lines)
        => UiPackets.Custom("scShowRankStuff" + string.Concat(lines.Select(line => $" {line.Key} {line.Have} {line.Need}")));

    public static byte[] Fresh(string prop, int value) => UiPackets.Custom($"scShowWakeResult 0 {prop} {value}");

    public static byte[] FreshSplit() => UiPackets.Custom("scShowWakeResult");

    public static byte[] HideFresh() => UiPackets.Custom("scHideWakeResult");

    public static byte[] Essence(int amount) => UiPackets.Custom($"scShowWakeAura {amount}");
}
