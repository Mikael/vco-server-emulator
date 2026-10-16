using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ColonyCommands : ITextCommand
{
    public IEnumerable<string> Names =>
    [
        "ChooseCityStyle", "SetCityName", "SetDLMap", "CityInfo_Q", "AskBuildCityInfo", "AskUnionInfo", "JoinUnion",
        "ShowNextUnionCons", "ShowPrevUnionCons",
    ];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "ChooseCityStyle" when BboArguments.Last(words, out int style):
                ColonyDirectory.ChooseStyle(session, style);
                return true;
            case "SetCityName":
                ColonyDirectory.SetName(session, BboArguments.Rest(words, 1).Trim());
                return true;
            case "SetDLMap" when BboArguments.Last(words, out int index):
                ColonyDirectory.Develop(session, index);
                return true;
            case "CityInfo_Q":
                ColonyDirectory.CityInfo(session);
                return true;
            case "AskBuildCityInfo":
                ColonyDirectory.BuildInfo(session);
                return true;
            case "AskUnionInfo":
                ColonyDirectory.UnionInfo(session, words.Length > 1 ? words[1] : "");
                return true;
            case "JoinUnion":
                ColonyDirectory.JoinUnion(session, words.Length > 1 ? words[1] : "");
                return true;
            case "ShowNextUnionCons" or "ShowPrevUnionCons":
                ColonyDirectory.UnionPage(session, words.Length > 1 ? words[1] : "", words[0] == "ShowNextUnionCons" ? 1 : -1);
                return true;
            default:
                return false;
        }
    }
}
