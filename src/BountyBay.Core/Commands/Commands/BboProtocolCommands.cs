using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class BboSeaRouteCommands : ITextCommand
{
    public IEnumerable<string> Names => ["LeaveGroup", "GoPort", "OpenMapOnOcean", "CloseMapOnOcean", "EscTeamPos"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "LeaveGroup":
                session.BboLeaveGroup();
                return true;
            case "GoPort":
                session.BboGoPort();
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} map notice: {words[0]}");
                return true;
        }
    }
}

public sealed class BboTeamProtocolCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csAskJoinTeam", "csPermit", "csGetTeamMembInfo"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAskJoinTeam" when words.Length >= 2:
                session.BboAskJoinTeam(words[1], words.Length > 2 ? words[2] : "");
                return true;
            case "csPermit" when words.Length >= 2:
                session.BboPermit(words[1] == "1");
                return true;
            case "csGetTeamMembInfo":
                int members = session.Team?.Members.Count ?? 1;
                session.Send(BboProtocolPackets.TeamMemberInfo($"{members}/{TeamDirectory.MaxMembers}"), "(team member info)");
                return true;
            default:
                return false;
        }
    }
}

public sealed class BboRewardCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csShowVIPInfo", "csGetOnLineArward", "csGetReward"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csShowVIPInfo":
                session.BboShowVip();
                return true;
            case "csGetOnLineArward" when BboArguments.Last(words, out int stage):
                session.ClaimOnlineReward(stage);
                return true;
            case "csGetReward":
                session.ClaimDailyReward();
                return true;
            default:
                return false;
        }
    }
}

public sealed class BboUnsupportedCommands : ITextCommand
{
    private static readonly HashSet<string> Quiet =
    [
        "csShipModelMergeClose", "csCloseGlobalView", "csCloseAideDialog", "csCloseArmRebuildHedge", "csCloseCFightSoulList",
        "csCloseChangeFightSoul", "csCloseConsArm", "csCloseConsWarBid", "csCloseFightSoulView", "csCloseImpawn", "csCloseLuckSoul",
        "csCloseNewUpdate", "csCloseRankArmView", "csCloseWakeArmView", "csBonTotemBoxClose", "csArmRebuildClose", "csAideRebuildClose",
        "csFuseClose", "csCancleCFightSoulList", "csCancleChangeFightSoul", "csCancleStartMap", "csCancelTotemReset", "csCancelTrainResult",
        "exclose", "transclose", "csPVPTipInfo", "csFreshReadyTime", "BYE", "SetVisitedSea",
        "csAskQueueST", "csAskConWarEntryTimes", "csOpenGlobalView", "csAskBlog", "ShowCopyInfo", "csCheckPhoneVerify", "AskToBattle_No",
    ];

