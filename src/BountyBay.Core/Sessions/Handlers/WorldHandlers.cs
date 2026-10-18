using BountyBay.Core.Commands;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Network;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions.Handlers;

public sealed class MoveHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.Move;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (!session.InWorld || session.StallOpen) return;
        float x = reader.ReadSingle();
        float z = reader.ReadSingle();
        if (float.IsFinite(x) && float.IsFinite(z))
        {
            session.MoveTo(x, z);
            session.OnMoveRequested(x, z);
        }
    }
}

public sealed class InteractHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.Interact;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (!session.InWorld) return;
        int target = (int)reader.Seek(0x16).ReadUInt32();
        if (target == ProtocolConstants.SelfObjectId) return;
        GameData data = session.Context.Data;
        if (session.MeleeTarget is { } engaged && engaged.Id != target) session.StopMelee();
        session.ShowTarget(target);
        if (session.OnSeaObjectClicked(target)) return;
        if (session.OnLinkClicked(target)) return;
        if (session.BboOceanDockClicked(target)) return;

        if (data.Monsters.TryGetValue(target, out MonsterDefinition? monster) && monster.Scene == session.Scene)
        {
            Log.Debug(session.Tag, $"{session.Name} attacks {monster.Name}");
            session.CombatTarget = monster.Id;
            session.Attack(monster);
        }
        else if (data.Npcs.TryGetValue(target, out NpcDefinition? npc) && npc.Scene == session.Scene)
        {
            Log.Info(session.Tag, $"{session.Name} talks to {npc.Name}");
            session.ApproachNpc(npc);
        }
        else if (session.ClickPet(target))
        {
        }
        else if (session.Context.World.Sessions.FirstOrDefault(other => other.ObjectId == target) is { } player)
        {
            if (!session.OnPlayerClicked(player, reader.Seek(0x1e).ReadByte()))
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

public sealed class ServiceChatHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.ServiceChat;

    public void Handle(ClientSession session, PacketReader reader)
    {
        string text = reader.ReadCString();
        if (text.StartsWith("#EXTE#", StringComparison.Ordinal) || text == "BYE")
        {
            Log.Debug(session.Tag, $"{session.Name} client extension message: {text}");
            return;
        }
        Log.Info(session.Tag, $"{session.Name} to GM: {text}");
        session.Send(ChatPackets.ServiceChat("ECHO", text), "(service chat echo)");
        session.Send(ChatPackets.ServiceChat("GM", "Your message has been written to the server log."), "(service chat reply)");
    }
}

public sealed class TextCommandHandler : IMessageHandler
{
    private readonly CommandRouter _commands;
    private readonly int _textOffset;
    private readonly bool _alwaysCommand;

    public TextCommandHandler(ClientMsg id, int textOffset, CommandRouter commands, bool alwaysCommand = false)
    {
        Id = id;
        _textOffset = textOffset;
        _commands = commands;
        _alwaysCommand = alwaysCommand;
    }

    public ClientMsg Id { get; }

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (!session.InWorld) return;
        session.ExchangeTargetId = Id == ClientMsg.HashedCommand ? (int)reader.Seek(0x16).ReadUInt32() : 0;
        string text = reader.Seek(_textOffset).ReadCString();
        if (text.Length == 0) return;

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        if (text.StartsWith('/'))
        {
            Log.Info(session.Tag, $"{session.Name} slash command: {text}");
            _commands.Slash(session, words);
        }
        else if (text.StartsWith('#'))
        {
            session.FollowPath(reader, _textOffset);
        }
        else if (_alwaysCommand || _commands.Knows(words[0]))
        {
            if (!_commands.Execute(session, words))
                Log.Missing(session.Tag, $"command '{(text.Length > 60 ? text[..60] : text)}'", reader.Data);
        }
        else if (session.ChatAllowed("say", ref text))
        {
            Log.Info(session.Tag, $"{session.Name} says: {text}");
            session.Send(ChatPackets.Chat(ProtocolConstants.SelfObjectId, text), "(chat echo)");
            session.ToOthers(ChatPackets.Chat(session.ObjectId, text), $"(chat from {session.Name})");
        }
    }
}

public sealed class SceneLoadedHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.SceneLoaded;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (session.InWorld) session.OnSceneLoaded();
    }
}
