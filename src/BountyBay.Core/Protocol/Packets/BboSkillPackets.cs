using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public static class BboSkillPackets
{
    public const string WoundEffect = "wound";
    public const string WardEffect = "ward";
    public const string DodgeEffect = "dodge";

    public static byte[] SkillState(string stunt, bool enabled) => ChatPackets.Script(0, $"scEnableSoulSkill {stunt} {(enabled ? 1 : 0)}");

    public static byte[] UniqueSkillReload(string stunt, int milliseconds)
        => ChatPackets.Script(0, $"UniqueSkillReload {stunt} {Math.Max(0, milliseconds)}");

    public static byte[] ToolCooling(int coolingKind, int milliseconds)
        => ChatPackets.Script(0, $"ToolCooling {coolingKind} {Math.Max(0, milliseconds)}");

    public static byte[] Action(int objectId, int action, bool attack = false) => ScenePackets.PlayAction(objectId, action, attack);

    public static byte[] TimedAction(int objectId, int action, int milliseconds) => ScenePackets.TimedAction(objectId, action, milliseconds);

    public static byte[] Hit(int objectId, string text, uint colour, string reaction, string effectId, float x, float y, float z)
    {
        string script = $"3DA\ninfo {text}\ncolo {colour & 0xffffff:x6}\nweff {reaction}\n" + Position(x, y, z);
        if (effectId.Length > 0) script += $"effid {effectId}\n";
        return ChatPackets.Script(objectId, script);
    }

    public static byte[] Effect(int objectId, string effect, float x = 0, float y = 0, float z = 0)
        => ChatPackets.Script(objectId, $"3DA\nweff {effect}\n" + Position(x, y, z));

    public static byte[] Text(int objectId, string text, uint colour)
        => ChatPackets.Script(objectId, $"3DA\ninfo {text}\ncolo {colour & 0xffffff:x6}\n");

    private static string Position(float x, float y, float z)
        => string.Create(CultureInfo.InvariantCulture, $"x {x:F2}\ny {y:F2}\nz {z:F2}\n");
}
