using System.Globalization;
using BountyBay.Core.Logging;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class SocialCommands : ITextCommand
{
    public IEnumerable<string> Names => ["AddFriend", "RemoveFriend", "AddBlacklist", "RemoveBlacklist"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 2) return false;
        SortedSet<string> names = words[0].Contains("Friend") ? session.Character.Friends : session.Character.Blacklist;
        if (words[0].StartsWith("Add", StringComparison.Ordinal)) names.Add(words[1]);
        else names.Remove(words[1]);
        session.Save();
        session.SendSocialTables();
        session.SystemMessage($"{words[0]}: {words[1]}");
        return true;
    }
}

public sealed class GroupCommands : ITextCommand
{
    public IEnumerable<string> Names => ["TeamInvite", "JoinCons", "CreateCons", "KickOut", "TeamObj"];

    public bool Execute(ClientSession session, string[] words)
    {
        session.SystemMessage("Teams and guilds are not available on this server yet.");
        return true;
    }
}

public sealed class BankCommands : ITextCommand
{
    public IEnumerable<string> Names => ["DepositMoney", "EncashMoney"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 2 || !int.TryParse(words[1], out int amount) || amount < 0) return false;
        if (words[0] == "DepositMoney") session.Deposit(amount);
        else session.Withdraw(amount);
        return true;
    }
}

public sealed class DoActionCommand : ITextCommand
{
    public IEnumerable<string> Names => ["DoAction"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 2) return false;
        int action = session.Context.Data.Actions.Gesture(words[1]);
        if (action >= 0) session.PlayGesture(action);
        Log.Debug(session.Tag, $"{session.Name} emote {words[1]}");
        return true;
    }
}

public sealed class AdjShipSpeedCommand : ITextCommand
{
    public IEnumerable<string> Names => ["AdjShipSpeed"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 2) return false;
        if (float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fraction) && float.IsFinite(fraction))
        {
            session.SetSail(fraction);
            Log.Debug(session.Tag, $"{session.Name} sail {session.Sail:F2}");
        }
        return true;
    }
}

public sealed class SetTitleCommand : ITextCommand
{
    private const int LongestTitle = 30;

    public IEnumerable<string> Names => ["SetTitle"];

    public bool Execute(ClientSession session, string[] words)
    {
        string title = string.Join(' ', words.Skip(1));
        if (title.Length > LongestTitle) title = title[..LongestTitle];
        session.Character.Title = title;
        session.SetProperty("Title", title, visibleToOthers: true);
        session.Save();
        return true;
    }
}

public sealed class GetPlayerInfoCommand : ITextCommand
{
    public IEnumerable<string> Names => ["GetPlayerInfo"];

    public bool Execute(ClientSession session, string[] words)
    {
        ClientSession? other = words.Length > 1 ? session.Context.World.FindPlayer(words[1]) : session;
        if (other == null) session.SystemMessage($"{words[1]} is not online.");
        else if (other == session) session.SystemMessage($"{session.Name}: total skill level {session.TotalSkillLevel}, {session.Money} silver, reputation {session.Character.Reputation}, rank {session.Character.Rank}");
        else session.SystemMessage($"{other.Name}: total skill level {other.TotalSkillLevel}, in scene {other.Scene}");
        return true;
    }
}

public sealed class ShipCommands : ITextCommand
{
    public IEnumerable<string> Names => ["BuySailor", "FireSailor", "Repair"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words[0] == "Repair")
        {
            session.RepairShip();
            return true;
        }
        if (words.Length != 2 || !int.TryParse(words[1], out int count) || count <= 0) return false;
        if (words[0] == "BuySailor") session.HireSailors(count);
        else session.FireSailors(count);
        return true;
    }
}

