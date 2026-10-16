using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private int _towerFloor;

    public TowerDefinition? TowerHere => Character.BattleReturnScene != 0 && CurrentBattle == null ? BattleRules.TowerInScene(Scene) : null;

    private void TowerEnterWorld()
    {
        if (PropertyTable.Has("MaxFloor")) SetProperty("MaxFloor", Character.TowerMaxFloor);
        if (PropertyTable.Has("SeaMaxFloor")) SetProperty("SeaMaxFloor", Character.TowerSeaMaxFloor);
    }

    public void TowerEnter(bool sea)
    {
        TowerDefinition? tower = BattleRules.Tower(sea);
        if (tower == null || !Context.Data.Scenes.Exists(tower.Scene))
        {
            SystemMessage("The tower is not open on this server.");
            return;
        }
        if (CurrentBattle != null || InInstanceScene || TowerHere != null)
        {
            SystemMessage("You cannot enter the tower from here.");
            return;
        }
        if (tower.DailyEntries > 0)
        {
            BattleEntryCount count = TowerCount(tower);
            if (count.Today >= tower.DailyEntries)
            {
                SystemMessage($"You have used all of today's entries for {tower.Name}.");
                return;
            }
            count.Today++;
            count.ThisWeek++;
        }
        _towerFloor = 1;
        BattleTravelTo(tower.Scene, tower.Spawn, 0f);
        SystemMessage($"You entered {tower.Name}. Floor {_towerFloor}.");
        Log.Info(Tag, $"{Name} entered {tower.Name}");
        Save();
    }

    public void TowerLeave()
    {
        if (TowerHere == null) return;
        _towerFloor = 0;
        if (Hp <= 0) SetHp(MaxHp);
        BattleTravelBack("(tower left)");
    }

    public void TowerSendList()
    {
        foreach ((TableDefinition table, bool sea) in new[] { (BattleTables.TowerLandList, false), (BattleTables.TowerSeaList, true) })
        {
            List<object[]> rows = Context.World.Sessions.Where(other => other.InWorld)
                .Select(other => (other.Name, Floor: sea ? other.Character.TowerSeaMaxFloor : other.Character.TowerMaxFloor))
                .Where(entry => entry.Floor > 0)
                .OrderByDescending(entry => entry.Floor).Take(50)
                .Select((entry, rank) => new object[] { $"{rank + 1}", entry.Name, $"{entry.Floor}" }).ToList();
            Send(TablePackets.Clear(table), $"(clear {table.Name})");
            if (rows.Count > 0) Send(TablePackets.Rows(table, rows), $"({table.Name})");
        }
        Send(BattlePackets.OpenTowerList(), "(tower list)");
    }

    private void TowerMonsterKilled(MonsterDefinition monster)
    {
        TowerDefinition? tower = TowerHere;
        if (tower == null || monster.Scene != Scene || _towerFloor <= 0) return;
        TowerFloor? floor = tower.Floors.FirstOrDefault(entry => entry.Floor == _towerFloor);
        if (tower.Floors.Count > 0 && (floor == null || floor.Boss.Length > 0 && floor.Boss != monster.Name)) return;
        if (floor != null)
        {
            BattleFighter dummy = new(this, 0);
            foreach (BattleReward reward in floor.Prize) GiveBattleReward(reward, dummy);
        }
        if (tower.Sea) Character.TowerSeaMaxFloor = Math.Max(Character.TowerSeaMaxFloor, _towerFloor);
        else Character.TowerMaxFloor = Math.Max(Character.TowerMaxFloor, _towerFloor);
        TowerEnterWorld();
        SystemMessage($"Floor {_towerFloor} of {tower.Name} cleared.");
        _towerFloor++;
        if (tower.Floors.Count > 0 && _towerFloor > tower.Floors.Max(entry => entry.Floor)) SystemMessage($"You conquered {tower.Name}!");
        Save();
    }

    private BattleEntryCount TowerCount(TowerDefinition tower)
    {
        string key = tower.Sea ? "tower/sea" : "tower/land";
        if (!Character.CopyEntries.TryGetValue(key, out BattleEntryCount? count)) Character.CopyEntries[key] = count = new BattleEntryCount();
        RollCount(count);
        return count;
    }

    private bool TowerTalk(NpcDefinition npc)
    {
        var entries = new List<(DialogueEntry, int, string)>();
        if (TowerHere is { } inside && npc.Scene == Scene)
        {
            entries.Add((DialogueEntry.Text, 0, $"{inside.Name}, floor {_towerFloor}."));
            entries.Add((DialogueEntry.Option, OptionTowerLeave, "Leave the tower"));
        }
        else if (BattleRules.Towers.Any(tower => tower.NpcKeys.Contains(npc.NameKey)))
        {
            entries.Add((DialogueEntry.Text, 0, "Challenge the tower floor by floor and enter the rankings."));
            foreach (TowerDefinition tower in BattleRules.Towers.Where(tower => tower.NpcKeys.Contains(npc.NameKey)))
                entries.Add((DialogueEntry.Option, tower.Sea ? OptionSeaTowerEnter : OptionTowerEnter, $"Enter {tower.Name}"));
            entries.Add((DialogueEntry.Option, OptionTowerList, "Tower rankings"));
        }
        else
        {
            return false;
        }
        TalkingTo = npc.Id;
        entries.Add((DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"));
        Send(ChatPackets.NpcDialogue(npc.Id, entries), "(tower npc)");
        return true;
    }

    private bool HandleTowerOption(int target, int action)
    {
        switch (action)
        {
            case OptionTowerEnter:
                TowerEnter(false);
                return true;
            case OptionSeaTowerEnter:
                TowerEnter(true);
                return true;
            case OptionTowerList:
                TowerSendList();
                return true;
            case OptionTowerLeave:
                TowerLeave();
                return true;
            default:
                return false;
        }
    }
}
