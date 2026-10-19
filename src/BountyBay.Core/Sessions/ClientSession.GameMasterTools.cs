using System.Globalization;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public bool GmHidden { get; private set; }

    private int _gmObjects;

    public void Kick(string reason)
    {
        SystemMessage(reason.Length > 0 ? $"You were disconnected by a Game Master: {reason}" : "You were disconnected by a Game Master.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} is kicked{(reason.Length > 0 ? $": {reason}" : "")}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(800);
            _connection.Drop();
        });
    }

    public bool GmTool(string[] words)
    {
        string command = words[0].TrimStart('/').ToLowerInvariant();
        string Rest(int from) => string.Join(' ', words.Skip(from));
        switch (command)
        {
            case "kick" when words.Length >= 2:
                if (GmOnlinePlayer(words[1]) is not { } kicked) return true;
                kicked.Kick(Rest(2));
                SystemMessage($"{kicked.Name} was kicked.");
                return true;
            case "mute" when words.Length >= 3 && int.TryParse(words[2], out int minutes) && minutes > 0:
                if (GmOnlinePlayer(words[1]) is not { } muted) return true;
                muted.Character.MutedUntil = DateTime.Now.AddMinutes(minutes);
                muted.Save();
                muted.SystemMessage($"A Game Master muted you for {minutes} minute{(minutes == 1 ? "" : "s")}{(words.Length > 3 ? $": {Rest(3)}" : ".")}", SystemMessageKind.Prompt);
                SystemMessage($"{muted.Name} is muted for {minutes} minute(s).");
                Log.Info(Tag, $"GM {Name} mutes {muted.Name} for {minutes} minute(s) {Rest(3)}");
                return true;
            case "unmute" when words.Length >= 2:
                if (GmOnlinePlayer(words[1]) is not { } unmuted) return true;
                unmuted.Character.MutedUntil = null;
                unmuted.Save();
                unmuted.SystemMessage("You can talk again.");
                SystemMessage($"{unmuted.Name} can talk again.");
                return true;
            case "ban" when words.Length >= 3:
                string? account = AccountFor(words[1]);
                if (account == null || Context.Accounts.Find(account) is not { } record)
                {
                    SystemMessage($"No account or character called {words[1]}.");
                    return true;
                }
                DateTime until = words[2].Equals("perm", StringComparison.OrdinalIgnoreCase) ? DateTime.MaxValue
                    : int.TryParse(words[2], out int hours) && hours > 0 ? DateTime.Now.AddHours(hours) : DateTime.MinValue;
                if (until == DateTime.MinValue)
                {
                    SystemMessage("Usage: /ban <account or character> <hours|perm> [reason]");
                    return true;
                }
                record.BannedUntil = until;
                record.BanReason = Rest(3);
                Context.Accounts.Save();
                foreach (ClientSession banned in Context.World.Sessions.Where(session => string.Equals(session.Account, account, StringComparison.OrdinalIgnoreCase)).ToList())
                    banned.Kick(record.BanReason.Length > 0 ? $"banned: {record.BanReason}" : "banned");
                SystemMessage($"Account {account} is banned {(until == DateTime.MaxValue ? "for good" : $"until {until:yyyy-MM-dd HH:mm}")}.");
                Log.Info(Tag, $"GM {Name} bans account {account} until {until:yyyy-MM-dd HH:mm} {record.BanReason}");
                return true;
            case "unban" when words.Length >= 2:
                if (AccountFor(words[1]) is not { } freed || Context.Accounts.Find(freed) is not { } freedRecord)
                {
                    SystemMessage($"No account or character called {words[1]}.");
                    return true;
                }
                freedRecord.BannedUntil = null;
                freedRecord.BanReason = "";
                Context.Accounts.Save();
                SystemMessage($"Account {freed} can log in again.");
                Log.Info(Tag, $"GM {Name} unbans account {freed}");
                return true;
            case "notice" or "announce" when words.Length >= 2:
                string notice = Rest(1);
                foreach (ClientSession player in Context.World.Sessions.Where(session => session.InWorld).ToList())
                    player.SystemMessage($"[GM] {notice}", SystemMessageKind.System);
                Log.Info(Tag, $"GM {Name} announces: {notice}");
                return true;
            case "goto" when words.Length >= 2:
                if (GmOnlinePlayer(words[1]) is not { Mover: not null } found) return true;
                (float toX, float toZ) = found.Mover.Now();
                GmMove(this, found.Scene, toX + 1.5f, found.Height, toZ + 1.5f);
                return true;
            case "summon" when words.Length >= 2:
                if (GmOnlinePlayer(words[1]) is not { } summoned || Mover == null) return true;
                (float hereX, float hereZ) = Mover.Now();
                GmMove(summoned, Scene, hereX + 1.5f, Height, hereZ + 1.5f);
                summoned.SystemMessage($"You were summoned by Game Master {Name}.");
                return true;
            case "hide":
                if (GmHidden) return true;
                GmHidden = true;
                ToOthers(ScenePackets.RemoveObject(ObjectId), $"({Name} hides)");
                SystemMessage("You are hidden from other players.");
                return true;
            case "unhide":
                if (!GmHidden) return true;
                GmHidden = false;
                foreach (ClientSession other in Others()) ShowTo(other);
                SystemMessage("Other players can see you again.");
                return true;
            case "info" when words.Length >= 2:
                if (GmOnlinePlayer(words[1]) is not { Mover: not null } shown) return true;
                (float infoX, float infoZ) = shown.Mover.Now();
                SystemMessage($"{shown.Name}: account {shown.Account}, {shown.Tag}, scene {shown.Scene} at {infoX:F1}, {infoZ:F1}, "
                    + $"level {shown.TotalSkillLevel}, {shown.Money:N0} silver, HP {shown.Hp}/{shown.MaxHp}"
                    + $"{(shown.Character.MutedUntil > DateTime.Now ? $", muted until {shown.Character.MutedUntil:HH:mm}" : "")}");
                return true;
            case "give" when words.Length >= 3:
                if (GmOnlinePlayer(words[1]) is not { } receiver) return true;
                int count = words.Length > 3 && int.TryParse(words[3], out int amount) ? Math.Max(1, amount) : 1;
                receiver.SpawnItem(words[2], count);
                SystemMessage($"Gave {count} x {words[2]} to {receiver.Name}.");
                Log.Info(Tag, $"GM {Name} gives {count} x {words[2]} to {receiver.Name}");
                return true;
            case "givemoney" when words.Length >= 3 && long.TryParse(words[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long money):
                if (GmOnlinePlayer(words[1]) is not { } paid) return true;
                paid.SetMoney(Math.Max(0, (long)paid.Money + money));
                paid.Save();
                SystemMessage($"{paid.Name} now has {paid.Money:N0} silver.");
                Log.Info(Tag, $"GM {Name} gives {money} silver to {paid.Name}");
                return true;
            case "healplayer" when words.Length >= 2:
                if (GmOnlinePlayer(words[1]) is not { } healed) return true;
                healed.SetHp(healed.MaxHp);
                SystemMessage($"{healed.Name} is healed.");
                return true;
            case "engage" when words.Length >= 2 && int.TryParse(words[1], out int shipId):
                if (!OnSeaObjectClicked(shipId)) SystemMessage($"No sea ship {shipId} here.");
                return true;
            case "repairship":
                ClientSession fixedShip = words.Length >= 2 && GmOnlinePlayer(words[1]) is { } shipOwner ? shipOwner : this;
                fixedShip.SetShipHull(fixedShip.ShipHullMax);
                fixedShip.SetShipSail(fixedShip.FleetSailMax);
                fixedShip.SetSailors(fixedShip.FleetSailorsMax, 0);
                fixedShip.Save();
                SystemMessage($"{fixedShip.Name}'s ship is repaired and fully crewed.");
                return true;
            case "sinkall":
                List<SeaNpcState> targets = Sea.InScene(Scene).Where(ship => !ship.Dead && !ship.Definition.Friendly).ToList();
                foreach (SeaNpcState ship in targets)
                {
                    Sea.Sink(ship);
                    OnSeaNpcSunk(ship.Definition.Name);
                    SeaInstanceShipSunk(ship.Definition);
                }
                SystemMessage($"{targets.Count} ship(s) sunk.");
                Log.Info(Tag, $"GM {Name} sinks {targets.Count} ship(s) in scene {Scene}");
                return true;
            case "obj" when words.Length >= 2 && int.TryParse(words[1], out int objModel) && Mover != null:
                byte objKind = words.Length >= 3 && byte.TryParse(words[2], out byte k) ? k : (byte)ObjectKind.Goods;
                (float objX, float objZ) = Mover.Now();
                int objId = 0x7f000000 + ++_gmObjects;
                Send(ScenePackets.CreateObject(objId, (ObjectKind)objKind, objModel), "(gm test object)");
                Send(ScenePackets.ObjectProperties(objId, new PropertyBag { ["X"] = objX + 2f, ["Y"] = Height, ["Z"] = objZ, ["Orient"] = 0f }), "(gm test object)");
                Send(ScenePackets.ShowObject(objId), "(gm test object)");
                SystemMessage($"Object {objId} (model {objModel}, kind {objKind}) placed next to you.");
                return true;
            case "ships":
                foreach (SeaNpcState ship in Sea.InScene(Scene).Where(ship => !ship.Gone).Take(20))
                    SystemMessage($"{ship.Definition.Id} {ship.Definition.Name} L{ship.Definition.Level} hull {ship.Hull}/{ship.Definition.Hull} at {ship.X:F0}, {ship.Z:F0}{(ship.Definition.Friendly ? " (friendly)" : "")}");
                return true;
            default:
                return false;
        }
    }

    private ClientSession? GmOnlinePlayer(string name)
    {
        ClientSession? player = Context.World.FindPlayer(name);
        if (player == null || !player.InWorld) SystemMessage($"{name} is not online.");
        return player is { InWorld: true } ? player : null;
    }

    private string? AccountFor(string name)
    {
        string lower = name.ToLowerInvariant();
        if (Context.Accounts.Find(lower) != null) return lower;
        return Context.World.FindPlayer(name)?.Account ?? Context.Accounts.AccountOf(name);
    }

    private static void GmMove(ClientSession who, int scene, float x, float y, float z)
    {
        if (who.Scene == scene && !who.AtSea)
        {
            who.TeleportTo(x, z);
            return;
        }
        who._linkSerial++;
        who.GoTo(scene, x, y, z);
    }
}
