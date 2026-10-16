using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

internal static class BboArguments
{
    public static bool Last(string[] words, out int value)
    {
        value = 0;
        return words.Length >= 2 && int.TryParse(words[^1], out value);
    }

    public static string Rest(string[] words, int start) => words.Length > start ? string.Join(' ', words.Skip(start)) : "";
}

public sealed class BboQuestCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csAttachTask", "csUnAttachTask", "csAutoAttachTask", "GiveUpTask", "NewTaskOk", "NoviceToNext", "csAskRookieInfo",
        "csAcceptTeamBTask", "SelectExploreArea", "GetExploreRaward",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAttachTask" when BboArguments.Last(words, out int attached):
                session.AttachQuest(attached);
                return true;
            case "csUnAttachTask" when BboArguments.Last(words, out int detached):
                session.DetachQuest(detached);
                return true;
            case "csAutoAttachTask":
                session.ToggleAutoTrack();
                return true;
            case "GiveUpTask" when BboArguments.Last(words, out int abandoned):
                session.GiveUpQuest(abandoned);
                return true;
            case "NewTaskOk" when BboArguments.Last(words, out int accepted):
                session.AcceptOfferedQuest(accepted);
                return true;
            case "NoviceToNext":
                session.NextNoviceStep();
                return true;
            case "csAskRookieInfo":
                Log.Debug(session.Tag, $"{session.Name} rookie info request: {string.Join(' ', words)}");
                return true;
            case "csAcceptTeamBTask":
                session.NotAvailable("Team battle tasks", words[0]);
                return true;
            case "SelectExploreArea" or "GetExploreRaward":
                session.NotAvailable("Exploration", words[0]);
                return true;
            default:
                return false;
        }
    }
}

public sealed class BboTeamCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "csAskJoinTeam", "SetTeamPickWay", "SetTeamPickLevel", "ChangeCaptain", "VirtualTeam", "csPermit", "csCreateCrossTeam",
        "csCrossTeamInvite", "ChatInvite",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAskJoinTeam" when words.Length >= 2:
                session.AskJoinTeam(words[1], words.Length > 2 ? words[2] : "");
                return true;
            case "SetTeamPickWay" when BboArguments.Last(words, out int way):
                session.SetTeamPick("TeamPickWay", way);
                return true;
            case "SetTeamPickLevel" when BboArguments.Last(words, out int level):
                session.SetTeamPick("TeamPickLevel", level);
                return true;
            case "ChangeCaptain" when words.Length >= 2:
                session.ChangeTeamCaptain(words[1]);
                return true;
            case "csPermit":
                Log.Debug(session.Tag, $"{session.Name} permit answer: {string.Join(' ', words)}");
                return true;
            case "VirtualTeam":
                session.NotAvailable("Team recruiting", words[0]);
                return true;
            case "csCreateCrossTeam" or "csCrossTeamInvite":
                session.NotAvailable("Cross-server teams", words[0]);
                return true;
            case "ChatInvite":
                session.NotAvailable("Chat rooms", words[0]);
                return true;
            default:
                return false;
        }
    }
}

