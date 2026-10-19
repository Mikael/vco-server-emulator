using BountyBay.Core.Network;

namespace BountyBay.Core.Protocol.Packets;

public static class ScenePackets
{
    public static byte[] EnterScene(int scene, int playerId = ProtocolConstants.SelfObjectId)
        => new PacketWriter(ServerMsg.EnterScene)
            .WriteUInt32(ProtocolConstants.DynamicKey).WriteUInt32((uint)playerId).WriteUInt32(0).WriteUInt32((uint)scene)
            .ToArray();

    public static byte[] LeaveScene() => new PacketWriter(ServerMsg.LeaveScene).ToArray();

    public static byte[] SceneState(Data.GameTime time, int weather, string cityName = "", string sceneName = "", float windLevel = 1f, float windOrient = 0f, float streamOrient = 0f, int scene = 0, int sceneType = -1, int iconSwitch = 0)
    {
        var state = new PropertyBag
        {
            ["Hour"] = time.Hour,
            ["WeatherType"] = weather,
            ["WindLevel"] = windLevel,
            ["WindOrient"] = windOrient,
            ["StreamOrient"] = streamOrient,
            ["Year"] = time.Year,
            ["Month"] = time.Month,
            ["Day"] = time.Day,
        };
        if (cityName.Length > 0 && PropertyTable.Has("CityName")) state["CityName"] = cityName;
        if (PropertyTable.Has("SceneKind")) state["SceneKind"] = 0;
        if (sceneType >= 0 && PropertyTable.Has("Type")) state["Type"] = sceneType;
        if (sceneName.Length > 0) state["Name"] = sceneName;
        if (scene > 0 && PropertyTable.Has("SceneSerial")) state["SceneSerial"] = $"L{scene}";
        // InterFace_Main shows the instance info buttons from this: 1 sea instance, 2 land tower, 4 sea tower.
        if (PropertyTable.Has("IconSwitchMsg")) state["IconSwitchMsg"] = iconSwitch;
        return PropertyTable.WriteValues(new PacketWriter(ServerMsg.SceneState), state).ToArray();
    }

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

    public static byte[] PlayAction(int objectId, int action, bool attack = false)
        => new PacketWriter(attack ? ServerMsg.AttackAction : ServerMsg.PlayAction)
            .WriteUInt32((uint)objectId).WriteUInt16((ushort)action).WriteZeros(8)
            .ToArray();

    public static byte[] TimedAction(int objectId, int action, int milliseconds)
        => new PacketWriter(ServerMsg.TimedAction)
            .WriteUInt32((uint)objectId).WriteUInt16((ushort)action).WriteUInt32(0).WriteUInt32((uint)milliseconds)
            .ToArray();

    public static byte[] SailState(int objectId, int state)
        => new PacketWriter(ServerMsg.SailState).WriteUInt32((uint)objectId).WriteByte((byte)(state + 2)).ToArray();

    public static byte[] SetObjectPosition(int objectId, float x, float y, float z, int heading)
        => new PacketWriter(ServerMsg.SetObjectPosition)
            .WriteByte(1)
            .WriteUInt32((uint)objectId)
            .WriteSingle(x).WriteSingle(y).WriteSingle(z)
            .WriteUInt16((ushort)heading)
            .ToArray();

    public static byte[] MoveObject(int objectId, float destX, float destZ, float speed, int heading, int action = 0, int turn = 0)
        => new PacketWriter(ServerMsg.MoveObject)
            .WriteByte(1)
            .WriteUInt32((uint)objectId)
            .WriteSingle(destX).WriteSingle(destZ)
            .WriteUInt16((ushort)Math.Clamp((int)(speed * 100), 0, 65535))
            .WriteUInt16((ushort)heading)
            .WriteInt16((short)Math.Clamp(turn, 0, short.MaxValue))
            .WriteUInt16((ushort)action)
            .ToArray();

    public static byte[] PlaceObject(int objectId, float x, float y, float z, float? destX = null, float? destZ = null,
        float speed = 0f, int action = 0, int orient = 0, int? destOrient = null, int turn = 0)
        => new PacketWriter(ServerMsg.PlaceObject)
            .WriteUInt32((uint)objectId)
            .WriteSingle(x).WriteSingle(y).WriteSingle(z)
            .WriteUInt16((ushort)orient)
            .WriteSingle(destX ?? x).WriteSingle(destZ ?? z)
            .WriteUInt16((ushort)Math.Clamp((int)(speed * 100), 0, 65535))
            .WriteUInt16((ushort)(destOrient ?? orient))
            .WriteInt16((short)Math.Clamp(turn, 0, short.MaxValue))
            .WriteUInt16((ushort)action)
            .ToArray();
}
