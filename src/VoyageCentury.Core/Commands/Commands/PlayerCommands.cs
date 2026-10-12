using System.Globalization;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands.Commands;

public sealed class SocialCommands : ITextCommand
{
    public IEnumerable<string> Names => ["AddFriend", "RemoveFriend", "AddBlacklist", "RemoveBlacklist"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 2) return false;
        SortedSet<string> names = words[0].Contains("Friend") ? session.Character.Friends : session.Character.Blacklist;
        if (words[0].StartsWith("Add", StringComparison.Ordinal)) names.Add(words[1]);
        else names.Remove(words[1]);
        session.Save();
        session.SystemMessage($"{words[0]}: {words[1]}");
        return true;
    }
}

public sealed class GroupCommands : ITextCommand
{
    public IEnumerable<string> Names => ["TeamInvite", "JoinCons", "CreateCons", "KickOut", "TeamObj"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.SystemMessage("Teams and guilds are not available on this server yet.");
        return true;
    }
}

public sealed class BankCommands : ITextCommand
{
    public IEnumerable<string> Names => ["DepositMoney", "EncashMoney"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 2 || !int.TryParse(words[1], out int amount) || amount < 0) return false;
        if (words[0] == "DepositMoney") session.Deposit(amount);
        else session.Withdraw(amount);
        return true;
    }
}

public sealed class DoActionCommand : ITextCommand
{
    public IEnumerable<string> Names => ["DoAction"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 2) return false;
        int action = session.Context.Data.Actions.IndexOf(words[1]);
        if (action >= 0) session.PlayAction(action, "(emote)");
        Log.Debug(session.Tag, $"{session.Name} emote {words[1]}");
        return true;
    }
}

public sealed class AdjShipSpeedCommand : ITextCommand
{
    public IEnumerable<string> Names => ["AdjShipSpeed"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 2) return false;
        if (float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fraction) && float.IsFinite(fraction))
        {
            session.SetSail(fraction);
            Log.Debug(session.Tag, $"{session.Name} sail {session.Sail:F2}");
        }
        return true;
    }
}

public sealed class SetTitleCommand : ITextCommand
{
    private const int LongestTitle = 30;

    public IEnumerable<string> Names => ["SetTitle"];

    public bool Execute(ClientSession session, string[] words)
    {
        string title = string.Join(' ', words.Skip(1));
        if (title.Length > LongestTitle) title = title[..LongestTitle];
        session.Character.Title = title;
        session.SetProperty("Title", title, visibleToOthers: true);
        session.Save();
        return true;
    }
}

public sealed class GetPlayerInfoCommand : ITextCommand
{
    public IEnumerable<string> Names => ["GetPlayerInfo"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.SystemMessage($"{session.Name}: level {session.Character.Level}, {session.Money} gold");
        return true;
    }
}

public sealed class SlashCommands
{
    public void Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "/go" or "/ship" when words.Length == 2 && int.TryParse(words[1], out int scene):
                if (!session.Context.Data.Scenes.Exists(scene))
                {
                    session.SystemMessage($"no such scene {scene}");
                    return;
                }
                session.ForceLand = words[0] != "/ship";
                session.Travel(scene);
                break;
            case "/sail" or "/land":
                session.SailOff();
                break;
            case "/where":
                (float x, float z) = session.Mover!.Now();
                session.SystemMessage($"scene {session.Scene} at {x:F1}, {z:F1}");
                break;
            case "/heal":
                session.SetHp(session.MaxHp);
                break;
            case "/money" when words.Length == 2 && int.TryParse(words[1], out int amount) && amount >= 0:
                session.SetMoney(amount);
                session.Save();
                break;
            default:
                session.SystemMessage("commands: /go <scene id>, /ship <scene id>, /sail, /land, /where, /heal, /money <amount>");
                break;
        }
    }
}