    private static readonly string[] Words =
    [
        "AppointCaptian", "AskBuildCityInfo", "AskConsWarInfo", "AskGoToObj", "AskToBattle_Yes", "AutoDriveChange",
        "CHANGE_SHIP_MODEL", "ConcelSuit", "Contraband", "csAddAideSuit", "csAddAutoSkill", "csAddQualityLevel", "csAddTotemExp",
        "csAgreeApply", "csAgreeConCopyApply", "csAideArmActive", "csAideArmRebuild", "csAideArmRebuildS", "csAideRebuildView",
        "csApplyConcopy", "csApplyConwar", "csArmRebuildCopy", "csArmRebuildHedge", "csArmRebuildLock", "csArmRebuildSelect",
        "csArmRebuildStart", "csAskAideSuit", "csAskAuction", "csAskConCopyApplyList", "csAskConsActDetail",
        "csAskConsBuildInfo", "csAskConwarApplyList", "csAskCopyInfo", "csAskEquipInfo", "csAskForEntryBattle",
        "csAskForLeaveBattle", "csAskPlayerInfo", "csAskSkillNameOption", "csAuction", "csBankItemList", "csBuildBankIndex",
        "csChangeBankIndexName", "csChangeHatSwitch", "csChangeModel", "csChangeSpecial1", "csChooseCamp", "csChooseShipModel",
        "csCleanChargePower", "csCleanSaveSkill", "csCleanSaveSoul", "csClearFlagView", "csConsCityBattleCommit",
        "csConsCopyCommit", "csConsCopyGame", "csConsCopyGiveUp", "csConscribeTeam", "csConsTeamCancel", "csCreteGiveUp",
        "csDelAideSuit", "csDelBankIndex", "csDoLuckWarkProp", "csDoSaveSkill", "csDoSaveSoul", "csDoUpLevel", "csDoUpQuality",
        "csDoWarkProp", "csDownItem", "csEuropGiveUp", "csEvol", "csFightSoulDevour", "csFightSoulDown", "csFightSoulUpQuality",
        "csForgeFlag", "csForgetPetSkill", "csFreshStartMap", "csFuseCancel", "csFuseSave", "csFuseStart", "csGambleConsMatch",
        "csGetConsMReward", "csGetItem", "csGoWarkProp", "csGroupToCopy", "csGrowItem", "csGuessFBall", "csGuessOlympic",
        "csGuessWinShip", "csImpawnBargain", "csImpawnCancel", "csImpawnDone", "csInvitePlayer", "csItemInfo", "csJoinConsCopy",
        "csJoinLuckSoul", "csLeaveConstestWar", "csLeaveConsWar", "csLeaveConWar", "csLeaveCopyList", "csLeaveHonorWar",
        "csLeaveMatch", "csLeavePaleWar", "csLeaveSpartaWar", "csLookAllCard", "csMakeConsArm", "csMakeStar",
        "csMatchHistryNextPage", "csMatchHistryPrevPage", "csMatchPlayersNextPage", "csMatchPlayersPrevPage", "csMixModel",
        "csNewArmUpdate", "csOpenAideDialog", "csOpenEvolView", "csOpenFBall", "csOpenFightSoulView", "csOpenFlagView",
        "csOpenRankArm", "csOpenRankArmView", "csOpenSaveBit", "csOpenSaveSkill", "csOpenSaveSoul", "csOpenStarView",
        "csOpenTotemBoxView", "csOpenWakeArmView", "csOpenWhistle", "csPetSkillAutoOn", "csPhoneAid", "csPutBankItem",
        "csPutDownBomb", "csQualityItem", "csQueryCleanItemList", "csQueryMatchLay", "csQueryWearInfo", "csQuizAnswer",
        "csQuizSubject", "csRebindConsArm", "csRecoverArmProp", "csRecoverArmPropS", "csRecruitAide", "csRefreshAideList",
        "csRefuseApply", "csRemakeFlag", "csRemoveArmRebuildProp", "csRemoveAutoSkill", "csRestoreSaveSkill",
        "csRestoreSaveSoul", "csRiBankItem", "csSaveArmProp", "csSaveArmPropS", "csSaveCFightSoulList", "csSaveChangeFightSoul",
        "csSaveNorthAmePlayer", "csSaveStartMap", "csSaveTotemReset", "csSaveTrainResult", "csSaveWarkProp", "csSelectAide",
        "csSelectLuckHap", "csSelectLuckSoul", "csSelectRole", "csSelectUpdateType", "csSendInvite", "csSendPoInfo",
        "csSetMatchLeader", "csShipInner", "csShowSailorBarWnd", "csShowTotemReset", "csShowWhistle", "csSmallGameScore",
        "csSplitConsArm", "csSplitSaveProp", "csSplitWarkProp", "csStartBonTotem", "csStartCFightSoulList",
        "csStartChangeFightSoul", "csStartCleanToem", "csStartForge", "csSwitchWhistle", "csTakeOffThing", "csTeamBattle",
        "csTrainAide", "csTransferAide", "csTurnCard", "csUpdateAideProp", "csUpgradeLuckSoul", "csUpHonorListInfo",
        "csUpLevelConsArm", "csUpPropetylist", "csUseAideSkill", "csUseAideSuit", "csUseSoulSkill", "csUseTotemReset",
        "csUseTotemSkill", "csUseWarkProp", "csVConsMatchMList", "csViewChangeFightSoul", "csViewConsArmUp", "csViewConsMatch",
        "csViewFightSoul", "csViewFSUpQuality", "csViewGamble", "csViewMyGamble", "csViewOtherArm", "csWatchFinding",
        "EnsureSuit", "EnsureUnRebuild", "exchange", "FilterByName", "FindBuyItem", "FindBuyItemNext", "FindBuyItemPrev",
        "FindSellItem", "FindSellItemNext", "FindSellItemPrev", "KeepValueYN", "LeaveTower", "open", "OpenSubAssembly",
        "QuerySendCost", "RebuildYN", "ReferItem", "ROBBT_ID", "SaveItem", "scQueryMatchPlayerHonor",
        "SelectAssemblyOrProbability", "SetCityName", "SetupTorpedo", "ShowNextUnionCons", "transprop",
        "csAddItemPropPoint", "csAskForCreteWar", "csAskForEntryTimes", "csAskForEurop", "csHonorWarWinner", "csImpeach",
        "csImpeachPlayer", "csJoinConsCopy2", "csLoadItemMemory", "csOpenArmConsolidate", "csOpenExpSystem", "csPaleWarWinner",
        "csRefuseList", "csSaveItemMemory", "csShowHonorWarTable", "csShowPKTable", "csShowPaleWarTable", "csShowTeamWarTable",
        "csSucMake", "csSucMake1", "csTeamWarWinner",
    ];

