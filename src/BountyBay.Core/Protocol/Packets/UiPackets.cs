namespace BountyBay.Core.Protocol.Packets;

public enum ChatChannel
{
    Team = 1,
    City = 2,
    Public = 4,
    Private = 5,
    Guild = 6,
    GuildNotice = 7,
    Department = 8,
    DepartmentNotice = 9,
    Blessing = 10,
}

public enum LeaveGroupState
{
    Disabled = -1,
    Hidden = 0,
    Scene = 1,
    CollectingField = 2,
}

public static class UiPackets
{
    public const char Blank = '\u0003';
    public const char FieldSeparator = '\b';

    public static string Token(string text)
    {
        string cleaned = Line(text).Trim().Replace(' ', Blank).Replace('\t', Blank);
        return cleaned.Length == 0 ? "-" : cleaned;
    }

    public static string Line(string text)
        => text.Replace("\r", "").Replace('\n', ' ').Replace(FieldSeparator, ' ');

    private static string Command(string command) => Line(command).Replace("%", "").Trim();

    public static byte[] Custom(string text, int objectId = 0) => ChatPackets.Script(objectId, text);

    public static byte[] MessageBox(string text) => Custom("MBox " + Line(text));

    public static byte[] QuestionBox(string text, string yesCommand)
        => Custom($"QBox {Token(text)} {Command(yesCommand)}");

    public static byte[] ShowQuestionBox(string text, string yesCommand, string noCommand = "")
        => Custom($"ShowQBox {Line(text)}{FieldSeparator}{Command(yesCommand)}{FieldSeparator}{Command(noCommand)}");

    public static byte[] InputBox(string command, string title, string info)
        => Custom($"IBox {Token(Command(command))} {Token(title)} {Token(info)}");

    public static byte[] NumberInputBox(int first, int second, string textA, string textB, string textC, string rest)
        => Custom($"NIBox {first} {second} {Token(textA)} {Token(textB)} {Token(textC)} {Command(rest)}");

    public static byte[] Request(string text) => Custom("Request " + Token(text));

    public static byte[] ShowRequest(int requesterObjectId, string text, string kind)
        => Custom($"ShowRequest {Token(text)} {Token(kind)}", requesterObjectId);

    public static byte[] ShowRequestEx(int flags, string motive, string text, string name = "-", string extra = "-")
        => Custom($"ShowRequestEx {flags} {Token(motive)} {Token(text)} {Token(name)} {Token(extra)}");

    public static byte[] TopText(int objectId, int number, string first, string second)
        => Custom($"TopText {number} {Token(first)} {Token(second)}", objectId);

    public static byte[] PlaySound(string name) => Custom("PlaySound " + Token(name));

    public static byte[] CloseMenu() => Custom("CloseMenu");

    public static byte[] Freeze(int first, int second, int time) => Custom($"freeze {first} {second} {time}");

    public static byte[] Unfreeze(int first, int second) => Custom($"_freeze {first} {second}");

    public static byte[] CanLeaveGroup(LeaveGroupState state) => Custom($"CanLeaveGroup {(int)state}");

    public static byte[] LogoutTimer(int seconds) => Custom($"LogoutTimer {Math.Max(1, seconds)}");

    public static byte[] LogoutOk() => Custom("LogoutOK");

    public static byte[] LogoutCancel() => Custom("LogoutCancel");

    public static byte[] UpdatePlayerInfo(string name, int photo, string rank, string totalSkillLevel, string nationality)
        => Custom($"UpdatePlayerInfo {Token(name)} {photo} {Token(rank)} {Token(totalSkillLevel)} {Token(nationality)}");

    public static byte[] ChannelChat(ChatChannel channel, string sender, string title, string text)
        => Custom($"c {(int)channel} {Token(sender)} {Token(title)} {Line(text)}");

    public static byte[] WhisperEcho(string receiver, string text)
        => Custom($"c 0 {Token(receiver)} {Line(text)}");
}
