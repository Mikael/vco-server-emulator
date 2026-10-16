using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const float BboCoastExitBand = 0.8f;
    public const int BboOnlineAwardMoney = 200;
    public const int BboDailyRewardMoney = 1000;

    private static readonly object BboProtocolGate = new();
    private static readonly Dictionary<int, (float CenterX, float CenterZ, float HalfWidth, float HalfHeight)?> BboCoastBounds = [];
    private static List<int>? _bboAwardMinutes;

    private bool _bboShortWayShown;
    private string? _bboJoinApplicant;
    private int _bboAwardStage;
    private long _bboAwardSince;
    private bool _bboDailyClaimed;

    public bool BboInCoastalSea => AtSea && !BboOceanScene(Scene) && Scene % 1000 == 3;

    public void BboCheckCoastExit(float x, float z)
    {
        if (!BboInCoastalSea)
        {
            BboHideShortWay();
            return;
        }
        var bounds = CoastBounds(Scene);
        if (bounds == null) return;
        (float centerX, float centerZ, float halfWidth, float halfHeight) = bounds.Value;
        bool outer = MathF.Abs(x - centerX) >= halfWidth * BboCoastExitBand || MathF.Abs(z - centerZ) >= halfHeight * BboCoastExitBand;
        if (outer && !_bboShortWayShown)
        {
            _bboShortWayShown = true;
            Send(BboProtocolPackets.ShowShortWay(), "(leave coastal waters)");
        }
        else if (!outer) BboHideShortWay();
    }

    public void BboHideShortWay()
    {
        if (!_bboShortWayShown) return;
        _bboShortWayShown = false;
        Send(BboProtocolPackets.HideShortWay(), "(hide leave coastal waters)");
    }

    public void BboLeaveGroup()
    {
        if (!BboInCoastalSea)
        {
            SystemMessage("There is nothing to leave here.");
            return;
        }
        BboHideShortWay();
        BboSailToOcean();
    }

    public void BboGoPort()
    {
        if (BboAtOcean)
        {
            BboComeAshoreFromOcean();
            return;
        }
        BboHideShortWay();
        if (!AtSea || !SailByLink()) SystemMessage("There is no dock to sail to from here.");
    }

    public BboInviteCard BboCard(string text)
        => new(Name, Character.WorkType, Character.WorkLevel, Properties.Int("LandPoints"), Properties.Int("SeaPoints"),
            Scene.ToString(CultureInfo.InvariantCulture), Properties.Text("ConsName"), text);

    public void BboSendTeamInvite(ClientSession inviter)
        => Send(BboProtocolPackets.TeamInviteCard(inviter.BboCard($"{inviter.Name}")), $"(team invitation card from {inviter.Name})");

    public void BboAskJoinTeam(string first, string second)
    {
        ClientSession? target = Context.World.FindPlayer(first) ?? (second.Length > 0 ? Context.World.FindPlayer(second) : null);
        if (target == null || target == this || !target.InWorld)
        {
            SystemMessage("That player is not online.");
            return;
        }
        if (Team != null)
        {
            SystemMessage("You are already in a team.");
            return;
        }
        ClientSession leader = target.Team?.Leader ?? target;
        if (leader.Team != null && leader.Team.Members.Count >= TeamDirectory.MaxMembers)
        {
            SystemMessage("The team is full.");
            return;
        }
        leader._bboJoinApplicant = Name;
        leader.Send(BboProtocolPackets.TeamJoinCard(BboCard(Name)), $"(team application from {Name})");
        SystemMessage($"You asked {leader.Name} to let you join the team.");
        Log.Info(Tag, $"{Name} applies to the team of {leader.Name}");
    }

    public void BboPermit(bool accept)
    {
        string? name = _bboJoinApplicant;
        _bboJoinApplicant = null;
        ClientSession? applicant = name == null ? null : Context.World.FindPlayer(name);
        if (applicant == null || !applicant.InWorld)
        {
            if (accept) SystemMessage("The application is no longer valid.");
            return;
        }
        if (!accept)
        {
            applicant.SystemMessage($"{Name} declined your request.");
            return;
        }
        if (Team != null && Team.Leader != this)
        {
            SystemMessage("Only the team leader can accept members.");
            return;
        }
        applicant.TeamInviter = this;
        applicant.TeamInvitedAt = Environment.TickCount64;
        TeamDirectory.Answer(applicant, true);
        applicant.Send(BboProtocolPackets.TeamAdded(), "(team application accepted)");
    }

    public void BboStartRewards()
    {
        if (_bboAwardStage == 0)
        {
            _bboAwardStage = BboProtocolPackets.FirstOnlineStage;
            _bboAwardSince = Environment.TickCount64;
        }
        BboSendAwardState();
        if (PropertyTable.Has("AwardFlag")) SetProperty("AwardFlag", _bboDailyClaimed ? 0 : 1);
    }

    public void BboClaimOnlineAward(int stage)
    {
        if (_bboAwardStage == 0) BboStartRewards();
        List<int> minutes = AwardMinutes();
        if (_bboAwardStage > minutes.Count)
        {
            SystemMessage("You have collected every online reward today.");
            return;
        }
        if (stage != _bboAwardStage)
        {
            SystemMessage("That online reward is not ready.");
            BboSendAwardState();
            return;
        }
        if (AwardSecondsLeft(minutes) > 0)
        {
            SystemMessage("Stay online a little longer to collect this reward.");
            BboSendAwardState();
            return;
        }
        int money = BboOnlineAwardMoney * stage;
        SetMoney((long)Money + money);
        SystemMessage($"Online reward {stage}: you receive {money} silver.");
        Log.Info(Tag, $"{Name} collects online reward {stage} ({money})");
        _bboAwardStage++;
        _bboAwardSince = Environment.TickCount64;
        BboSendAwardState();
    }

    public void BboClaimDailyReward()
    {
        if (_bboDailyClaimed)
        {
            SystemMessage("You have already collected today's reward.");
            return;
        }
        _bboDailyClaimed = true;
        SetMoney((long)Money + BboDailyRewardMoney);
        if (PropertyTable.Has("AwardFlag")) SetProperty("AwardFlag", 0);
        SystemMessage($"Daily reward: you receive {BboDailyRewardMoney} silver.");
        Log.Info(Tag, $"{Name} collects the daily reward");
    }

    public void BboShowVip()
    {
        int level = Properties.Int("VIP");
        Send(BboProtocolPackets.VipInfo(Math.Max(1, level), level > 0 ? "VIP" + level : "-", 0, 0), "(vip info)");
    }

    public void BboSendAwardState()
    {
        List<int> minutes = AwardMinutes();
        if (_bboAwardStage < 1 || _bboAwardStage > minutes.Count) return;
        int left = AwardSecondsLeft(minutes);
        Send(left > 0 ? BboProtocolPackets.AwardCountdown(_bboAwardStage, left) : BboProtocolPackets.AwardReady(_bboAwardStage),
            $"(online reward {_bboAwardStage})");
    }

    private int AwardSecondsLeft(List<int> minutes)
    {
        long elapsed = (Environment.TickCount64 - _bboAwardSince) / 1000;
        return (int)Math.Max(0, minutes[_bboAwardStage - 1] * 60L - elapsed);
    }

    private List<int> AwardMinutes()
    {
        lock (BboProtocolGate)
        {
            if (_bboAwardMinutes != null) return _bboAwardMinutes;
            var minutes = new List<int>();
            try
            {
                string path = Path.Combine(Context.Settings.ResourcePath, "ini", "OnLineAward.ini");
                if (File.Exists(path))
                {
                    foreach (IniSection section in IniFile.Load(path).Named("Award")
                        .OrderBy(section => int.TryParse(section["Stage"], out int stage) ? stage : int.MaxValue))
                    {
                        if (int.TryParse(section["Times"], out int times)) minutes.Add(times);
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error(Tag, exception);
            }
            return _bboAwardMinutes = minutes;
        }
    }

    private (float, float, float, float)? CoastBounds(int scene)
    {
        lock (BboProtocolGate)
        {
            if (BboCoastBounds.TryGetValue(scene, out var known)) return known;
            (float, float, float, float)? bounds = null;
            try
            {
                string path = Path.Combine(Context.Settings.ResourcePath, "ini", "window", "map", scene + ".ini");
                if (File.Exists(path))
                {
                    IniSection header = IniFile.Load(path).Header;
                    if (TryFloat(header["centerx"], out float x) && TryFloat(header["centerz"], out float z)
                        && TryFloat(header["width"], out float width) && TryFloat(header["height"], out float height) && width > 0 && height > 0)
                        bounds = (x, z, width / 2f, height / 2f);
                }
            }
            catch (Exception exception)
            {
                Log.Error(Tag, exception);
            }
            BboCoastBounds[scene] = bounds;
            return bounds;
        }
    }
}