    private static readonly (string Prefix, string Feature)[] Features =
    [
        ("RebuildYN", "Ship inner rebuild"), ("EnsureUnRebuild", "Ship inner rebuild"), ("KeepValueYN", "Ship inner rebuild"),
        ("csChooseShipModel", "Ship model merge"), ("csMixModel", "Ship model merge"), ("CHANGE_SHIP_MODEL", "Ship model change"),
        ("csShipInner", "Ship inner rebuild"), ("SaveItem", "The role bank"), ("csBankItemList", "The role bank"),
        ("csPutBankItem", "The role bank"), ("csRiBankItem", "The role bank"), ("csBuildBankIndex", "The role bank"),
        ("csDelBankIndex", "The role bank"), ("csChangeBankIndexName", "The role bank"), ("csAide", "Aides"),
        ("csRecruitAide", "Aides"), ("csRefreshAideList", "Aides"), ("csTrainAide", "Aides"), ("csTransferAide", "Aides"),
        ("csSelectAide", "Aides"), ("csUseAide", "Aides"), ("csAddAideSuit", "Aides"), ("csDelAideSuit", "Aides"), ("csAskAideSuit", "Aides"),
        ("csOpenAideDialog", "Aides"), ("csSaveTrainResult", "Aides"), ("csUpdateAideProp", "Aides"), ("csRecoverArmProp", "Aides"),
        ("csSaveArmProp", "Aides"), ("csFightSoul", "Fight souls"), ("csViewFightSoul", "Fight souls"), ("csViewChangeFightSoul", "Fight souls"),
        ("csStartCFightSoulList", "Fight souls"), ("csSaveCFightSoulList", "Fight souls"), ("csStartChangeFightSoul", "Fight souls"),
        ("csSaveChangeFightSoul", "Fight souls"), ("csViewFSUpQuality", "Fight souls"), ("csOpenFightSoulView", "Fight souls"),
        ("csDownItem", "Fight souls"), ("csUseSoulSkill", "Fight souls"), ("csFreshStartMap", "Fight souls"), ("csSaveStartMap", "Fight souls"),
        ("csJoinLuckSoul", "Luck souls"), ("csSelectLuck", "Luck souls"), ("csUpgradeLuckSoul", "Luck souls"),
        ("csTotem", "Totems"), ("csUseTotem", "Totems"), ("csShowTotemReset", "Totems"), ("csSaveTotemReset", "Totems"),
        ("csAddTotemExp", "Totems"), ("csGrowItem", "Totems"), ("csQualityItem", "Totems"), ("csAddQualityLevel", "Totems"),
        ("csQueryCleanItemList", "Totems"), ("csStartBonTotem", "Totems"), ("csStartCleanToem", "Totems"), ("csOpenTotemBoxView", "Totems"),
        ("csCleanChargePower", "Totems"), ("csArmRebuild", "Equipment rebuild"), ("csRemoveArmRebuildProp", "Equipment rebuild"),
        ("csOpenRankArm", "Rank equipment"), ("csOpenSaveBit", "Rank equipment"), ("csGoWarkProp", "Rank equipment"),
        ("csDoWarkProp", "Rank equipment"), ("csDoLuckWarkProp", "Rank equipment"), ("csSaveWarkProp", "Rank equipment"),
        ("csSplitWarkProp", "Rank equipment"), ("csUseWarkProp", "Rank equipment"), ("csSplitSaveProp", "Rank equipment"),
        ("csOpenWakeArmView", "Rank equipment"), ("csDoUpLevel", "Equipment upgrade"), ("csDoUpQuality", "Equipment upgrade"),
        ("csNewArmUpdate", "Equipment upgrade"), ("csSelectUpdateType", "Equipment upgrade"), ("EnsureSuit", "Equipment upgrade"),
        ("ConcelSuit", "Equipment upgrade"), ("OpenSubAssembly", "Equipment upgrade"), ("SelectAssemblyOrProbability", "Equipment upgrade"),
        ("csEvol", "Equipment evolution"), ("csOpenEvolView", "Equipment evolution"), ("csMakeStar", "Equipment stars"),
        ("csOpenStarView", "Equipment stars"), ("csOpenSaveSkill", "Star saving"), ("csDoSaveSkill", "Star saving"),
        ("csCleanSaveSkill", "Star saving"), ("csRestoreSaveSkill", "Star saving"), ("csOpenSaveSoul", "Star saving"),
        ("csDoSaveSoul", "Star saving"), ("csCleanSaveSoul", "Star saving"), ("csRestoreSaveSoul", "Star saving"),
        ("csForgeFlag", "Fire flags"), ("csRemakeFlag", "Fire flags"), ("csStartForge", "Fire flags"), ("csClearFlagView", "Fire flags"),
        ("csOpenFlagView", "Fire flags"), ("csFuse", "Belief merging"), ("csAuction", "Auctions"), ("csAskAuction", "Auctions"),
        ("csImpawn", "The pawn shop"), ("csQuiz", "Quizzes"), ("csGuess", "Guessing games"), ("csOpenFBall", "Guessing games"),
        ("csGamble", "Guild match betting"), ("csGetConsMReward", "Guild match betting"), ("csViewGamble", "Guild match betting"),
        ("csViewMyGamble", "Guild match betting"), ("csViewConsMatch", "Guild matches"), ("csVConsMatchMList", "Guild matches"),
        ("csConsCopy", "Guild instances"), ("csJoinConsCopy", "Guild instances"), ("csApplyConcopy", "Guild instances"),
        ("csAgreeConCopyApply", "Guild instances"), ("csAskConCopyApplyList", "Guild instances"), ("csApplyConwar", "Guild wars"),
        ("csAskConwarApplyList", "Guild wars"), ("csAgreeApply", "Guild wars"), ("csRefuseApply", "Guild wars"),
        ("csConsCityBattleCommit", "Guild wars"), ("csLeaveCons", "Guild wars"), ("csLeaveCon", "Guild wars"), ("AskConsWarInfo", "Guild wars"),
        ("csMakeConsArm", "Guild weapons"), ("csSplitConsArm", "Guild weapons"), ("csRebindConsArm", "Guild weapons"),
        ("csUpLevelConsArm", "Guild weapons"), ("csViewConsArmUp", "Guild weapons"), ("csAskConsActDetail", "Guild activity"),
        ("csAskConsBuildInfo", "Guild construction"), ("csAskForEntryBattle", "Battles"), ("csAskForLeaveBattle", "Battles"),
        ("csTeamBattle", "Battles"), ("csCreteGiveUp", "Battles"), ("csEuropGiveUp", "Battles"), ("csLeaveHonorWar", "Battles"),
        ("csLeavePaleWar", "Battles"), ("csLeaveSpartaWar", "Battles"), ("csLeaveConstestWar", "Battles"), ("csLeaveMatch", "Battles"),
        ("csChooseCamp", "Battles"), ("csSelectRole", "Instances"), ("csAskCopyInfo", "Instances"), ("csGroupToCopy", "Instances"),
        ("csLeaveCopyList", "Instances"), ("LeaveTower", "The tower"), ("csSmallGameScore", "Mini games"), ("csTurnCard", "Treasure boxes"),
        ("csLookAllCard", "Treasure boxes"), ("csMatch", "Match records"), ("csQueryMatchLay", "Match records"),
        ("csSetMatchLeader", "Match records"), ("scQueryMatchPlayerHonor", "Match records"), ("csAskPlayerInfo", "Match records"),
        ("csConscribeTeam", "Team recruiting"), ("csConsTeamCancel", "Team recruiting"), ("csInvitePlayer", "Team recruiting"),
        ("csAskSkillNameOption", "Team recruiting"), ("csAskEquipInfo", "Booth search"), ("FindBuyItem", "Booth search"),
        ("FindSellItem", "Booth search"), ("FilterByName", "Booth search"), ("csQueryWearInfo", "Equipment viewing"),
        ("csViewOtherArm", "Equipment viewing"), ("csWatchFinding", "Finding records"), ("csSendPoInfo", "Experience points"),
        ("csGetItem", "Experience exchange"), ("csItemInfo", "Experience exchange"), ("csUpHonorListInfo", "Military rank"),
        ("csUpPropetylist", "Military rank"), ("csShowSailorBarWnd", "The sailor bar"), ("csGuessWinShip", "Boat races"),
        ("csSaveNorthAmePlayer", "Statistics"), ("csChangeModel", "Model change"), ("csChangeHatSwitch", "Appearance options"),
        ("csChangeSpecial1", "Appearance options"), ("csPhoneAid", "Phone verification"), ("csAddAutoSkill", "Pet skills"),
        ("csRemoveAutoSkill", "Pet skills"), ("csPetSkillAutoOn", "Pet skills"), ("csForgetPetSkill", "Pet skills"),
        ("csOpenWhistle", "Pet whistles"), ("csShowWhistle", "Pet whistles"), ("csSwitchWhistle", "Pet whistles"),
        ("csPutDownBomb", "Sea bombs"), ("csSendInvite", "Leave words"), ("ReferItem", "Exploits"), ("ROBBT_ID", "Robbery"),
        ("QuerySendCost", "Parcel post"), ("AskBuildCityInfo", "Colonies"), ("SetCityName", "Colonies"), ("AskGoToObj", "Teleporting"),
        ("Contraband", "Contraband"), ("AppointCaptian", "Fleet command"), ("AutoDriveChange", "Auto sailing"),
        ("SetupTorpedo", "Torpedoes"), ("transprop", "Attribute transfer"), ("exchange", "Item exchange"), ("AskToBattle_Yes", "Battles"),
        ("open", "This window"), ("csTakeOffThing", "Equipment slots"), ("ShowNextUnionCons", "Guild unions"),
        ("csAddItemPropPoint", "Item attribute points"), ("csAskForCreteWar", "Battles"), ("csAskForEntryTimes", "Battles"),
        ("csAskForEurop", "Battles"), ("csHonorWarWinner", "Battles"), ("csPaleWarWinner", "Battles"), ("csTeamWarWinner", "Battles"),
        ("csShowHonorWarTable", "Battles"), ("csShowPaleWarTable", "Battles"), ("csShowTeamWarTable", "Battles"),
        ("csShowPKTable", "PK rankings"), ("csImpeach", "Guild impeachment"), ("csRefuseList", "Guild wars"),
        ("csLoadItemMemory", "Item memory"), ("csSaveItemMemory", "Item memory"), ("csOpenArmConsolidate", "Equipment consolidation"),
        ("csOpenExpSystem", "Experience system"), ("csSucMake", "Crafting"),
    ];

