using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private readonly List<SupplyDefinition> _yardTools = [];

    public void BboOpenShipyardTabs()
    {
        if (!_yard.ContainsKey(SeaPackets.YardEquipmentView)) return;
        ShowShipMarket();
        ShowYardTools();
        if (!_yard.ContainsKey(BboSeaPackets.BuyBackView))
        {
            _yard[BboSeaPackets.BuyBackView] = [];
            Send(ViewPackets.Create(BboSeaPackets.BuyBackView, ViewId.ShopSlots), "(shipyard buy-back)");
        }
    }

    public bool BboShipyardSelect(int view, int how, int index)
    {
        if (!_yard.ContainsKey(view)) return false;
        if (view == BboSeaPackets.BuyBackView)
        {
            SystemMessage("The shipwright has nothing of yours to sell back.");
            return true;
        }
        if (how != 0) return false;
        if (view == SeaPackets.YardShipView)
        {
            if (index < 1 || index > _market.Count) return true;
            Send(BboSeaPackets.BuyInfo(view, index, _market[index - 1].Price, 1), "(ship offer)");
            return true;
        }
        if (view == BboSeaPackets.YardToolsView)
        {
            if (index < 1 || index > _yardTools.Count) return true;
            SupplyDefinition tool = _yardTools[index - 1];
            int most = tool.Price > 0 ? Math.Min(99, Money / tool.Price) : 99;
            if (most < 1)
            {
                SystemMessage($"Not enough money: that costs {tool.Price} gold.");
                return true;
            }
            Send(BboSeaPackets.BuyInfo(view, index, tool.Price, most), "(tool offer)");
            return true;
        }
        return false;
    }

    public bool BboShipyardBuy(int view, int index, int quantity)
    {
        if (view != BboSeaPackets.YardToolsView || !_yard.ContainsKey(view)) return false;
        if (index < 1 || index > _yardTools.Count) return true;
        SupplyDefinition tool = _yardTools[index - 1];
        quantity = Math.Clamp(quantity, 1, 99);
        int bought = 0;
        for (int i = 0; i < quantity; i++)
        {
            if (!HasRoomFor(tool.Item))
            {
                SystemMessage("Your bag is full.");
                break;
            }
            if (!PayShipyard(tool.Price)) break;
            ReceiveIntoBag(tool.Item, tool.Photo, 1, "(shipyard tool)");
            bought++;
        }
        if (bought == 0) return true;
        Save();
        Log.Info(Tag, $"{Name} buys {bought} x {tool.Label} for {bought * tool.Price}");
        return true;
    }

    private void ShowYardTools()
    {
        if (_yard.ContainsKey(BboSeaPackets.YardToolsView)) return;
        _yardTools.Clear();
        var shelf = new List<string>();
        _yard[BboSeaPackets.YardToolsView] = shelf;
        Send(ViewPackets.Create(BboSeaPackets.YardToolsView, ViewId.ShopSlots), "(shipyard tools)");
        foreach (SupplyDefinition tool in Yard.Supplies.Take(ViewId.ShopSlots))
        {
            _yardTools.Add(tool);
            shelf.Add(tool.Item);
            var item = new PropertyBag
            {
                ["Name"] = tool.Item,
                ["Index"] = shelf.Count,
                ["Amount"] = 1,
                ["Photo"] = tool.Photo,
                ["Kind"] = 0,
                ["ItemType"] = SeaPackets.ItemTypeTool,
            };
            ItemCatalog.Extend(item, tool.Item);
            item["Cost"] = tool.Price;
            item["ShowPrice"] = tool.Price;
            Send(ViewPackets.AddItem(BboSeaPackets.YardToolsView, shelf.Count, SeaPackets.Known(item)), "(shipyard tool)");
        }
    }

    public const int BboHighSeasOption = 390;

    public PropertyBag BboFiringArc()
    {
        EnsureShipState();
        CannonDefinition cannon = ShipCannon;
        return BboSeaPackets.FiringArc(Character.CannonsLeft, Character.CannonsRight, cannon.Range, cannon.FireAngle);
    }

    public void BboSendFiringArc()
    {
        if (!AtSea) return;
        PropertyBag arc = BboFiringArc();
        if (arc.Count > 0) Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, arc), "(firing arc)");
    }

    public void BboSendNpcFiringArc(SeaNpcState ship)
    {
        SeaNpcDefinition definition = ship.Definition;
        CannonDefinition cannon = Sea.CannonOf(definition);
        PropertyBag arc = BboSeaPackets.FiringArc(definition.Cannons, definition.Cannons, cannon.Range, cannon.FireAngle);
        if (arc.Count > 0) Send(ScenePackets.ObjectProperties(definition.Id, arc), $"(sea npc {definition.Name} firing arc)");
    }

    public (DialogueEntry, int, string)? BboHighSeasEntry()
        => BboOceanRouteFromHere() ? (DialogueEntry.Option, BboHighSeasOption, "Sail out to the high seas") : null;

    public bool BboChooseShipOption(int npcId, int option)
    {
        if (option != BboHighSeasOption) return false;
        CloseShipyard();
        return BboSailToOcean();
    }

    public void BboHealSailors() => HealShipSailors();

    private static readonly object OceanGate = new();
    private static List<BboOceanDock>? _oceanDocks;

    public static bool BboOceanScene(int scene) => scene is >= BboSeaPackets.FirstOcean and <= BboSeaPackets.LastOcean;

    public bool BboAtOcean => AtSea && BboOceanScene(Scene);

    public ObjectKind BboOwnShipKind => BboOceanScene(Scene) ? ObjectKind.OceanSelf : ObjectKind.SeaSelf;

    public ObjectKind BboOtherShipKind => BboOceanScene(Scene) ? ObjectKind.OceanPlayer : ObjectKind.SeaPlayer;

    public void BboSendOceanDocks()
    {
        if (!BboOceanScene(Scene)) return;
        BboSendOceanGates();
        foreach (BboOceanDock dock in OceanDocks().Where(entry => entry.Ocean == Scene))
        {
            var properties = new PropertyBag
            {
                ["Name"] = dock.Name,
                ["X"] = dock.X,
                ["Y"] = 0f,
                ["Z"] = dock.Z,
                ["Orient"] = 0f,
            };
            Send(ScenePackets.CreateObject(dock.Id, ObjectKind.Door, BboSeaPackets.OceanDockModel), $"(dock {dock.Name})");
            Send(ScenePackets.ObjectProperties(dock.Id, SeaPackets.Known(properties)), $"(dock {dock.Name} properties)");
            Send(ScenePackets.PlaceObject(dock.Id, dock.X, 0f, dock.Z), $"(dock {dock.Name} position)");
            Send(ScenePackets.ShowObject(dock.Id), $"(show dock {dock.Name})");
        }
    }

    public bool BboOceanDockClicked(int objectId)
    {
        if (!BboOceanScene(Scene) || Mover == null) return false;
        if (BboOceanGateClicked(objectId)) return true;
        BboOceanDock? dock = OceanDocks().FirstOrDefault(entry => entry.Ocean == Scene && entry.Id == objectId);
        if (dock == null) return false;
        SceneLink link = DockLink(dock);
        (float x, float z) = Mover.Now();
        if (link.DistanceTo(x, z) <= link.Radius)
        {
            UseLink(link);
            return true;
        }
        MoveTo(link.X, link.Z);
        WatchArrival(link);
        return true;
    }

    public bool BboComeAshoreFromOcean()
    {
        if (!BboOceanScene(Scene) || Mover == null) return false;
        (float x, float z) = Mover.Now();
        BboOceanDock? dock = OceanDocks().Where(entry => entry.Ocean == Scene)
            .OrderBy(entry => SeaDistance(x, z, entry.X, entry.Z)).FirstOrDefault();
        if (dock == null)
        {
            SystemMessage("There is no harbour on these waters.");
            return true;
        }
        if (SeaDistance(x, z, dock.X, dock.Z) > BboSeaPackets.OceanDockReach)
        {
            SystemMessage($"Sail to the harbour of {dock.City} and click its dock to enter.");
            return true;
        }
        UseLink(DockLink(dock));
        return true;
    }

    public bool BboOceanRouteFromHere()
    {
        return !BboOceanScene(Scene) && HomeDock() != null;
    }

    private BboOceanDock? HomeDock()
    {
        int coast = Scene / 1000 * 1000 + 3;
        return OceanDocks().FirstOrDefault(entry => entry.Coast == Scene) ?? OceanDocks().FirstOrDefault(entry => entry.Coast == coast);
    }

    public bool BboSailToOcean()
    {
        if (SceneLocked()) return true;
        BboOceanDock? dock = HomeDock();
        if (BboOceanScene(Scene) || dock == null || !Context.Data.Scenes.Exists(dock.Ocean))
        {
            SystemMessage("No sea route leads to the high seas from here.");
            return true;
        }
        if (!AtSea)
        {
            if (!ReadyToSail()) return true;
            Character.LastPort = Scene;
        }
        Log.Info(Tag, $"{Name} sails out to the high seas ({dock.Ocean}) off {dock.City}");
        (float outX, float outZ) = ScenePaths.Nearest(Context.Settings, dock.Ocean, dock.X + BboSeaPackets.OceanDockOffset, dock.Z + BboSeaPackets.OceanDockOffset);
        GoTo(dock.Ocean, outX, 0f, outZ);
        return true;
    }

    private SceneLink DockLink(BboOceanDock dock)
    {
        SceneLink? arrival = Links.Links.FirstOrDefault(link => link.Sail && link.TargetScene == dock.Coast);
        System.Numerics.Vector3 spawn = arrival == null ? Context.Data.Scenes.SpawnPoint(dock.Coast) : default;
        return new SceneLink
        {
            Id = dock.Id,
            Scene = dock.Ocean,
            Name = dock.Name,
            X = dock.X,
            Z = dock.Z,
            Radius = BboSeaPackets.OceanDockReach,
            TargetScene = dock.Coast,
            TargetX = arrival?.TargetX ?? spawn.X,
            TargetZ = arrival?.TargetZ ?? spawn.Z,
        };
    }

    private List<BboOceanDock> OceanDocks()
    {
        lock (OceanGate)
        {
            if (_oceanDocks != null) return _oceanDocks;
            var docks = new List<BboOceanDock>();
            try
            {
                LoadOceanDocks(Context.Settings.ResourcePath, docks);
            }
            catch (Exception exception)
            {
                Log.Error(Tag, exception);
            }
            Log.Info(Tag, $"high seas: {docks.Count} harbour dock(s) found");
            return _oceanDocks = docks;
        }
    }

    private static void LoadOceanDocks(string resources, List<BboOceanDock> docks)
    {
        Dictionary<string, string> names = IdNames(Path.Combine(resources, "ui", "lang", "china", "mapped", "namelist.idres"));
        string windows = Path.Combine(resources, "ini", "window");
        string scenes = Path.Combine(resources, "ini", "scenes");
        if (!Directory.Exists(scenes)) return;
        var coasts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(scenes, "*.ini"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int scene) || BboOceanScene(scene) || scene % 1000 != 3) continue;
            string map = Path.Combine(windows, "map", scene + ".ini");
            if (!File.Exists(map)) continue;
            string? key = IniFile.Load(map).Header["name"];
            if (key == null) continue;
            string city = CityOf(names.GetValueOrDefault(key, key).Replace('\u0003', ' ').Trim());
            if (city.Length > 0) coasts.TryAdd(city, scene);
        }
        // Coast scenes that are not numbered xxx003, such as the Pacific towns (San Francisco 1603011, Seward 1603001...):
        // any other sea scene whose map is named after the harbour.
        foreach (string file in Directory.EnumerateFiles(scenes, "*.ini"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int scene) || BboOceanScene(scene) || scene % 1000 == 3) continue;
            string map = Path.Combine(windows, "map", scene + ".ini");
            if (!File.Exists(map)) continue;
            IniFile header = IniFile.Load(map);
            if (header.Header["sea"] is not ("true" or "ture") || header.Header["name"] is not { } key) continue;
            string city = CityOf(names.GetValueOrDefault(key, key).Replace('\u0003', ' ').Trim());
            if (city.Length > 0) coasts.TryAdd(city, scene);
        }
        for (int ocean = BboSeaPackets.FirstOcean; ocean <= BboSeaPackets.LastOcean; ocean++)
        {
            string list = Path.Combine(windows, "map2", ocean + ".ini");
            if (!File.Exists(list)) continue;
            foreach (IniSection section in IniFile.Load(list).Sections)
            {
                string? key = section["name"];
                if (key == null || !TryFloat(section["wx"], out float x) || !TryFloat(section["wz"], out float z)) continue;
                string label = names.GetValueOrDefault(key, key).Replace('\u0003', ' ').Trim();
                if (!coasts.TryGetValue(CityOf(label), out int coast) || docks.Any(dock => dock.Coast == coast)) continue;
                docks.Add(new BboOceanDock(BboSeaPackets.FirstOceanDockId + docks.Count, ocean, CityOf(label), label, x, z, coast));
            }
        }
    }

    private static bool TryFloat(string? text, out float value)
        => float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);

    private static string CityOf(string label)
    {
        foreach (string suffix in new[] { "Offshore", "Port", "Dock" })
        {
            if (label.Length > suffix.Length && label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return label[..^suffix.Length].Trim();
        }
        return label.Trim();
    }

    private static Dictionary<string, string> IdNames(string path)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return names;
        foreach (IniSection section in IniFile.Load(path).Sections)
        {
            string? name = section["name"];
            if (section.Name.Length > 0 && name != null) names.TryAdd(section.Name, name.Trim());
        }
        return names;
    }
}

public sealed record BboOceanDock(int Id, int Ocean, string City, string Name, float X, float Z, int Coast);
