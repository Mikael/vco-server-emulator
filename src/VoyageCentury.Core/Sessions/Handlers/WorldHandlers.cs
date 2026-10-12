using VoyageCentury.Core.Commands;
using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Network;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions.Handlers;

public sealed class MoveHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.Move;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (!session.InWorld) return;
        float x = reader.ReadSingle();
        float z = reader.ReadSingle();
        if (float.IsFinite(x) && float.IsFinite(z)) session.MoveTo(x, z);
    }
}

public sealed class InteractHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.Interact;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (!session.InWorld) return;
        int target = (int)reader.Seek(0x16).ReadUInt32();
        GameData data = session.Context.Data;

        if (data.Monsters.TryGetValue(target, out MonsterDefinition? monster) && monster.Scene == session.Scene)
        {
            Log.Debug(session.Tag, $"{session.Name} attacks {monster.Name}");
            session.Attack(monster);
        }
        else if (data.Npcs.TryGetValue(target, out NpcDefinition? npc) && npc.Scene == session.Scene)
        {
            Log.Info(session.Tag, $"{session.Name} talks to {npc.Name}");
            session.Talk(npc);
        }
        else if (session.Context.World.Sessions.Any(other => other.ObjectId == target))
        {
            Log.Debug(session.Tag, $"{session.Name} clicked player object {target}");
        }
        else
        {
            Log.Missing(session.Tag, $"interact with unknown object {target}", reader.Data);
        }
    }
}

public sealed class DialogueOptionHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.DialogueOption;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (!session.InWorld) return;
        int target = (int)reader.ReadUInt32();
        reader.ReadUInt32();
        int option = reader.ReadUInt16();
        Log.Debug(session.Tag, $"{session.Name} picks dialogue option {option} of object {target}");
        session.ChooseOption(target, (DialogueAction)option);
    }
}

public sealed class TextCommandHandler : IMessageHandler
{
    private readonly CommandRouter _commands;
    private readonly int _textOffset;

    public TextCommandHandler(ClientMsg id, int textOffset, CommandRouter commands)
    {
        Id = id;
        _textOffset = textOffset;
        _commands = commands;
    }

    public ClientMsg Id { get; }

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (!session.InWorld) return;
        string text = reader.Seek(_textOffset).ReadCString();
        if (text.Length == 0) return;

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        if (_commands.Knows(words[0]))
        {
            if (!_commands.Execute(session, words))
                Log.Missing(session.Tag, $"command '{text}'", reader.Data);
        }
        else if (text.StartsWith('/'))
        {
            Log.Info(session.Tag, $"{session.Name} slash command: {text}");
            _commands.Slash(session, words);
        }
        else
        {
            Log.Info(session.Tag, $"{session.Name} says: {text}");
            session.Send(ChatPackets.Chat(ProtocolConstants.SelfObjectId, text), "(chat echo)");
            session.ToOthers(ChatPackets.Chat(session.ObjectId, text), $"(chat from {session.Name})");
        }
    }
}