    public IEnumerable<string> Names => [.. Quiet, .. Words];

    public bool Execute(ClientSession session, string[] words)
    {
        if (Quiet.Contains(words[0]))
        {
            Log.Debug(session.Tag, $"{session.Name} window notice: {string.Join(' ', words)}");
            return true;
        }
        string? feature = Features.FirstOrDefault(entry => words[0].StartsWith(entry.Prefix, StringComparison.Ordinal)).Feature;
        if (feature == null) return false;
        session.NotAvailable(feature, string.Join(' ', words));
        return true;
    }
}

public sealed class GoToObjCommand : ITextCommand
{
    public IEnumerable<string> Names => ["AskGoToObj"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.AskGoToObj(words);
        return true;
    }
}

public sealed class ParcelCostCommand : ITextCommand
{
    public IEnumerable<string> Names => ["QuerySendCost"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.Send(ChatPackets.Script(0, "SendCost 0"), "(parcel postage)");
        return true;
    }
}

public sealed class FindBoothCommand : ITextCommand
{
    public IEnumerable<string> Names =>
        ["FindSellItem", "FindBuyItem", "FindSellItemNext", "FindSellItemPrev", "FindBuyItemNext", "FindBuyItemPrev", "FilterByName"];

    public bool Execute(ClientSession session, string[] words)
    {
        int page = words.Length > 1 && int.TryParse(words[^1], out int number) ? number : 1;
        switch (words[0])
        {
            case "FindSellItem" or "FindBuyItem":
                session.FindBooth(words[0] == "FindBuyItem", string.Join(' ', words.Skip(1)).Replace('\u0003', ' '), 1);
                return true;
            case "FindSellItemNext" or "FindBuyItemNext":
                session.FindBoothPage(page + 1);
                return true;
            case "FindSellItemPrev" or "FindBuyItemPrev":
                session.FindBoothPage(page - 1);
                return true;
            default:
                session.FindBoothPage(1);
                return true;
        }
    }
}
