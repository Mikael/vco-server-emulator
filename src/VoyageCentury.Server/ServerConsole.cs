using VoyageCentury.Core.Commands;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;
using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Server;

public sealed class ServerConsole
{
    private const string Tag = "Console";

    private readonly ServerContext _context;
    private readonly BotDirector _bots;
    private readonly CommandRouter _commands;
    private readonly CancellationTokenSource _stopping;

    public ServerConsole(ServerContext context, BotDirector bots, CommandRouter commands, CancellationTokenSource stopping)
    {
        _context = context;
        _bots = bots;
        _commands = commands;
        _stopping = stopping;
    }

    public void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "console" };
        thread.Start();
    }

    private void Run()
    {
        Log.Info(Tag, "Type 'help' for commands.");
        while (!_stopping.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = Console.ReadLine();
            }
            catch (IOException)
            {
                return;
            }
            if (line == null) return;
            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            try
            {
                Execute(words, line);
            }
            catch (Exception exception)
            {
                Log.Error(Tag, exception);
            }
        }
    }

    private void Execute(string[] words, string line)
    {
        switch (words[0].ToLowerInvariant())
        {
            case "help":
                Log.Info(Tag, "players | bots <count> | say <text> | missing | commands | packets on|off | save | quit");
                break;
            case "players":
                lock (_context.World.Sync)
                {
                    Log.Info(Tag, $"{_context.World.Sessions.Count} in the world");
                    foreach (ClientSession session in _context.World.Sessions)
                    {
                        (float x, float z) = session.Mover!.Now();
                        Log.Info(Tag, $"  {session.Name,-16} object {session.ObjectId} scene {session.Scene} at {x:F1}, {z:F1}{(session.AtSea ? " (at sea)" : "")}");
                    }
                }
                break;
            case "bots" when words.Length == 2 && int.TryParse(words[1], out int count) && count is >= 0 and <= 200:
                _bots.SetCount(count, _stopping.Token);
                Log.Info(Tag, $"{count} bot(s)");
                break;
            case "say" when words.Length > 1:
                _context.World.Broadcast(ChatPackets.SystemMessage(line[(line.IndexOf(' ') + 1)..], SystemMessageKind.System), "(broadcast)");
                break;
            case "missing":
                var missing = Log.MissingSummary();
                Log.Info(Tag, missing.Count == 0 ? "Nothing unhandled so far." : $"{missing.Count} unhandled thing(s):");
                foreach ((string what, int seen) in missing) Log.Info(Tag, $"  {seen,5} x {what}");
                break;
            case "commands":
                Log.Info(Tag, "Implemented: " + string.Join(' ', _commands.Implemented));
                Log.Info(Tag, "Not implemented: " + string.Join(' ', _commands.NotImplemented));
                break;
            case "packets" when words.Length == 2:
                Log.PacketsToConsole = words[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                Log.Info(Tag, $"Packets to console: {(Log.PacketsToConsole ? "on" : "off")}");
                break;
            case "save":
                _context.Accounts.Save();
                Log.Info(Tag, "Saved.");
                break;
            case "quit" or "exit" or "stop":
                _stopping.Cancel();
                break;
            default:
                Log.Info(Tag, "Unknown command. Type 'help'.");
                break;
        }
    }
}
