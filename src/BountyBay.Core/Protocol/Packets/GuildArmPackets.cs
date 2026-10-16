using System.Text;

namespace BountyBay.Core.Protocol.Packets;

public static class GuildArmPackets
{
    public const int ComposeView = 64;
    public const int MedalView = 100;
    public const int MaxStuff = 4;

    public static byte[] Stuff(IEnumerable<(string Item, int Owned, int Needed)> stuff)
    {
        var text = new StringBuilder("scShowConsArmStuff");
        foreach ((string item, int owned, int needed) in stuff.Take(MaxStuff))
            text.Append(' ').Append(UiPackets.Token(item)).Append(' ').Append(owned).Append(' ').Append(needed);
        return UiPackets.Custom(text.ToString());
    }
}
