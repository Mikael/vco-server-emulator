using System.Globalization;
using System.Numerics;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int OptionCopyEnter = 480;
    public const int OptionFlagOccupy = 481;
    public const int OptionGatherResource = 482;
    public const int OptionBattleLeave = 483;
    public const int OptionCopyLeave = 484;
    public const int OptionCopyInfo = 485;
    public const int OptionTowerEnter = 486;
    public const int OptionTowerList = 487;
    public const int OptionSeaTowerEnter = 488;
    public const int OptionTowerLeave = 489;

    private static readonly int[] LimitedBattleTypes = [4, 5, 7, 9];

    private BattleBook BattleRules => BattleBook.For(Context.Settings);

    public BattleRoom? CurrentBattle => BattleDirectory.RoomOf(this);

    public void BattleEnterWorld()
    {
        BattleDirectory.EnsureTicking(Context.World);
        IEnumerable<(int, bool)> open = BattleRules.Battles.Where(battle => battle.ActFlag >= 0)
            .Select(battle => (battle.ActFlag, true)).Distinct();
        Send(BattlePackets.OpenBattle(open), "(open battles)");
        TowerEnterWorld();
        if (Character.BattleReturnScene != 0 && CurrentBattle == null) BattleTravelBack("(back from an old battlefield)");
    }

    public void BattleLeaveWorld()
    {
        BattleDirectory.Forget(this);
        CopyDirectory.Forget(this);
        if (CurrentBattle?.Find(this) is { } fighter) fighter.Left = true;
        if (Character.BattleReturnScene == 0) return;
        Character.Scene = Character.BattleReturnScene;
        Character.Position = Character.BattleReturnPosition;
        Character.BattleReturnScene = 0;
        Character.BattleReturnPosition = null;
    }

    public void BattleAskTimes(int type)
    {
        BattleDefinition? definition = BattleRules.Battle(type);
        if (definition == null) return;
        BattleEntryCount count = BattleCount(type);
        int[] values = LimitedBattleTypes.Contains(type)
            ? [count.Today, definition.DayMax, count.ThisWeek, definition.WeekMax]
            : [definition.DayMax, definition.DayMax == 0 ? 0 : Math.Max(0, definition.DayMax - count.Today)];
        Send(BattlePackets.BattleTimes(type, values), "(battle entry times)");
    }

    public void BattleAskEntry(int type, int level)
    {
        BattleDirectory.EnsureTicking(Context.World);
        BattleDefinition? definition = BattleRules.Battle(type);
        BattleLevelDefinition? tier = definition?.Level(level);
        if (definition == null || tier == null)
        {
            SystemMessage("This battlefield is not open on this server.");
            Log.Debug(Tag, $"{Name} asked for unknown battle {type} level {level}");
            return;
        }
        if (CurrentBattle != null || BattleDirectory.InviteOf(this) != null)
        {
            SystemMessage("You are already taking part in a battle.");
            return;
        }
        if (InInstanceScene || TowerHere != null)
        {
            SystemMessage("You cannot register for a battlefield inside an instance.");
            return;
        }
        List<ClientSession> party = [this];
        if (Team is { } team && team.Members.Count > 1)
        {
            if (definition.NoTeam)
            {
                SystemMessage("You cannot enter this battlefield while you are in a team.");
                return;
            }
            if (definition.TeamEntry || definition.MaxTeam > 0)
            {
                if (team.Leader != this)
                {
                    SystemMessage("Only the team leader may register the team.");
                    return;
                }
                if (definition.MaxTeam > 0 && team.Members.Count > definition.MaxTeam)
                {
                    SystemMessage($"A team may have at most {definition.MaxTeam} members in this battlefield.");
                    return;
                }
                party = team.Members.Where(member => member.InWorld).ToList();
            }
        }
        foreach (ClientSession member in party)
        {
            string? refusal = member.BattleRefusal(definition, tier);
            if (refusal == null) continue;
            SystemMessage(member == this ? refusal : $"{member.Name}: {refusal}");
            return;
        }
        int group = BattleDirectory.NewGroup();
        foreach (ClientSession member in party)
        {
            member.Send(BattlePackets.AlreadyEntered(type, Math.Max(1, definition.MinPlayers - BattleDirectory.Queued(definition, level))), "(battle queue)");
            BattleDirectory.Enqueue(new BattleTicket(member, definition, level, group));
            Log.Info(member.Tag, $"{member.Name} queues for {definition.Name} level {level}");
        }
    }

    private string? BattleRefusal(BattleDefinition definition, BattleLevelDefinition tier)
    {
        BattleEntryCount count = BattleCount(definition.Type);
        if (definition.DayMax > 0 && count.Today >= definition.DayMax) return $"You have used all of today's entries for {definition.Name}.";
        if (definition.WeekMax > 0 && count.ThisWeek >= definition.WeekMax) return $"You have used all of this week's entries for {definition.Name}.";
        if (!BattleRules.EnforceRequirements) return null;
        if (definition.MinSkillTotal > 0 && TotalSkillLevel < definition.MinSkillTotal)
            return $"Your total skill points must be at least {definition.MinSkillTotal}.";
        int land = Properties.Int("MaxLandScore", -1);
        if (land >= 0 && (land < tier.MinLand || tier.MaxLand > 0 && land >= tier.MaxLand))
            return $"Your Land Battle points do not match {tier.Name}.";
        int sea = Properties.Int("MaxSeaScore", -1);
        if (sea >= 0 && (sea < tier.MinSea || tier.MaxSea > 0 && sea >= tier.MaxSea))
            return $"Your Sea Battle points do not match {tier.Name}.";
        return null;
    }

    public void BattleLeaveQueue()
    {
        if (BattleDirectory.Dequeue(this)) Log.Info(Tag, $"{Name} left the battle queue");
        Send(BattlePackets.NotEntered(), "(battle queue left)");
    }

    public void BattleConfirm(bool yes)
    {
        if (yes) BattleDirectory.Accept(this);
        else BattleDirectory.Decline(this);
    }

    public void BattleEnterRoom(BattleRoom room, BattleFighter fighter)
    {
        if (!InWorld) return;
        BattleDefinition definition = room.Definition;
        BattleEntryCount count = BattleCount(definition.Type);
        count.Today++;
        count.ThisWeek++;
        fighter.Entered = true;
        fighter.EnteredAt = Environment.TickCount64;
        if (definition.Mode == "Duel" && Character.ArenaRating == 0) Character.ArenaRating = definition.StartRating;
        BattleTravelTo(definition.Scene, BattleSpawn(definition, fighter.Camp), fighter.Camp == 1 ? -2f : 2f);
        Send(BattlePackets.SceneKind(definition.SceneKind), "(battle scene kind)");
        BattleSetCamp(fighter.Camp);
        if (definition.ChooseCamp) Send(BattlePackets.ChooseCamp(), "(choose camp)");
        SystemMessage($"You entered {definition.Name} on the {(fighter.Camp == 1 ? "red" : "blue")} side.");
        Log.Info(Tag, $"{Name} entered {definition.Name} #{room.Id} camp {fighter.Camp}");
        BattleDirectory.Status(room);
        Save();
    }

    public void BattleChooseCamp(int camp)
    {
        BattleRoom? room = CurrentBattle;
        if (room?.Find(this) is not { } fighter || room.Over || camp is not (1 or 2)) return;
        if (fighter.Camp == camp) return;
        if (room.Camp(camp).Count() >= Math.Max(1, room.Definition.MaxPerCamp))
        {
            SystemMessage("That side is full.");
            return;
        }
        fighter.Camp = camp;
        BattleSetCamp(camp);
        float[]? spawn = BattleSpawn(room.Definition, camp);
        if (spawn != null) TeleportTo(spawn[0], spawn[2]);
    }

    private void BattleSetCamp(int camp)
    {
        if (PropertyTable.Has("Camp")) SetProperty("Camp", camp, visibleToOthers: true);
        SetProperty("TeamNum", camp, visibleToOthers: true);
    }

    public void BattleRelive(BattleRoom room, BattleFighter fighter)
    {
        SetHp(MaxHp);
        float[]? spawn = BattleSpawn(room.Definition, fighter.Camp);
        if (spawn != null) TeleportTo(spawn[0], spawn[2]);
        Send(BattlePackets.StopRelive(ProtocolConstants.SelfObjectId), "(relive)");
        ToOthers(BattlePackets.StopRelive(ObjectId), $"({Name} relives)");
    }

    public bool BattleKnockedOut()
    {
        BattleRoom? room = CurrentBattle;
        if (room?.Find(this) is not { } fighter || !fighter.Present)
            return InstanceKnockedOut();
        fighter.Deaths++;
        SetHp(0);
        if (room.Definition.Mode is "Duel" or "Survivors" or "General" && !room.Over)
        {
            fighter.Alive = false;
            SystemMessage("You have been defeated. Wait for the battle to end or leave the battlefield.");
        }
        else
        {
            int seconds = Math.Max(1, room.Definition.ReliveSeconds);
            fighter.ReliveAt = Environment.TickCount64 + seconds * 1000L;
            Send(BattlePackets.StartRelive(ProtocolConstants.SelfObjectId, seconds), "(relive timer)");
            ToOthers(BattlePackets.StartRelive(ObjectId, seconds), $"({Name} relive timer)");
        }
        Log.Info(Tag, $"{Name} was knocked out in {room.Definition.Name}");
        BattleDirectory.Status(room);
        return true;
    }

    public void BattleMonsterKilled(MonsterDefinition monster)
    {
        TowerMonsterKilled(monster);
        BattleRoom? room = CurrentBattle;
        if (room?.Find(this) is not { } fighter || !fighter.Present || room.Over || monster.Scene != room.Definition.Scene) return;
        int general = room.Definition.Generals.IndexOf(monster.Name);
        if (general >= 0)
        {
            room.FallenGenerals.Add(general + 1);
            fighter.Score += room.Definition.KillPoints * 10;
        }
        fighter.Kills++;
        fighter.Score += room.Definition.KillPoints;
        BattleDirectory.Status(room);
    }

    public void BattleDamageDealt(MonsterDefinition monster, int dealt)
    {
        BattleRoom? room = CurrentBattle;
        if (room?.Find(this) is not { } fighter || !fighter.Present || room.Over || dealt <= 0) return;
        fighter.Damage += dealt;
        if (room.Definition.Generals.Contains(monster.Name)) fighter.BossDamage += dealt;
    }

    public void BattleLeave(string command, int argument)
    {
        BattleRoom? room = CurrentBattle;
        BattleFighter? fighter = room?.Find(this);
        if (room == null || fighter == null || !fighter.Present)
        {
            if (BattleDirectory.Dequeue(this)) Send(BattlePackets.NotEntered(), "(battle queue left)");
            else if (TowerHere != null) TowerLeave();
            else if (InInstanceScene) InstanceLeave();
            else Log.Debug(Tag, $"{Name} {command} {argument} outside a battle");
            return;
        }
        if (argument == -1 && !room.Over && room.Definition.SceneKind is BattleKinds.Honor or BattleKinds.Pale)
        {
            Send(BattlePackets.AskLeave(room.Definition.SceneKind), "(confirm leaving the battle)");
            return;
        }
        BattleExit(room.Over ? "(battle left)" : "(battle deserted)");
    }

    public void BattleExit(string note)
    {
        BattleRoom? room = CurrentBattle;
        if (room?.Find(this) is { } fighter)
        {
            fighter.Left = true;
            fighter.ReliveAt = 0;
        }
        if (Hp <= 0) SetHp(MaxHp);
        if (PropertyTable.Has("Camp")) SetProperty("Camp", 0, visibleToOthers: true);
        SetProperty("TeamNum", Team?.Id ?? 0, visibleToOthers: true);
        BattleTravelBack(note);
        TeamDirectory.Refresh(this);
    }

    public void BattleReward(BattleRoom room, BattleFighter fighter)
    {
        if (fighter.Left || !InWorld) return;
        BattleDefinition definition = room.Definition;
        bool won = room.Winner != 0 && room.Winner == fighter.Camp;
        if (definition.Mode == "Duel") BattleRate(room, fighter, won);
        Character.MatchRecords.Add(new MatchRecordEntry
        {
            Type = definition.Type,
            Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            Won = won,
            Score = fighter.Score,
            Kills = fighter.Kills,
            Rating = Character.ArenaRating,
        });
        if (Character.MatchRecords.Count > 50) Character.MatchRecords.RemoveRange(0, Character.MatchRecords.Count - 50);
        if (won) Character.BattleWins++;
        else Character.BattleLosses++;

        bool earned = fighter.Kills > 0 || fighter.Flags > 0 || fighter.Resources > 0 || fighter.Damage >= Math.Max(1, definition.MinDamage);
        if (definition.Mode == "Survivors" && room.Winner == 0 && room.CampDamage(1) + room.CampDamage(2) == 0) earned = false;
        BattleLevelDefinition? tier = definition.Level(room.Level);
        if (!earned || tier == null)
        {
            SystemMessage("You did not contribute enough to this battle to earn a reward.");
            Save();
            return;
        }
        foreach (BattleReward reward in won ? tier.Win : tier.Lose) GiveBattleReward(reward, fighter);
        List<BattleFighter> entered = room.Fighters.Where(other => other.Entered && !other.Left).ToList();
        foreach (BattleSortPrize prize in definition.SortPrizes)
        {
            Func<BattleFighter, int> measure = prize.Score switch
            {
                "Kills" => other => other.Kills,
                "Flags" => other => other.Flags,
                "Resources" => other => other.Resources,
                _ => other => other.Damage,
            };
            if (measure(fighter) <= 0) continue;
            int rank = 1 + entered.Count(other => measure(other) > measure(fighter));
            if (rank <= prize.Amounts.Count) GiveBattleReward(new BattleReward { Item = prize.Item, Amount = prize.Amounts[rank - 1] }, fighter);
        }
        Save();
    }

    private void GiveBattleReward(BattleReward reward, BattleFighter fighter)
    {
        if (reward.Amount <= 0) return;
        if (reward.Item.Length > 0)
        {
            var stack = new ItemStack { Name = reward.Item, Photo = Context.Data.PhotoOf(reward.Item), Amount = reward.Amount };
            if (GiveItem(stack, "(battle reward)") || WarehouseStore(stack.Clone()))
            {
                fighter.BadgesGained += reward.Amount;
                SystemMessage($"You received {reward.Amount} x {Context.Data.Items.Goods.GetValueOrDefault(reward.Item)?.Label ?? reward.Item}.");
            }
            else
            {
                SystemMessage("Your bags are full: a battle reward was lost.");
            }
        }
        if (reward.Honor.Length == 0) return;
        if (reward.Honor == "Battlefield") AddFlagHonor(reward.Amount);
        else Character.BattleHonor[reward.Honor] = Character.BattleHonor.GetValueOrDefault(reward.Honor) + reward.Amount;
        fighter.HonorGained += reward.Amount;
        SystemMessage($"You gained {reward.Amount} {reward.Honor} Honor.");
    }

    private void BattleRate(BattleRoom room, BattleFighter fighter, bool won)
    {
        BattleFighter? opponent = room.Fighters.FirstOrDefault(other => other != fighter && other.Camp != fighter.Camp);
        int start = Math.Max(1, room.Definition.StartRating);
        int mine = fighter.Rating == 0 ? start : fighter.Rating;
        int theirs = opponent == null || opponent.Rating == 0 ? start : opponent.Rating;
        if (opponent == null || !opponent.Entered)
        {
            Character.ArenaRating = mine + (won ? 5 : 0);
            return;
        }
        if (!fighter.Entered)
        {
            Character.ArenaRating = Math.Max(0, mine - 5);
            return;
        }
        double expected = 1.0 / (1.0 + Math.Pow(10, (theirs - mine) / 400.0));
        int delta = Math.Clamp((int)Math.Round(32 * ((won ? 1.0 : room.Winner == 0 ? 0.5 : 0.0) - expected)), -32, 32);
        if (delta == 0 && room.Winner != 0) delta = won ? 1 : -1;
        Character.ArenaRating = Math.Max(0, mine + delta);
    }

    public bool BattleTalk(NpcDefinition npc)
    {
        if (TowerTalk(npc) || InstanceTalk(npc)) return true;
        BattleRoom? room = CurrentBattle;
        if (room?.Find(this) is not { } fighter || !fighter.Present || npc.Scene != room.Definition.Scene) return false;
        BattleDefinition definition = room.Definition;
        var entries = new List<(DialogueEntry, int, string)>();
        if (definition.FlagKeys.Contains(npc.NameKey))
        {
            int owner = room.FlagOwners.GetValueOrDefault(npc.Id);
            entries.Add((DialogueEntry.Text, 0, $"{npc.Name}: {(owner == 0 ? "neutral" : owner == 1 ? "held by the red side" : "held by the blue side")}."));
            if (!room.Over && owner != fighter.Camp) entries.Add((DialogueEntry.Option, OptionFlagOccupy, "Occupy the flag"));
        }
        else if (definition.ResourceKeys.Contains(npc.NameKey))
        {
            entries.Add((DialogueEntry.Text, 0, $"{npc.Name}. Gather resources and bring them to your side."));
            if (!room.Over) entries.Add((DialogueEntry.Option, OptionGatherResource, "Gather resources"));
        }
        else if (npc.NameKey == "IDName00004948")
        {
            entries.Add((DialogueEntry.Text, 0, "May God protect you on the battlefield."));
            entries.Add((DialogueEntry.Option, OptionBattleLeave, "Leave the battlefield"));
        }
        else
        {
            return false;
        }
        TalkingTo = npc.Id;
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(npc.Id, entries), "(battle npc)");
        return true;
    }

    public bool HandleBattleOption(int target, int action)
    {
        if (action is < OptionCopyEnter or > 499) return false;
        if (HandleInstanceOption(target, action) || HandleTowerOption(target, action)) return true;
        BattleRoom? room = CurrentBattle;
        BattleFighter? fighter = room?.Find(this);
        if (room == null || fighter == null || !fighter.Present) return action is OptionFlagOccupy or OptionGatherResource or OptionBattleLeave;
        switch (action)
        {
            case OptionFlagOccupy when !room.Over:
                if (room.FlagOwners.GetValueOrDefault(target) == fighter.Camp) return true;
                room.FlagOwners[target] = fighter.Camp;
                fighter.Flags++;
                fighter.Score += room.Definition.FlagPoints;
                byte[] taken = BattlePackets.BattleText($"{Name} occupied a flag for the {(fighter.Camp == 1 ? "red" : "blue")} side.", lower: true);
                foreach (BattleFighter other in room.Fighters.Where(other => other.Present)) other.Session.Send(taken, "(flag occupied)");
                BattleDirectory.Status(room);
                return true;
            case OptionGatherResource when !room.Over:
                long now = Environment.TickCount64;
                if (now < fighter.NextResourceAt)
                {
                    SystemMessage($"You can gather again in {(fighter.NextResourceAt - now + 999) / 1000} seconds.");
                    return true;
                }
                fighter.NextResourceAt = now + 30_000;
                int amount = Math.Max(1, room.Definition.ResourcePoints);
                fighter.Resources += amount;
                fighter.Score += amount;
                SystemMessage($"You submitted {amount} resources.");
                BattleDirectory.Status(room);
                return true;
            case OptionBattleLeave:
                BattleExit(room.Over ? "(battle left)" : "(battle deserted)");
                return true;
            default:
                return true;
        }
    }

    private BattleEntryCount BattleCount(int type)
    {
        if (!Character.BattleEntries.TryGetValue(type, out BattleEntryCount? count)) Character.BattleEntries[type] = count = new BattleEntryCount();
        RollCount(count);
        return count;
    }

    private static void RollCount(BattleEntryCount count)
    {
        DateTime today = DateTime.Now;
        string day = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string week = $"{ISOWeek.GetYear(today)}-{ISOWeek.GetWeekOfYear(today)}";
        if (count.Day != day)
        {
            count.Day = day;
            count.Today = 0;
        }
        if (count.Week != week)
        {
            count.Week = week;
            count.ThisWeek = 0;
        }
    }

    private static float[]? BattleSpawn(BattleDefinition definition, int camp)
        => definition.Spawns.Count >= camp && camp >= 1 ? definition.Spawns[camp - 1] : definition.Spawns.FirstOrDefault();

    private void BattleTravelTo(int scene, float[]? spawn, float offset)
    {
        if (Character.BattleReturnScene == 0 && Mover != null)
        {
            (float x, float z) = Mover.Now();
            Character.BattleReturnScene = Scene;
            Character.BattleReturnPosition = [MathF.Round(x, 2), MathF.Round(Height, 2), MathF.Round(z, 2)];
        }
        Vector3 at = spawn is { Length: >= 3 }
            ? new Vector3(spawn[0] + offset, spawn[1], spawn[2])
            : Context.Data.Scenes.SpawnPoint(scene) + new Vector3(offset, 0f, 0f);
        ForceLand = false;
        Send(ScenePackets.LeaveScene(), "(leave scene)");
        EnterScene(scene, at);
    }

    private void BattleTravelBack(string note)
    {
        int scene = Character.BattleReturnScene;
        float[]? saved = Character.BattleReturnPosition;
        Character.BattleReturnScene = 0;
        Character.BattleReturnPosition = null;
        Vector3? at = saved is { Length: 3 } ? new Vector3(saved[0], saved[1], saved[2]) : null;
        if (scene == 0 || !Context.Data.Scenes.Exists(scene))
        {
            scene = WorldSettings.StartScene;
            at = null;
        }
        ForceLand = false;
        Send(ScenePackets.LeaveScene(), note);
        EnterScene(scene, at);
        Save();
    }
}
