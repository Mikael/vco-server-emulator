namespace BountyBay.Core.Protocol.Packets;

public static class PetWhistlePackets
{
    public const int LandView = 66;
    public const int SeaView = 67;
    public const int EquipmentView = 42;
    public const int PackageView = 43;
    public const int Slots = 15;
    public const int AutoSlots = 4;

    public static byte[] PetShow(int petId, int type, string itemId) => ChatPackets.Script(petId, $"scPetShow {type} {UiPackets.Token(itemId)}");

    public static byte[] PetHide(int petId) => ChatPackets.Script(petId, "scPetHide");

    public static byte[] AutoOpen() => ChatPackets.Script(0, "AutoOpenPetWnd");

    public static byte[] SkillUpdate(int petId, string list) => ChatPackets.Script(petId, $"scSkillUpdate {list}");

    public static byte[] SkillSet(int petId, int used) => ChatPackets.Script(petId, $"scSkillSet {used}");
}
