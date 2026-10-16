namespace BountyBay.Core.Protocol.Packets;

public static class ShipModelPackets
{
    public const int MergeView = 62;
    public const int MergeMainSlot = 10;
    public const int MergeViceSlots = 9;
    public const int MostVice = 3;
    public const int MostChoices = 4;
    public const int NoChoice = 5;
    public const int MostMerges = 5;
    public const int FirstInnerSlot = 11;
    public const int InnerSlots = 4;
    public const int FirstInnerArmType = 12;

    public static byte[] MergeResult(IReadOnlyList<(string Property, int Before, int Added)> changes, IReadOnlyList<(string Name, int Photo)> choices)
    {
        var words = new List<string> { changes.Count.ToString() };
        words.AddRange(changes.Select(change => $"{change.Property},{change.Before},{change.Added}"));
        words.Add(Math.Min(choices.Count, MostChoices).ToString());
        foreach ((string name, int photo) in choices.Take(MostChoices))
        {
            words.Add(UiPackets.Token(name));
            words.Add(photo.ToString());
        }
        return ChatPackets.Script(0, "scMergeResult " + string.Join(' ', words));
    }

    public static byte[] CanShipMerge() => ChatPackets.Script(0, "scCanShipMerge 0");

    public static byte[] HideShipMerge() => ChatPackets.Script(0, "scHideShipMerge");

    public static byte[] ShowRebuildView() => ChatPackets.Script(0, "ShowRebuldView");

    public static int InnerBit(int part) => 1 << (part + 1);
}
