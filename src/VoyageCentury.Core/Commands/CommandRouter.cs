using VoyageCentury.Core.Commands.Commands;
using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands;

public interface ITextCommand
{
    IEnumerable<string> Names { get; }

    bool Execute(ClientSession session, string[] words);
}

public sealed class CommandRouter
{
    private static readonly HashSet<string> ClientCommands =
    [
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
            .Add(new SelectCommand())
            .Add(new SellCommand())
            .Add(new BuyCommand())
            .Add(new RemoveItemCommand())
            .Add(new ShortcutCommands())
            .Add(new SocialCommands())
            .Add(new GroupCommands())
            .Add(new BankCommands())
            .Add(new DoActionCommand())
            .Add(new AdjShipSpeedCommand())
            .Add(new SetTitleCommand())
            .Add(new GetPlayerInfoCommand());

    public bool Knows(string name) => ClientCommands.Contains(name);

    public IEnumerable<string> Implemented => _commands.Keys.Order();

    public IEnumerable<string> NotImplemented => ClientCommands.Where(name => !_commands.ContainsKey(name)).Order();

    public bool Execute(ClientSession session, string[] words)
        => _commands.TryGetValue(words[0], out ITextCommand? command) && command.Execute(session, words);

    public void Slash(ClientSession session, string[] words) => _slash.Execute(session, words);
}