public sealed class BboGuildCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "ConsInvite", "AskConsPreJoin", "CancelConsJoin", "AskConsDetail", "AskConsMemberAll", "csAskJoinedMembers",
        "AskConsKickMemberList", "AskConsMemberKick", "AskConsRelation", "AskConsRelationAll", "csNextConsRelation",
        "csPrevConsRelation", "AskConsQuit", "AskConsDismiss", "SetConsBulletin", "csAskConsLog", "AskUnionInfo",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "ConsInvite" when words.Length >= 2:
                session.InviteToGuild(words[1]);
                return true;
            case "AskConsPreJoin" when words.Length >= 2:
                GuildDirectory.Apply(session, BboArguments.Rest(words, 1));
                return true;
            case "CancelConsJoin" when words.Length >= 2:
                session.CancelGuildApplication(BboArguments.Rest(words, 1));
                return true;
            case "AskConsDetail":
                session.ShowGuildDetail(BboArguments.Rest(words, 1));
                return true;
            case "AskConsMemberAll" or "csAskJoinedMembers" or "AskConsKickMemberList" or "AskConsRelation" or "AskConsRelationAll":
                session.RefreshGuildLists();
                return true;
            case "AskConsMemberKick" when words.Length >= 3:
                GuildDirectory.Expel(session, words[^1]);
                return true;
            case "AskConsQuit":
                GuildDirectory.Quit(session);
                return true;
            case "AskConsDismiss":
                GuildDirectory.Dismiss(session);
                return true;
            case "SetConsBulletin":
                GuildDirectory.SetTenet(session, BboArguments.Rest(words, 1));
                return true;
            case "csNextConsRelation" or "csPrevConsRelation" or "csAskConsLog":
                Log.Debug(session.Tag, $"{session.Name} guild page request: {string.Join(' ', words)}");
                return true;
            case "AskUnionInfo":
                return true;
            default:
                return false;
        }
    }
}

public sealed class BboMentorCommands : ITextCommand
{
    public IEnumerable<string> Names =>
        ["csAskShifu", "csSearchShifu", "csShituList", "csShoutuList", "csStartShoutu", "csEndShoutu", "csIsShoutu", "csDelShitu", "ListAssist"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAskShifu" when words.Length >= 2:
                session.AskMaster(words[1]);
                return true;
            case "csSearchShifu":
                session.SearchMaster(BboArguments.Rest(words, 1));
                return true;
            case "csShituList" or "csShoutuList" or "ListAssist":
                session.ShowMentorList();
                return true;
            case "csStartShoutu":
                session.SetRecruiting(true);
                return true;
            case "csEndShoutu":
                session.SetRecruiting(false);
                return true;
            case "csIsShoutu":
                session.SystemMessage(session.Character.RecruitingApprentices ? "You take apprentices." : "You take no apprentices.");
                return true;
            case "csDelShitu" when words.Length >= 2:
                session.RemoveMentor(words[1]);
                return true;
            default:
                return false;
        }
    }
}

public sealed class BboMailCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csAskMailGoodsItem"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.AskMailGoods();
        return true;
    }
}

public sealed class RankingCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "AskConsList", "AskLandList", "AskSeaList", "AskTreasureList", "AskBusinessList", "AskHonorLevelList", "AskPKTopList",
        "AskBattleList", "AskRefineMentList", "order/getrich", "order/getcons", "order/getteacher",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        RankingList? list = BboSocialPackets.RankingRequest(words[0]);
        if (list != null) Rankings.Open(session, list.Value);
        else Rankings.Tell(session, words[0]);
        return true;
    }
}

public sealed class RankingChatCommand : ITextCommand
{
    private readonly ChatChannelCommand _channels = new();
    private readonly TeamChatCommand _team = new();
    private readonly GuildChatCommand _guild = new();

    public IEnumerable<string> Names => ["c1", "c2", "c4", "c6"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length >= 2 && Rankings.IsRequest(words[1]))
        {
            Rankings.Tell(session, words[1]);
            return true;
        }
        return words[0] switch
        {
            "c1" => _team.Execute(session, words),
            "c6" => _guild.Execute(session, words),
            _ => _channels.Execute(session, words),
        };
    }
}

public sealed class BboToolbarCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "store", "Store", "csShowVIPInfo", "csGetOnLineArward", "csGetReward", "csGetHonorRaward", "csEnjoyValeDay", "csLinkGameScore",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "store" or "Store":
                session.NotAvailable("The item mall", string.Join(' ', words));
                return true;
            case "csShowVIPInfo":
                session.NotAvailable("VIP membership", words[0]);
                return true;
            case "csGetOnLineArward" or "csGetReward" or "csGetHonorRaward" or "csEnjoyValeDay":
                session.NotAvailable("Daily and online rewards", string.Join(' ', words));
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} mini game score: {string.Join(' ', words)}");
                return true;
        }
    }
}
