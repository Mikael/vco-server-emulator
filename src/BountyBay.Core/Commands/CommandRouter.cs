using BountyBay.Core.Commands.Commands;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands;

public interface ITextCommand
{
    IEnumerable<string> Names { get; }

    bool Execute(ClientSession session, string[] words);
}

public sealed class CommandRouter
{
    private static readonly HashSet<string> ClientCommands =
    [
        "CMD_SHOWNOUSE", "Suit", "AskSuit", "SuitOne", "_SuitOne", "SuitOneRank", "_SuitOneRank", "SuitOneTotem", "_SuitOneTotem", "SelectWork", "WorkUSKill",
        "csHealSailor", "HandTo", "Chuck", "DirectSelect", "RefreshViewport", "csExpandLock", "ExpandContainer", "UseToBoxItem",
        "AskPostSellOpen", "AskPostSellPageSell", "AskPostSellClose", "AskPostSellPageSearch", "AskPostSellSell", "AskPostSellUnSell",
        "AskPostSellModify", "AskPostSellBuy", "AskPostSellSearch", "AskPostSellPrev", "AskPostSellNext",
        "csAppartusStart", "csAppartusClose", "csAppartusCancel", "csAppartusSave",
        "csAttachTask", "csUnAttachTask", "csAutoAttachTask", "GiveUpTask", "NewTaskOk", "csAskRookieInfo", "csAcceptTeamBTask", "SelectExploreArea", "GetExploreRaward",
        "csAskJoinTeam", "SetTeamPickWay", "SetTeamPickLevel", "ChangeCaptain", "VirtualTeam", "csPermit", "csCreateCrossTeam", "csCrossTeamInvite", "ChatInvite",
        "ConsInvite", "AskConsPreJoin", "CancelConsJoin", "AskConsDetail", "AskConsMemberAll", "csAskJoinedMembers", "AskConsKickMemberList", "AskConsMemberKick",
        "AskConsRelation", "AskConsRelationAll", "csNextConsRelation", "csPrevConsRelation", "AskConsQuit", "AskConsDismiss", "SetConsBulletin", "csAskConsLog", "AskUnionInfo",
        "csAskShifu", "csSearchShifu", "csShituList", "csShoutuList", "csStartShoutu", "csEndShoutu", "csIsShoutu", "csDelShitu", "ListAssist", "csAskMailGoodsItem",
        "AskConsList", "AskLandList", "AskSeaList", "AskTreasureList", "AskBusinessList", "AskHonorLevelList", "AskPKTopList", "AskBattleList", "AskRefineMentList",
        "store", "Store", "csShowVIPInfo", "csGetOnLineArward", "csGetReward", "csGetHonorRaward", "csEnjoyValeDay", "csLinkGameScore",
        "AddBlacklist", "AddFriend", "AddShortcut", "AdjShipSpeed", "ApplyConsDept", "Buy", "BuySailor", "BuyShip",
        "CancelStallItem", "ChangeTeamOceanMode", "CheckStallBuy", "CloseConsDept", "ConsFireOut", "ConsRelationCmd",
        "Contraband", "CreateCons", "CreateConsDept", "DepositMoney", "display", "DoAction", "EncashMoney", "ERROR",
        "FireSailor", "GetPlayerInfo", "GetRecvItem", "HandToShortcut", "ItemPwdLogin", "ItemPwdSet", "JoinCons",
        "KickOut", "MakingItem", "nt", "QMI", "QueryRebuildShip", "QuerySTObj", "RebuildShip", "RebuildStatuary",
        "ReferConsTaskItem", "RemoveBlacklist", "RemoveFriend", "RemoveItem", "RemoveShortcut", "RemoveSM", "Repair",
        "rm", "SailDye", "SaveItem", "SaveSM", "SeeMemory", "SeeMemoryNum", "SeeRw_List", "SeeRw_ListNum", "Select",
        "Sell", "SellCard", "SendItem", "SendMoney", "SendSM", "SetConsDept", "SetConsDuty", "SetConsTenet",
        "SetExchange", "SetTitle", "ShipDye", "ShortcutPage", "Skill", "StallBuy", "StallSell", "StartStall", "State",
        "StatueSetMessage", "TakeUp", "TeamInvite", "TeamObj", "TransferConsProffer", "Unlay", "UseTo",
        "UseToBoxItem", "UseUniqueSkill", "WarCitySetting",
        "ChangePetName", "NoviceToNext", "PetCmd", "PetSkill", "ShowRewardInfo", "RecvSM", "RepairShip", "RenameShip", "ChangeShip", "NextAmmo",
        "ConsQuit", "ConsDismiss", "CloseConsApply", "ProcessConsApply", "ChangeConsWarState", "BeforeConsBid",
    ];

