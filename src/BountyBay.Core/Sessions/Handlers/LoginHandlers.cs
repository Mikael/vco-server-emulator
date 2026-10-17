using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Network;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions.Handlers;

public sealed class LoginHandler : IMessageHandler
{
    public LoginHandler(ClientMsg id = ClientMsg.LoginAnyway) => Id = id;

    public ClientMsg Id { get; }

    public void Handle(ClientSession session, PacketReader reader)
    {
        byte[] data = reader.Data;
        if (data.Length < 0x42)
        {
            session.Send(LoginPackets.Result((byte)LoginResult.WrongAccount), "(login refused: short request)");
            return;
        }
        string account = LoginCipher.DecryptAccount(data.AsSpan(0x02, 32)).Trim().ToLowerInvariant();
        string passwordHash = Id == ClientMsg.LoginFromWeb ? "" : LoginCipher.DecryptPasswordHash(data.AsSpan(0x22, 32));
        if (account.Length == 0 || account.Any(c => c < 0x20 || c > 0x7e))
        {
            Log.Warn(session.Tag, "Login with an unreadable account name");
            session.Send(LoginPackets.Result((byte)LoginResult.WrongAccount), "(login refused: account)");
            return;
        }
        if (!session.Context.Accounts.CheckPassword(account, passwordHash))
        {
            Log.Info(session.Tag, $"Login refused for account {account}: wrong password");
            session.Send(LoginPackets.Result((byte)LoginResult.WrongAccount), "(login refused: password)");
            return;
        }
        session.Account = account;
        List<CharacterRecord> characters = session.Context.Accounts.Characters(session.Account);
        if (characters.Count(character => CreateCharacterHandler.RepairBody(character, session.Context.Data.Creation)) > 0) session.Context.Accounts.Save();
        Log.Info(session.Tag, $"Login, account {account}, {characters.Count} character(s)");
        session.Send(LoginPackets.CharacterList(characters), "(login ok, character list)");
    }
}

public sealed class CreateInfoHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.RequestCreateInfo;

    public void Handle(ClientSession session, PacketReader reader)
        => session.Send(LoginPackets.CreateInfo(session.Context.Data.Creation), "(character creation tables)");
}

public sealed class CreateCharacterHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.CreateCharacter;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (session.Account.Length == 0 || session.InWorld) return;
        CreationData creation = session.Context.Data.Creation;

        string name = reader.Seek(2).ReadFixedString(32);
        byte[] choices = reader.ReadBytes(6);
        int skillCount = reader.ReadByte();
        var skills = new List<SkillEntry>();
        for (int i = 0; i < skillCount; i++)
            skills.Add(new SkillEntry { Id = reader.ReadByte(), Level = reader.ReadByte() });

        CreationChoice role = Pick(creation.Roles, choices[1]);
        CreationChoice face = choices[2] < creation.Faces.Count && creation.Faces[choices[2]].Sex == role.Sex ? creation.Faces[choices[2]]
            : creation.Faces.FirstOrDefault(candidate => candidate.Sex == role.Sex) ?? creation.Faces[0];
        CreationChoice body = creation.Bodies.FirstOrDefault(candidate => candidate.Sex == role.Sex) ?? creation.Bodies[0];
        int city = choices[5] < creation.Cities.Count ? creation.Cities[choices[5]] : creation.Cities[0];

        List<CharacterRecord> characters = session.Context.Accounts.Characters(session.Account);
        LoginResult refusal = characters.Count >= ProtocolConstants.CharacterSlots ? LoginResult.CharacterDataError
            : !ValidName(name) ? LoginResult.IllegalName
            : session.Context.Accounts.FindCharacter(name) != null || session.Context.World.FindPlayer(name) != null ? LoginResult.NameInUse
            : LoginResult.None;
        if (refusal != LoginResult.None)
        {
            Log.Info(session.Tag, $"Refused to create character '{name}': {refusal}");
            session.Send(LoginPackets.Result((byte)refusal), $"(create refused: {refusal})");
            return;
        }
        characters.Add(new CharacterRecord
        {
            Name = name,
            Role = role.Id,
            Face = face.Id,
            Body = body.Id,
            Hair = choices[3],
            HairColor = choices[4],
            City = city,
            Skills = skills,
        });
        session.Context.Accounts.Save();
        Log.Info(session.Tag, $"Created character '{name}' (role {role.Id}, face {face.Id}, body {body.Id}, hair colour {choices[4]}, city {city})");
        session.Send(LoginPackets.CharacterList(characters), "(character list after create)");
    }

    public static bool ValidName(string name)
        => name.Length is >= 1 and <= ProtocolConstants.CharacterNameLength
            && name.All(letter => letter == '_' || char.IsAsciiLetterOrDigit(letter));

    public static bool RepairBody(CharacterRecord character, CreationData creation)
    {
        if (creation.ReplacedBodies.TryGetValue(character.Body, out int replacement))
        {
            character.Body = replacement;
            return true;
        }
        CreationChoice? worn = creation.Bodies.FirstOrDefault(candidate => candidate.Id == character.Body);
        if (worn == null || creation.Roles.Any(role => role.Id == character.Role && role.Sex == worn.Sex)) return false;
        CreationChoice? owner = creation.Roles.FirstOrDefault(role => role.Id == character.Role);
        CreationChoice? body = owner == null ? null : creation.Bodies.FirstOrDefault(candidate => candidate.Sex == owner.Sex);
        if (body == null) return false;
        character.Body = body.Id;
        return true;
    }

    private static CreationChoice Pick(List<CreationChoice> table, int index) => index < table.Count ? table[index] : table[0];
}

