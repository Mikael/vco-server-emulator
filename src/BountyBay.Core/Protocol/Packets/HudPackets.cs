namespace BountyBay.Core.Protocol.Packets;

public static class BboChatKind
{
    public const int Team = 1;
    public const int City = 2;
    public const int Public = 4;
    public const int Private = 5;
    public const int Guild = 6;
    public const int GuildNotice = 7;
    public const int Department = 8;
    public const int DepartmentNotice = 9;
    public const int Blessing = 10;
    public const int Popular = 11;
    public const int Newbie = 13;
    public const int Battle = 14;
    public const int Trumpet = 15;
    public const int Master = 16;
    public const int TeamRecruit = 17;
    public const int PublicChannel = 18;
}

public static class HudPackets
{
    public const uint PopularColour = 0x41eb00;

    public static byte[] NetPingAck(int counter) => UiPackets.Custom($"scNetPingAck {counter}");

    public static byte[] TitledChat(int kind, string sender, string title, string text)
        => UiPackets.Custom($"c {kind} {UiPackets.Token(sender)} {UiPackets.Token(title)} {UiPackets.Line(text)}");

    public static byte[] PopularChat(string sender, string title, string text)
        => UiPackets.Custom($"c {BboChatKind.Popular} {UiPackets.Token(sender)} {UiPackets.Token(title)} {PopularColour} {UiPackets.Line(text)}");

    public static byte[] UntitledChat(int kind, string sender, string text)
        => UiPackets.Custom($"c {kind} {UiPackets.Token(sender)} {UiPackets.Line(text)}");

    public static byte[] ChannelChat(int kind, string sender, string title, string text) => kind switch
    {
        BboChatKind.Popular => PopularChat(sender, title, text),
        BboChatKind.Newbie or BboChatKind.Trumpet => UntitledChat(kind, sender, text),
        _ => TitledChat(kind, sender, title, text),
    };
}