    private readonly Dictionary<string, ITextCommand> _commands = [];
    private readonly SlashCommands _slash = new();

    public CommandRouter Add(ITextCommand command)
    {
        foreach (string name in command.Names) _commands[name] = command;
        return this;
    }

    public static CommandRouter CreateDefault()
        => new CommandRouter()
            .Add(new BboUnsupportedCommands())
            .Add(new SystemCommands())
            .Add(new BboSelectCommand())
            .Add(new BboSellCommand())
            .Add(new BboBuyCommand())
            .Add(new RemoveItemCommand())
            .Add(new ShortcutCommands())
            .Add(new GroupCommands())
            .Add(new BankCommands())
            .Add(new DoActionCommand())
            .Add(new AdjShipSpeedCommand())
            .Add(new SetTitleCommand())
            .Add(new ShipServiceCommands())
            .Add(new SendMoneyCommand())
            .Add(new UnlayCommand())
            .Add(new ChannelChatCommand())
            .Add(new MotionCommands())
            .Add(new BboTakeUpCommand())
            .Add(new WorldActionCommands())
            .Add(new SkillCommand())
            .Add(new UseUniqueSkillCommand())
            .Add(new BboSkillCommand())
            .Add(new BboUseUniqueSkillCommand())
            .Add(new QuestLogCommands())
            .Add(new NoviceCommands())
            .Add(new PetCommands())
            .Add(new ExchangeCommand())
            .Add(new SwitchExchangeCommand())
            .Add(new TradeCommands())
            .Add(new StallCommands())
            .Add(new MailCommands())
            .Add(new CraftingCommands())
            .Add(new TeamCommands())
            .Add(new TeamChatCommand())
            .Add(new LogoutCommands())
            .Add(new PlayerInfoCommand())
            .Add(new FriendCommands())
            .Add(new ChatChannelCommand())
            .Add(new AnswerCommand())
            .Add(new LeaveGroupCommand())
            .Add(new GuildCommands())
            .Add(new GuildChatCommand())
            .Add(new FleetCommands())
            .Add(new BboQuestCommands())
            .Add(new BboTeamCommands())
            .Add(new BboGuildCommands())
            .Add(new BboMentorCommands())
            .Add(new BboMailCommands())
            .Add(new RankingCommands())
            .Add(new RankingChatCommand())
            .Add(new BboToolbarCommands())
            .Add(new BboShipCommands())
            .Add(new HandToCommand())
            .Add(new BagServiceCommands())
            .Add(new ConsignCommands())
            .Add(new CompoundCommands())
            .Add(new TotemFuseCommands())
            .Add(new BboCharacterCommands())
            .Add(new BboSeaRouteCommands())
            .Add(new BboTeamProtocolCommands())
            .Add(new BboRewardCommands())
            .Add(new GoToObjCommand())
            .Add(new ParcelCostCommand())
            .Add(new FindBoothCommand())
            .Add(new AuctionCommands())
            .Add(new FireFlagCommands())
            .Add(new BeliefFuseCommands());

    public bool Knows(string name) => ClientCommands.Contains(name);

    public IEnumerable<string> Implemented => _commands.Keys.Order();

    public IEnumerable<string> NotImplemented => ClientCommands.Where(name => !_commands.ContainsKey(name)).Order();

    public bool Execute(ClientSession session, string[] words)
        => _commands.TryGetValue(words[0], out ITextCommand? command) && command.Execute(session, words);

    public void Slash(ClientSession session, string[] words) => _slash.Execute(session, words, this);
}