public sealed class SendMoneyCommand : ITextCommand
{
    public IEnumerable<string> Names => ["SendMoney"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 3) return false;
        string name = words[1];
        if (!int.TryParse(words[2], out int amount))
        {
            if (!int.TryParse(words[1], out amount)) return false;
            name = words[2];
        }
        ClientSession? receiver = session.Context.World.FindPlayer(name);
        if (receiver == null || receiver == session)
        {
            session.SystemMessage($"{name} is not online.");
        }
        else if (amount <= 0 || amount > session.Money)
        {
            session.SystemMessage("Not enough money.");
        }
        else
        {
            session.SetMoney(session.Money - amount);
            receiver.SetMoney(receiver.Money + amount);
            session.Save();
            receiver.Save();
            session.SystemMessage($"You sent {amount} gold to {receiver.Name}.");
            receiver.SystemMessage($"{session.Name} sent you {amount} gold.");
            Log.Info(session.Tag, $"{session.Name} sends {amount} gold to {receiver.Name}");
        }
        return true;
    }
}

public sealed class UnlayCommand : ITextCommand
{
    public IEnumerable<string> Names => ["Unlay"];

    public bool Execute(ClientSession session, string[] words)
        => words.Length == 2 && int.TryParse(words[1], out int slot) && session.MoveGear(Protocol.ViewId.Equipped, slot);
}

public sealed class ChannelChatCommand : ITextCommand
{
    private static readonly Dictionary<string, string> Channels = new()
    {
        ["c1"] = "Channel 1", ["c2"] = "Channel 2", ["c4"] = "Channel 4", ["c6"] = "Channel 6",
        ["c7"] = "Channel 7", ["c8"] = "Channel 8", ["c9"] = "Channel 9",
    };

    public IEnumerable<string> Names => [.. Channels.Keys, "c5"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 2) return true;
        if (words[0] == "c5")
        {
            if (words.Length < 3) return true;
            string whisper = string.Join(' ', words.Skip(2));
            ClientSession? receiver = session.Context.World.FindPlayer(words[1]);
            if (receiver == null)
            {
                session.SystemMessage($"{words[1]} is not online.");
                return true;
            }
            receiver.SystemMessage($"[Whisper] {session.Name}: {whisper}", Protocol.SystemMessageKind.Prompt);
            session.SystemMessage($"[Whisper to {receiver.Name}] {whisper}", Protocol.SystemMessageKind.Prompt);
            Log.Info(session.Tag, $"{session.Name} whispers to {receiver.Name}: {whisper}");
            return true;
        }
        string text = string.Join(' ', words.Skip(1));
        string line = $"[{Channels[words[0]]}] {session.Name}: {text}";
        foreach (ClientSession other in session.Context.World.Sessions.ToList())
            other.SystemMessage(line, Protocol.SystemMessageKind.Prompt);
        Log.Info(session.Tag, $"{session.Name} on {words[0]}: {text}");
        return true;
    }
}

public sealed class MotionCommands : ITextCommand
{
    public IEnumerable<string> Names => ["3DWALK", "3DRUN", "3DST", "3DCS", "AskSit"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "AskSit":
                session.ToggleSit();
                return true;
            case "3DWALK":
                session.SetWalking(true);
                return true;
            case "3DRUN":
                session.SetWalking(false);
                return true;
            case "3DST" when words.Length == 3
                && float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                && float.IsFinite(x) && float.IsFinite(z):
                session.StopAt(x, z);
                return true;
            case "3DCS":
                return true;
            default:
                return false;
        }
    }
}

public sealed class TakeUpCommand : ITextCommand
{
    public IEnumerable<string> Names => ["TakeUp"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length != 4 || !int.TryParse(words[1], out int view) || !int.TryParse(words[2], out int index)
            || !int.TryParse(words[3], out int count)) return false;
        if (view == Protocol.ViewId.Warehouse)
        {
            session.WarehouseTake(index, count);
            return true;
        }
        if (view != Protocol.ViewId.BagItems || !session.Items.TryGetValue(index, out Persistence.ItemStack? item)) return true;
        Persistence.ItemStack taken = item.Clone();
        taken.Amount = Math.Clamp(count, 1, item.Amount);
        session.SetHand(taken);
        return true;
    }
}

