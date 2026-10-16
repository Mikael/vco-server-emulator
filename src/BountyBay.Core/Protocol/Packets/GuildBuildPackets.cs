using System.Text;

namespace BountyBay.Core.Protocol.Packets;

public static class GuildBuildPackets
{
    public const int Neutral = 0;
    public const int AtWar = 1;

    public static byte[] Construction(int state, int level, long exp, IEnumerable<(int Current, int Maximum)> degrees)
    {
        var text = new StringBuilder($"scAskConsBuildInfo {state} {level} {exp}");
        foreach ((int current, int maximum) in degrees.Take(4)) text.Append(' ').Append(current).Append(' ').Append(maximum);
        return UiPackets.Custom(text.ToString());
    }

    public static byte[] Activity(int level, long activity) => UiPackets.Custom($"scConActDetail {level} {activity}");

    public static byte[] LogLine(int total, string line) => UiPackets.Custom($"scConsLogInfo {Math.Max(0, total)} {UiPackets.Token(line)}");
}
