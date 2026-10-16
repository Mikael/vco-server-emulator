using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int LeisureFirstOption = 540;
    public const int LeisureLastOption = 559;
    public const int ModelChangeOption = 540;
    public const int MakeExploitOption = 541;
    public const int CampExchangeOption = 542;
    public const int CampHandInOption = 543;
    public const int CampGuardsOption = 544;
    public const int CampConquerorsOption = 545;
    public const int TreasureCardOption = 546;
    public const int TowerGameOption = 547;
    public const int GuessOlympicOption = 548;
    public const int StatisticsOption = 549;
    public const int MilitaryRankOption = 550;

    public const int AppearanceHideHatBit = 0x4;
    public const int AppearanceHatBitNumber = 3;

    private static string LeisureToday => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private LeisureRules LeisureData => LeisureRules.For(Context.Settings);

    public void ApplyLeisureProperties()
    {
        ApplyMilitaryRankProperties();
        ApplyCampProperties();
        PutBboProperty("special1", Character.Special1);
    }

    public void EnterWorldLeisure()
    {
        ApplyLeisureLook();
        if (Character.LeisurePendingMoney > 0)
        {
            SetMoney(Money + Character.LeisurePendingMoney);
            SystemMessage($"You receive {Character.LeisurePendingMoney:N0} silver coins of winnings from the sailor bar.");
            Character.LeisurePendingMoney = 0;
            Save();
        }
    }

    public bool HandleLeisureOption(int target, int option)
    {
        if (option is < LeisureFirstOption or > LeisureLastOption) return false;
        switch (option)
        {
            case ModelChangeOption:
                OpenModelChange();
                break;
            case MakeExploitOption:
                OpenMakeExploit();
                break;
            case CampExchangeOption:
                OpenCampExchange();
                break;
            case CampHandInOption:
                HandInCampItems();
                break;
            case CampGuardsOption or CampConquerorsOption:
                if (Character.PopularityCamp != CampNone) SystemMessage("You have already joined a camp.", SystemMessageKind.Prompt);
                else SetCamp(option == CampGuardsOption ? CampGuards : CampConquerors);
                break;
            case TreasureCardOption:
                OpenTreasureCards();
                break;
            case TowerGameOption:
                StartTowerGame();
                break;
            case GuessOlympicOption:
                OpenGuessOlympic();
                break;
            case StatisticsOption:
                Send(LeisurePackets.ShowVoteForm(), "(statistics form)");
                break;
            case MilitaryRankOption:
                Send(ChatPackets.NpcDialogue(target,
                [
                    (DialogueEntry.Text, 0, $"Your military rank is level {MilitaryRankLevel} with {Character.MilitaryHonor:N0} honor. "
                        + $"You have {MilitaryTrainingPoints} training point(s) left."),
                    (DialogueEntry.Option, MakeExploitOption, "Produce a Medal of Honor"),
                    (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
                ]), "(military rank dialogue)");
                return true;
            default:
                return false;
        }
        return true;
    }

    public void ChangeHatSwitch(int value) => SetSpecialBit(AppearanceHatBitNumber, value != 0);

    public void ChangeSpecial1(int bit, int value) => SetSpecialBit(bit, value != 0);

    public void SaveStatistics(string[] words)
    {
        string server = words.Length > 1 ? words[1] : "";
        string role = words.Length > 4 ? words[4] : "";
        Log.Info(Tag, $"{Name} statistics form: server {server}, role {role}");
        SystemMessage("Thank you, your information has been recorded.");
    }

    public void SendLeisureTable(int table, IEnumerable<string[]> rows, string note)
    {
        foreach (byte[] packet in LeisurePackets.Rows(table, rows)) Send(packet, note);
    }

    private void SetSpecialBit(int bit, bool on)
    {
        if (bit is < 1 or > 31) return;
        int mask = 1 << (bit - 1);
        Character.Special1 = on ? Character.Special1 | mask : Character.Special1 & ~mask;
        Save();
        if (PropertyTable.Has("special1")) SetProperty("special1", Character.Special1, visibleToOthers: true);
        if (bit == AppearanceHatBitNumber)
        {
            RefreshAppearance();
            ApplyLeisureLook();
        }
        Log.Debug(Tag, $"{Name} special1 bit {bit} -> {on}");
    }
}