public sealed class WorldActionCommands : ITextCommand
{
    public IEnumerable<string> Names => ["Search", "GoHome", "AutoDrive"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "Search":
                session.PlayAction(session.Context.Data.Actions.IndexOf("216") is >= 0 and int emote ? emote : 0, "(search)");
                session.SystemMessage("You search the area but find nothing.");
                return true;
            case "GoHome":
                if (session.SceneLocked()) return true;
                session.ForceLand = false;
                session.Travel(session.Context.Settings.World.StartScene);
                return true;
            case "AutoDrive":
                session.SetProperty("AutoDrive", words.Length > 1 && words[1] == "1" ? 1 : 0);
                return true;
            default:
                return false;
        }
    }
}

public sealed class SlashCommands
{
    public void Execute(ClientSession session, string[] words, CommandRouter router)
    {
        if (words[0].Equals("/sc", StringComparison.OrdinalIgnoreCase) && words.Length >= 2)
        {
            if (!session.MayUseGmCommands) return;
            if (words[1].StartsWith("ut", StringComparison.Ordinal)) return;
            words = [(words[1].StartsWith('/') ? "" : "/") + words[1], .. words[2..]];
        }
        if (words[0] == "/booth")
        {
            session.ChooseStallStyle(words);
            return;
        }
        if (words[0] is not ("/where" or "/who" or "/getrich" or "/getcons" or "/getteacher") && !session.MayUseGmCommands)
        {
            session.SystemMessage("Only Game Masters can use that command.");
            return;
        }
        if (words[0] == "/gm")
        {
            session.GmCommand(words);
            return;
        }
        if (session.GmTool(words)) return;
        switch (words[0])
        {
            case "/talk" when words.Length == 2 && int.TryParse(words[1], out int npcId)
                && session.Context.Data.Npcs.TryGetValue(npcId, out Data.NpcDefinition? npc):
                if (!session.OnNpcTalked(npc.Id)) session.Talk(npc);
                break;
            case "/opt" when words.Length >= 2 && int.TryParse(words[1], out int option):
                session.ChooseOption(words.Length > 2 && int.TryParse(words[2], out int optionTarget) ? optionTarget : session.TalkingTo,
                    (Protocol.DialogueAction)option);
                break;
            case "/tp" when words.Length == 3
                && float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float tpX)
                && float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float tpZ):
                session.TeleportTo(tpX, tpZ);
                break;
            case "/cmd" when words.Length >= 2:
                if (!router.Execute(session, words[1..])) session.SystemMessage("not handled: " + string.Join(' ', words[1..]));
                break;
            case "/as" when words.Length >= 3:
                ClientSession? puppet = session.Context.World.FindPlayer(words[1]);
                if (puppet == null) session.SystemMessage($"{words[1]} is not online.");
                else
                {
                    puppet.ExchangeTargetId = session.ObjectId;
                    if (!router.Execute(puppet, words[2..]))
                    {
                        string line = string.Join(' ', words[2..]);
                        puppet.Send(Protocol.Packets.ChatPackets.Chat(Protocol.ProtocolConstants.SelfObjectId, line), "(puppet chat)");
                        puppet.ToOthers(Protocol.Packets.ChatPackets.Chat(puppet.ObjectId, line), "(puppet chat)");
                    }
                }
                break;
            case "/bring" when words.Length == 2:
                ClientSession? brought = session.Context.World.FindPlayer(words[1]);
                if (brought == null || brought.Scene != session.Scene) session.SystemMessage($"{words[1]} is not here.");
                else
                {
                    (float bringX, float bringZ) = session.Mover!.Now();
                    brought.TeleportTo(bringX + 2f, bringZ + 2f);
                }
                break;
            case "/go" or "/ship" when words.Length == 2 && int.TryParse(words[1], out int scene):
                if (!session.Context.Data.Scenes.Exists(scene))
                {
                    session.SystemMessage($"no such scene {scene}");
                    return;
                }
                session.ForceLand = words[0] != "/ship";
                session.Travel(scene);
                break;
            case "/sail" or "/land":
                session.SailOff();
                break;
            case "/getrich" or "/getcons" or "/getteacher":
                Rankings.Tell(session, words[0]);
                break;
            case "/where":
                (float x, float z) = session.Mover!.Now();
                session.SystemMessage($"scene {session.Scene} at {x:F1}, {z:F1}");
                break;
            case "/exp" when words.Length == 3 && int.TryParse(words[2], out int experience) && experience > 0:
                session.GainSkillExperience(words[1], experience);
                break;
            case "/item" when words.Length >= 2:
                session.SpawnItem(words[1], words.Length > 2 && int.TryParse(words[2], out int count) ? count : 1);
                break;
            case "/honor" when words.Length == 2 && int.TryParse(words[1], out int honor):
                session.AddMilitaryHonor(honor);
                break;
            case "/rep" when words.Length == 2 && int.TryParse(words[1], out int reputation) && reputation > 0:
                session.GainReputation(reputation);
                break;
            case "/prop" when words.Length >= 3:
                object? value = Protocol.PropertyTable.Parse(words[1], string.Join(' ', words.Skip(2)));
                if (value == null) session.SystemMessage($"unknown property or bad value: {words[1]}");
                else session.SetProperty(words[1], value, visibleToOthers: true);
                break;
            case "/script" when words.Length >= 2:
                session.Send(Protocol.Packets.ChatPackets.Script(Protocol.ProtocolConstants.SelfObjectId,
                    string.Join(' ', words.Skip(1)).Replace("\\n", "\n")), "(script test)");
                break;
            case "/ui" when words.Length >= 2:
                session.Send(Protocol.Packets.UiPackets.Custom(string.Join(' ', words.Skip(1)).Replace("\\n", "\n")), "(ui keyword test)");
                break;
            case "/view" when words.Length >= 2 && int.TryParse(words[1], out int probeView) && probeView is >= 0 and <= 255:
                session.Send(Protocol.Packets.ViewPackets.Create(probeView, 18), "(probe view)");
                session.Send(Protocol.Packets.ViewPackets.AddItem(probeView, 1, ClientSession.ItemProperties(
                    new Persistence.ItemStack { Name = "IDName00003637", Photo = 140101, Amount = 3 }, 1, 10)), "(probe view item)");
                break;
            case "/unview" when words.Length >= 2 && int.TryParse(words[1], out int dropView) && dropView is >= 0 and <= 255 && dropView != Protocol.ViewId.Ship:
                session.Send(Protocol.Packets.ViewPackets.Delete(dropView), "(probe view removed)");
                break;
            case "/attack" when words.Length == 2 && int.TryParse(words[1], out int victimId)
                && session.Context.Data.Monsters.TryGetValue(victimId, out Data.MonsterDefinition? victim) && victim.Scene == session.Scene:
                session.CombatTarget = victim.Id;
                session.Attack(victim);
                break;
            case "/who":
                session.SystemMessage("Online: " + string.Join(", ", session.Context.World.Sessions.Select(other => other.Name)));
                break;
            case "/heal":
                session.SetHp(session.MaxHp);
                break;
            case "/money" when words.Length == 2 && int.TryParse(words[1], out int amount) && amount >= 0:
                session.SetMoney(amount);
                session.Save();
                break;
            case "/vip" when words.Length == 2 && long.TryParse(words[1], out long vip) && vip >= 0:
                session.SetVipScore(vip);
                break;
            case "/points" when words.Length == 2 && int.TryParse(words[1], out int points) && points >= 0:
                session.Character.GamePoints = points;
                session.Save();
                session.SystemMessage($"Remaining Point: {points}");
                break;
            default:
                session.SystemMessage("commands: /go <scene id>, /ship <scene id>, /sail, /land, /where, /who, /talk <npc id>, /opt <n>, /tp <x> <z>, /cmd <command>, /as <player> <command>, /bring <player>, /script <text>, /ui <keyword>, /prop <name> <value>, /heal, /exp <amount>, /money <amount>, /points <amount>");
                break;
        }
    }
}
