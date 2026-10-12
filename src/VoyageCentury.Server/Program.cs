using System.Net;
using VoyageCentury.Core.Commands;
using VoyageCentury.Core.Configuration;
using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Network;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Sessions;
using VoyageCentury.Core.Sessions.Handlers;
using VoyageCentury.Server;

EmulatorSettings settings = EmulatorSettings.Load();
Log.ConsoleLevel = settings.Logging.ConsoleLevel;
Log.PacketsToConsole = settings.Logging.PacketsToConsole || args.Contains("-v");
Log.Open(settings.Resolve(settings.Logging.File));

Log.Info("Server", "Voyage Century Online server emulator");
Log.Info("Server", $"Root {settings.Root}");

if (!Directory.Exists(settings.ResourcePath))
    Log.Warn("Server", $"Client files not found at {settings.ResourcePath}: scene travel and emotes are limited. Set ClientPath in config\\emulator.json.");

GameData data = GameData.Load(settings);
var accounts = new AccountRepository(new JsonAccountBackend(
    settings.Resolve("data\\accounts.json"),
    settings.Resolve("..\\tools\\characters.json")));
var context = new ServerContext(settings, data, accounts);
CommandRouter commands = CommandRouter.CreateDefault();

Log.Info("Server", $"{PropertyTable.Definitions.Count} properties, {data.Npcs.Count} NPCs, {data.Monsters.Count} monsters, " +
                   $"{data.Scenes.SeaSceneCount} sea scenes, {data.Actions.Count} actions, {accounts.AccountCount} account(s)");

MessageRouter listRouter = new MessageRouter()
    .Add(new ServerListHandler())
    .Add(new SilentHandler(ClientMsg.Hello));

MessageRouter gameRouter = new MessageRouter()
    .Add(new LoginHandshakeHandler())
    .Add(new SilentHandler(ClientMsg.Hello))
    .Add(new SilentHandler(ClientMsg.SceneLoaded))
    .Add(new LoginHandler())
    .Add(new CreateInfoHandler())
    .Add(new CreateCharacterHandler())
    .Add(new DeleteCharacterHandler())
    .Add(new LeaveCharacterScreenHandler())
    .Add(new EnterWorldHandler())
    .Add(new MoveHandler())
    .Add(new InteractHandler())
    .Add(new DialogueOptionHandler())
    .Add(new TextCommandHandler(ClientMsg.TextCommand, 0x02, commands))
    .Add(new TextCommandHandler(ClientMsg.HashedCommand, 0x1e, commands));

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};

IPAddress bind = IPAddress.Parse(settings.BindAddress);
var servers = new[]
{
    new GameServer("List", new IPEndPoint(bind, settings.ListPort),
        (connection, token) => new ClientSession(connection, context).RunAsync(listRouter, token)),
    new GameServer("Game", new IPEndPoint(bind, settings.GamePort),
        (connection, token) => new ClientSession(connection, context).RunAsync(gameRouter, token)),
};

var bots = new BotDirector(context);
var console = new ServerConsole(context, bots, commands, stopping);

try
{
    List<Task> running = servers.Select(server => server.RunAsync(stopping.Token)).ToList();
    if (settings.Bots.Enabled) bots.SetCount(settings.Bots.Count, stopping.Token);
    console.Start();
    await Task.WhenAll(running);
}
catch (System.Net.Sockets.SocketException exception)
{
    Log.Error("Server", $"Could not listen: {exception.Message} (is another copy of the server running?)");
    return 1;
}

accounts.Save();
Log.Info("Server", "Stopped.");
return 0;