public sealed class DeleteCharacterHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.DeleteCharacter;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (session.Account.Length == 0 || session.InWorld) return;
        string name = reader.Seek(2).ReadFixedString(32);
        List<CharacterRecord> characters = session.Context.Accounts.Characters(session.Account);
        LoginResult refusal = !characters.Any(character => character.Name == name) ? LoginResult.CharacterDataError
            : session.Context.World.FindPlayer(name) != null ? LoginResult.DeleteProtected
            : LoginResult.None;
        if (refusal != LoginResult.None)
        {
            Log.Info(session.Tag, $"Refused to delete character '{name}': {refusal}");
            session.Send(LoginPackets.Result((byte)refusal), $"(delete refused: {refusal})");
            return;
        }
        characters.RemoveAll(character => character.Name == name);
        session.Context.Accounts.Save();
        Log.Info(session.Tag, $"Deleted character '{name}'");
        session.Send(LoginPackets.CharacterList(characters), "(character list after delete)");
    }
}

public sealed class LeaveCharacterScreenHandler : IMessageHandler
{
    public ClientMsg Id => ClientMsg.LeaveCharacterScreen;

    public void Handle(ClientSession session, PacketReader reader)
        => Log.Info(session.Tag, "Left the character screen");
}

public sealed class EnterWorldHandler : IMessageHandler
{
    public const string BotPrefix = "bot";

    public ClientMsg Id => ClientMsg.EnterWorld;

    public void Handle(ClientSession session, PacketReader reader)
    {
        if (session.InWorld) return;
        string account = reader.Seek(0x12).ReadFixedString(32);
        int slot = reader.Seek(0x32).ReadByte();
        CreationData creation = session.Context.Data.Creation;

        if (account.StartsWith(BotPrefix, StringComparison.Ordinal))
        {
            session.BeginWorld(new CharacterRecord
            {
                Name = account,
                Face = creation.Faces[Math.Min(1, creation.Faces.Count - 1)].Id,
                Body = creation.Bodies[Math.Min(1, creation.Bodies.Count - 1)].Id,
            }, persistent: false);
            return;
        }

        List<CharacterRecord> characters = session.Account.Length > 0 ? session.Context.Accounts.Characters(session.Account) : [];
        if (slot < characters.Count)
        {
            session.BeginWorld(characters[slot], persistent: true);
        }
        else
        {
            Log.Warn(session.Tag, $"Enter world with empty slot {slot} (account '{account}'), using a temporary character");
            session.BeginWorld(new CharacterRecord { Face = creation.Faces[0].Id, Body = creation.Bodies[0].Id }, persistent: false);
        }
    }
}
