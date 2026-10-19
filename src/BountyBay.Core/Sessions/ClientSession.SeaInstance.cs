using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

// The sea instance windows: dlgSeaCopyInfo (HUD button btnMainSeaCoptInfo -> csNoticeShipInfo -> scAllShipInfo) and
// dlgSeaCopyReport (scSeaCopyInfo, sent when the last hostile ship of the instance is sunk), plus the tower window
// dlgTowerCopyInfo (btnMainTowerCoptInfo / btnMainSeaTowerCoptInfo -> csAskCurDetail -> scCurTowerInfo).
public sealed partial class ClientSession
{
    private const int SeaInstancePointsPerLevel = 10;
    private const int SeaInstanceBossFactor = 5;

    private int _seaCopyScene;
    private int _seaCopyPoints;
    private int _seaCopySunk;

    private CopyDefinition? SeaInstanceHere => InInstanceScene && BattleRules.CopyInScene(Scene) is { Sea: true } copy ? copy : null;

    // IconSwitchMsg (scene property): 1 sea instance, 2 land tower, 4 sea tower.
    private int SceneIcons => TowerHere is { } tower ? tower.Sea ? 4 : 2 : SeaInstanceHere != null ? 1 : 0;

    private static int SeaInstancePoints(SeaNpcDefinition ship)
        => Math.Max(1, ship.Level) * SeaInstancePointsPerLevel * (ship.Boss ? SeaInstanceBossFactor : 1);

    // scAllShipInfo <points> <points total> <difficulty> <spawned> <spawns> <NW> <NE> <SW> <SE>: the window shows
    // "points/total", the difficulty, "spawned/spawns" and the hostile ships left in each quarter of the map. The
    // quarter labels are drawn in the order lblNpcNumWN, WS, EN, ES, but the background puts NE top right and SW
    // bottom left, so the second and third numbers go to NE and SW.
    public void SeaInstanceInfo()
    {
        if (SeaInstanceHere is not { } copy)
        {
            SystemMessage("You are not in a sea instance.");
            return;
        }
        List<SeaNpcState> fleet = Sea.InScene(Scene).Where(ship => !ship.Definition.Friendly).ToList();
        int total = fleet.Sum(ship => SeaInstancePoints(ship.Definition));
        int[] quarters = new int[4];
        if (fleet.Count > 0)
        {
            float centreX = fleet.Average(ship => ship.Definition.X);
            float centreZ = fleet.Average(ship => ship.Definition.Z);
            // Map orientation: west is +x and north is -z (Lisbon lies at +x of Athens, London at -z of Dakar).
            foreach (SeaNpcState ship in fleet.Where(ship => !ship.Dead))
                quarters[(ship.X >= centreX ? 0 : 2) + (ship.Z <= centreZ ? 0 : 1)]++;
        }
        StartSeaInstanceRun();
        Send(UiPackets.Custom($"scAllShipInfo {_seaCopyPoints} {total} {copy.Level} 1 1 {quarters[0]} {quarters[2]} {quarters[1]} {quarters[3]}"),
            "(sea instance info)");
    }

    private void StartSeaInstanceRun()
    {
        if (_seaCopyScene == Scene) return;
        _seaCopyScene = Scene;
        _seaCopyPoints = 0;
        _seaCopySunk = 0;
    }

    private void SeaInstanceShipSunk(SeaNpcDefinition ship)
    {
        if (SeaInstanceHere is not { } copy || ship.Friendly) return;
        StartSeaInstanceRun();
        _seaCopyPoints += SeaInstancePoints(ship);
        _seaCopySunk++;
        if (ship.Boss) SeaInstanceRuns.BossKiller[Scene] = Name;
        if (Sea.InScene(Scene).Any(other => !other.Definition.Friendly && !other.Dead)) return;
        SeaInstanceReport(copy);
    }

    // scSeaCopyInfo alone clears the report; "PersonInfo <5 cells>" adds a row; "TeamInfo <1 won|0> <total> <star>".
    private void SeaInstanceReport(CopyDefinition copy)
    {
        List<ClientSession> crew = Context.World.Sessions.Where(other => other.InWorld && other.Scene == Scene).ToList();
        List<ClientSession> ranked = crew.OrderByDescending(other => other._seaCopyScene == Scene ? other._seaCopyPoints : 0).ToList();
        int total = ranked.Sum(other => other._seaCopyScene == Scene ? other._seaCopyPoints : 0);
        string star = SeaInstanceRuns.BossKiller.Remove(Scene, out string? killer) ? killer : "-";
        foreach (ClientSession member in crew)
        {
            member.Send(UiPackets.Custom("scSeaCopyInfo"), "(sea instance report)");
            for (int rank = 0; rank < ranked.Count; rank++)
            {
                ClientSession row = ranked[rank];
                bool counted = row._seaCopyScene == Scene;
                member.Send(UiPackets.Custom($"scSeaCopyInfo PersonInfo {UiPackets.Token(row.Name)} {row.TotalSkillLevel} "
                    + $"{(counted ? row._seaCopySunk : 0)} {(counted ? row._seaCopyPoints : 0)} {rank + 1}"), "(sea instance report row)");
            }
            member.Send(UiPackets.Custom($"scSeaCopyInfo TeamInfo 1 {total} {UiPackets.Token(star)}"), "(sea instance report total)");
            member.SystemMessage($"{copy.Name} cleared! Every enemy ship has been sunk.");
        }
        foreach (ClientSession member in crew) member._seaCopyScene = 0;
        Log.Info(Tag, $"{copy.Name} cleared by {string.Join(", ", crew.Select(member => member.Name))} ({total} points)");
    }

    // scCurTowerInfo <server best floor> <your best floor> <current floor>; the window lists the prizes of the
    // current floor from PrizeTowerCopy.ini (Lvl<n> or SeaLvl<n> by IconSwitchMsg).
    public void TowerInfo()
    {
        if (TowerHere is not { } tower)
        {
            SystemMessage("You are not in the tower.");
            return;
        }
        int Best(ClientSession who) => tower.Sea ? who.Character.TowerSeaMaxFloor : who.Character.TowerMaxFloor;
        int server = Context.World.Sessions.Where(other => other.InWorld).Select(Best).DefaultIfEmpty(0).Max();
        int mine = Best(this);
        Send(UiPackets.Custom($"scCurTowerInfo {Math.Max(server, mine)} {mine} {Math.Max(1, _towerFloor)}"), "(tower info)");
    }
}

public static class SeaInstanceRuns
{
    public static readonly Dictionary<int, string> BossKiller = [];
}
