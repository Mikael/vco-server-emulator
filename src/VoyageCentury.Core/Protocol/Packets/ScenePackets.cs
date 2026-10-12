using VoyageCentury.Core.Network;

namespace VoyageCentury.Core.Protocol.Packets;

public static class ScenePackets
{
    public static byte[] EnterScene(int scene, int playerId = ProtocolConstants.SelfObjectId)
        => new PacketWriter(ServerMsg.EnterScene)
            .WriteUInt32((uint)playerId).WriteUInt32(0).WriteUInt32(0).WriteUInt32((uint)scene)
            .ToArray();

    public static byte[] LeaveScene() => new PacketWriter(ServerMsg.LeaveScene).ToArray();

    public static byte[] SceneState(int hour, int weather, float windLevel = 1f, float windOrient = 0f, float streamOrient = 0f)
        => PropertyTable.WriteValues(new PacketWriter(ServerMsg.SceneState), new PropertyBag
        {
            ["Hour"] = hour,
            ["WeatherType"] = weather,
            ["WindLevel"] = windLevel,
            ["WindOrient"] = windOrient,
            ["StreamOrient"] = streamOrient,
        }).ToArray();

    public static byte[] CreateObject(int objectId, ObjectKind kind, int model, int serial = 0)
        => new PacketWriter(ServerMsg.CreateObject)
            .WriteUInt32((uint)objectId).WriteUInt32((uint)serial).WriteByte((byte)kind).WriteUInt32((uint)model)
            .ToArray();

    public static byte[] RemoveObject(int objectId, int serial = 0)
        => new PacketWriter(ServerMsg.RemoveObject).WriteUInt32((uint)objectId).WriteUInt32((uint)serial).ToArray();

    public static byte[] ObjectProperties(int objectId, IReadOnlyDictionary<string, object> values)
        => PropertyTable.WriteValues(new PacketWriter(ServerMsg.ObjectProperties).WriteUInt32((uint)objectId), values).ToArray();

    public static byte[] ShowObject(int objectId, bool visible = true)
        => new PacketWriter(ServerMsg.ShowObject).WriteUInt32((uint)objectId).WriteByte(visible ? (byte)1 : (byte)0).ToArray();

    public static byte[] PlaceObject(int objectId, float x, float y, float z, float? destX = null, float? destZ = null,
        float speed = 0f, int action = 0, int orient = 0)
        => new PacketWriter(ServerMsg.PlaceObject)
            .WriteUInt32((uint)objectId)
            .WriteSingle(x).WriteSingle(y).WriteSingle(z)
            .WriteUInt16((ushort)orient)
            .WriteSingle(destX ?? x).WriteSingle(destZ ?? z)
            .WriteUInt16((ushort)Math.Clamp((int)(speed * 100), 0, 65535))
            .WriteUInt16(0)
            .WriteInt16(0)
            .WriteUInt16((ushort)action)
            .ToArray();
}
