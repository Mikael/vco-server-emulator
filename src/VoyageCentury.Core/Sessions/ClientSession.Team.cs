using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed partial class ClientSession
{
    public const int LogoutSeconds = 5;
    public const int FriendLimit = 50;
    public const int FriendOnline = 1;
    public const int FriendListed = 2;
    public const int FriendBlocked = 4;

    private int _logoutSerial;
    private bool _loggingOut;
    private Action<bool>? _pendingAnswer;

    public Team? Team { get; internal set; }

    public ClientSession? TeamInviter { get; internal set; }

    public long TeamInvitedAt { get; internal set; }

    public bool TeamEnabled { get; private set; } = true;

    public int Portrait => Properties.Int("Photo");

    public string ChatTitle => Character.Title.Length > 0 ? Character.Title : $"Lv{TotalSkillLevel}";

    public void EnterWorldSocial()
    {
        SetSocialProperty("EnableTeam", TeamEnabled ? 1 : 0);
        NotifyFriends(online: true);
    }

    public void LeaveWorldSocial()
    {
        _loggingOut = false;
        _logoutSerial++;
        _pendingAnswer = null;
        TeamDirectory.Disconnect(this);
        NotifyFriends(online: false);
    }

    public void SwitchTeam()
    {
        TeamEnabled = !TeamEnabled;
        SetSocialProperty("EnableTeam", TeamEnabled ? 1 : 0);
        SystemMessage(TeamEnabled ? "You now accept team invitations." : "You now refuse team invitations.");
    }

    public void BeginLogout()
    {
        int serial = ++_logoutSerial;
        _loggingOut = true;
        Send(UiPackets.LogoutTimer(LogoutSeconds), "(logout countdown)");
        Log.Info(Tag, $"{Name} asks to log out");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(LogoutSeconds));
            lock (Context.World.Sync)
            {
                if (!_loggingOut || serial != _logoutSerial || _closed) return;
                _loggingOut = false;
                Save();
                Send(UiPackets.LogoutOk(), "(logout)");
                Log.Info(Tag, $"{Name} may log out");
            }
        });
    }

    public void CancelLogout()
    {
        _loggingOut = false;
        _logoutSerial++;
        Send(UiPackets.LogoutCancel(), "(logout cancelled)");
    }

    public void Ask(string text, Action<bool> answer)
    {
        _pendingAnswer = answer;
        Send(UiPackets.Request(text), "(request)");
    }

    public bool Answer(bool yes)
    {
        Action<bool>? answer = _pendingAnswer;
        _pendingAnswer = null;
        if (answer == null) return false;
        answer(yes);
        return true;
    }

    public void SendChannelChat(ChatChannel channel, ClientSession from, string text)
        => Send(UiPackets.ChannelChat(channel, from.Name, from.ChatTitle, text), $"({channel} chat from {from.Name})");

    public bool Whisper(string name, string text)
    {
        ClientSession? receiver = Context.World.FindPlayer(name);
        if (receiver == null || !receiver.InWorld)
        {
            SystemMessage($"{name} is not online.");
            return false;
        }
        if (!Listed(receiver.Character.Blacklist, Name))
            receiver.SendChannelChat(ChatChannel.Private, this, text);
        Send(UiPackets.WhisperEcho(receiver.Name, text), $"(whisper to {receiver.Name})");
        Log.Info(Tag, $"{Name} whispers to {receiver.Name}: {text}");
        return true;
    }

    public void Broadcast(ChatChannel channel, string text)
    {
        foreach (ClientSession other in Context.World.Sessions.ToList())
        {
            if (other.InWorld) other.SendChannelChat(channel, this, text);
        }
        Log.Info(Tag, $"{Name} on {channel}: {text}");
    }

    public IEnumerable<object[]> FriendRows(ClientSession? gone = null)
    {
        foreach (string name in Character.Friends.Union(Character.Blacklist).ToList())
        {
            ClientSession? other = Context.World.FindPlayer(name);
            int state = other != null && other != gone && other.InWorld ? FriendOnline : 0;
            if (Character.Friends.Contains(name)) state |= FriendListed;
            if (Character.Blacklist.Contains(name)) state |= FriendBlocked;
            yield return [name, state];
        }
    }

    public void SendFriendTables(ClientSession? gone = null)
    {
        List<object[]> friends = FriendRows(gone).ToList();
        Send(TablePackets.Clear(GameTables.Friends), "(clear Friends)");
        if (friends.Count > 0) Send(TablePackets.Rows(GameTables.Friends, friends), "(Friends)");
        List<object[]> blocked = Character.Blacklist.Select(name => new object[] { name }).ToList();
        Send(TablePackets.Clear(GameTables.Blacklist), "(clear Blacklist)");
        if (blocked.Count > 0) Send(TablePackets.Rows(GameTables.Blacklist, blocked), "(Blacklist)");
    }

    public void NotifyFriends(bool online)
    {
        foreach (ClientSession other in Context.World.Sessions.ToList())
        {
            if (other == this || !other.InWorld) continue;
            if (Listed(other.Character.Friends, Name) || Listed(other.Character.Blacklist, Name))
                other.SendFriendTables(online ? null : this);
        }
    }

    public void ChangeFriend(string name, bool add, bool blacklist)
    {
        SortedSet<string> names = blacklist ? Character.Blacklist : Character.Friends;
        string list = blacklist ? "blacklist" : "friend list";
        if (add)
        {
            ClientSession? other = Context.World.FindPlayer(name);
            if (other != null) name = other.Name;
            if (name.Equals(Name, StringComparison.OrdinalIgnoreCase))
            {
                SystemMessage("You cannot add yourself.");
                return;
            }
            if (Listed(names, name))
            {
                SystemMessage($"{name} is already on your {list}.");
                return;
            }
            if (names.Count >= FriendLimit)
            {
                SystemMessage($"Your {list} is full.");
                return;
            }
            names.Add(name);
            SystemMessage($"{name} was added to your {list}.");
        }
        else
        {
            string? existing = names.FirstOrDefault(entry => entry.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;
            names.Remove(existing);
            SystemMessage($"{existing} was removed from your {list}.");
        }
        Save();
        SendFriendTables();
    }

    public void SendPlayerInfo(string name)
    {
        ClientSession? other = name.Length == 0 ? this : Context.World.FindPlayer(name);
        if (other == null || !other.InWorld)
        {
            Send(UiPackets.UpdatePlayerInfo(name, 0, "-", "-", "-"), $"(player info {name})");
            return;
        }
        string rank = other.Character.Title.Length > 0 ? other.Character.Title : "-";
        int skills = other.Character.Skills.Sum(skill => skill.Level);
        Send(UiPackets.UpdatePlayerInfo(other.Name, other.Portrait, rank, skills.ToString(), other.Properties.Text("CountryName", "-")),
            $"(player info {other.Name})");
    }

    private static bool Listed(IEnumerable<string> names, string name)
        => names.Any(entry => entry.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void SetSocialProperty(string name, int value)
    {
        if (PropertyTable.Parse(name, value.ToString()) != null) SetProperty(name, value);
    }
}
