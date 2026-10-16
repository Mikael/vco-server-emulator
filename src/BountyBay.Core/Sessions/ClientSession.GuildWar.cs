using BountyBay.Core.Data;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int GuildWarMenuOption = 700;
    public const int GuildWarBidOption = 701;
    public const int GuildWarStatueOption = 702;
    public const int GuildWarSettingsOption = 703;
    public const int ColonyMenuOption = 710;
    public const int ColonyFoundOption = 711;
    public const int ColonyIndustryOption = 712;
    public const int ColonyTradeOption = 713;
    public const int ColonyGatheringOption = 714;
    public const int ColonyMilitaryOption = 715;
    public const int ColonyInfoOption = 716;
    public const int GuildWarFirstOption = 700;
    public const int GuildWarLastOption = 719;

    public string GuildWarCityName { get; set; } = "";

    public int ColonyRouteShown { get; set; } = -1;

    public int ColonyStyleChosen { get; set; } = -1;

    public bool HandleGuildWarOption(int target, int option)
    {
        if (option is < GuildWarFirstOption or > GuildWarLastOption) return false;
        GuildWarCity? city = GuildWarDirectory.CityHere(this);
        switch (option)
        {
            case GuildWarBidOption when city != null:
                GuildWarDirectory.ShowBids(this, city);
                break;
            case GuildWarStatueOption when city != null:
                GuildWarDirectory.ShowStatue(this, city);
                break;
            case GuildWarSettingsOption:
                GuildWarDirectory.ShowCitySettings(this);
                break;
            case ColonyMenuOption:
                ShowColonyMenu(target);
                break;
            case ColonyFoundOption:
                ColonyDirectory.OfferFounding(this, target);
                break;
            case ColonyIndustryOption:
                ColonyDirectory.ShowRoute(this, ColonyRouteType.Industry, false);
                break;
            case ColonyTradeOption:
                ColonyDirectory.ShowRoute(this, ColonyRouteType.Trade, false);
                break;
            case ColonyGatheringOption:
                ColonyDirectory.ShowRoute(this, ColonyRouteType.Gathering, false);
                break;
            case ColonyMilitaryOption:
                ColonyDirectory.ShowRoute(this, ColonyRouteType.Military, false);
                break;
            case ColonyInfoOption:
                ColonyDirectory.CityInfo(this);
                break;
            default:
                ShowGuildWarMenu(target, city);
                break;
        }
        return true;
    }

    public int GuildWarLoginScene(int scene)
    {
        if (scene != GuildWarDirectory.Rules(Context).WarScene) return scene;
        int city = Character.Guild.Length > 0 ? GuildWarDirectory.Owned(Context, Character.Guild).Select(owned => GuildWarData.SceneOf(Context.Settings, owned.Name)).FirstOrDefault() : 0;
        return city != 0 && Context.Data.Scenes.Exists(city) ? city : WorldSettings.StartScene;
    }

    public void LeaveWorldGuildWar()
    {
        GuildWarCityName = "";
        ColonyRouteShown = -1;
        ColonyStyleChosen = -1;
        ImpeachDirectory.Forget(this);
    }

    public void SayGuildWar(int target, string text, int back)
        => Send(ChatPackets.NpcDialogue(target,
        [
            (DialogueEntry.Text, 0, text),
            (DialogueEntry.Option, back, "Back"),
            (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
        ]), "(guild war dialogue)");

    private void ShowGuildWarMenu(int target, GuildWarCity? city)
    {
        var entries = new List<(DialogueEntry, int, string)>();
        if (city == null)
        {
            entries.Add((DialogueEntry.Text, 0, "This city is not contested by the guilds. Siege wars are fought over "
                + string.Join(", ", GuildWarDirectory.Rules(Context).SiegeCities) + "."));
        }
        else
        {
            entries.Add((DialogueEntry.Text, 0, GuildWarDirectory.Describe(Context, city)));
            entries.Add((DialogueEntry.Option, GuildWarBidOption, "Run for the territory supervisory right"));
            entries.Add((DialogueEntry.Option, GuildWarStatueOption, "Visit the statue of the ruling guild"));
        }
        if (GuildWarDirectory.IsGm(this)) entries.Add((DialogueEntry.Option, GuildWarSettingsOption, "Command the Siege War"));
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(guild war dialogue)");
    }

    private void ShowColonyMenu(int target)
    {
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, ColonyDirectory.Describe(this)) };
        Colony? colony = ColonyDirectory.Of(Context, Guild?.Name ?? "");
        if (colony == null)
        {
            entries.Add((DialogueEntry.Option, ColonyFoundOption, "Found a Colonial Port"));
        }
        else
        {
            entries.Add((DialogueEntry.Option, ColonyInfoOption, "Colonial Port display"));
            entries.Add((DialogueEntry.Option, ColonyIndustryOption, "Develop industry"));
            entries.Add((DialogueEntry.Option, ColonyTradeOption, "Develop trade"));
            entries.Add((DialogueEntry.Option, ColonyGatheringOption, "Develop collection"));
            entries.Add((DialogueEntry.Option, ColonyMilitaryOption, "Develop the military"));
        }
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(target, entries), "(colony dialogue)");
    }
}
