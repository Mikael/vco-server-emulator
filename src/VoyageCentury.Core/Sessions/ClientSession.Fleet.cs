using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public static class FleetOption
{
    public const int First = 240;
    public const int Menu = 240;
    public const int Market = 241;
    public const int Dock = 242;
    public const int Rebuild = 243;
    public const int Dye = 244;
    public const int Supplies = 245;
    public const int FirstSupply = 250;
    public const int LastSupply = 261;
    public const int FirstDye = 262;
    public const int Last = 279;
}

public static class FleetTables
{
    public const byte ShipDyeId = 5;
    public const byte SailDyeId = 6;

    public static readonly TableDefinition ShipDye = new(ShipDyeId, "ShipDye", Columns());

    public static readonly TableDefinition SailDye = new(SailDyeId, "SailDye", Columns());

    public static bool Usable
        => GameTables.All.Concat(ProgressTables.All).Concat(MailTables.All).Concat(CraftingTables.All)
            .All(table => table.Id != ShipDyeId && table.Id != SailDyeId);

    public static TableDefinition[] All => Usable ? new[] { ShipDye, SailDye } : Array.Empty<TableDefinition>();

    private static TableColumn[] Columns() =>
    [
        new("Name", PropertyType.String),
        new("Value", PropertyType.String),
        new("Res", PropertyType.String),
    ];
}

public sealed partial class ClientSession
{
    private const int DockSlots = 8;
    private const int RebuildSlots = 12;
    private const int DyeRows = 12;
    private const int RebuildKinds = 3;

    private readonly List<ShipDefinition> _market = [];
    private readonly List<int> _dock = [];
    private bool _dockOpen;
    private bool _rebuildOpen;
    private int _dyeScene;

    private ShipCatalogData Yard => ShipCatalog.Load(Context.Settings);

    public OwnedShip ActiveShip
    {
        get
        {
            EnsureFleet();
            return Character.Fleet![Character.ActiveShip];
        }
    }

    public ShipDefinition ActiveShipType => ShipTypeOf(ActiveShip.Model);

    public int ActiveShipModel => ActiveShipType.Model;

    public int FleetHullMax => Scaled(ActiveShipType.Hull, Yard.Rules.HullPercentPerLevel, ActiveShip.Level);

    public int FleetSailMax => Scaled(ActiveShipType.Sail, Yard.Rules.SailPercentPerLevel, ActiveShip.Level);

    public int FleetLoadMax => Scaled(ActiveShipType.Load, Yard.Rules.LoadPercentPerLevel, ActiveShip.Level);

    public int FleetSailorsMax => Math.Max(1, ActiveShipType.Sailors);

    public int FleetCannonsPerSide => CannonPorts(ActiveShipType, ActiveShip.Level);

    public float FleetSpeedFactor => Math.Clamp(ActiveShipType.Speed, 0.2f, 3f);

    public void EnsureFleet()
    {
        if (Character.Fleet is { Count: > 0 })
        {
            if (Character.ActiveShip < 0 || Character.ActiveShip >= Character.Fleet.Count) Character.ActiveShip = 0;
            return;
        }
        Character.Fleet = [new OwnedShip { Model = WorldSettings.ShipModel, Port = Yard.Rules.Port }];
        Character.ActiveShip = 0;
    }

    public void ApplyFleetShip(PropertyBag ship)
    {
        OwnedShip owned = ActiveShip;
        ShipDefinition type = ActiveShipType;
        var values = new PropertyBag
        {
            ["Model"] = type.Model,
            ["ShipModel"] = type.Model,
            ["S/Level"] = owned.Level,
            ["S/Color"] = owned.Color,
            ["S/MastFront"] = type.MastFront,
            ["S/MastMiddle"] = type.MastMiddle,
            ["S/MastBack"] = type.MastBack,
        };
        foreach ((string name, object value) in SeaPackets.Known(values)) ship[name] = value;
    }

    public PropertyBag FleetShipDetails()
    {
        EnsureShipState();
        OwnedShip owned = ActiveShip;
        ShipDefinition type = ActiveShipType;
        int sailMax = FleetSailMax;
        return SeaPackets.Known(new PropertyBag
        {
            ["Name"] = ShipName,
            ["S/Name"] = ShipName,
            ["Index"] = 1,
            ["Amount"] = 1,
            ["Model"] = type.Model,
            ["ShipModel"] = type.Model,
            ["Photo"] = type.Photo,
            ["S/Photo"] = type.Photo,
            ["ShipType"] = type.Name,
            ["ShipKind"] = type.Kind,
            ["Level"] = owned.Level,
            ["S/Level"] = owned.Level,
            ["S/Color"] = owned.Color,
            ["S/SailTex"] = Character.SailCloth,
            ["S/Flag"] = 0,
            ["S/MastFront"] = type.MastFront,
            ["S/MastMiddle"] = type.MastMiddle,
            ["S/MastBack"] = type.MastBack,
            ["RatioSailHP"] = 100,
            ["S/HP"] = ShipHull,
            ["S/MaxHP"] = ShipHullMax,
            ["S/SailHP"] = sailMax,
            ["S/MaxSailHP"] = sailMax,
            ["S/NumCannonL"] = Character.CannonsLeft,
            ["S/NumCannonR"] = Character.CannonsRight,
            ["CabinCannonMax"] = 2 * FleetCannonsPerSide,
            ["NumSailor"] = Character.Sailors,
            ["NumSailorMax"] = FleetSailorsMax,
            ["NumSailorBad"] = 0,
            ["S/BumpPower"] = 10 + 2 * owned.Level,
            ["S/SailDrive"] = (int)(10 * FleetSpeedFactor),
            ["S/Sail1Drive"] = (int)(10 * FleetSpeedFactor),
            ["S/OarDrive"] = 5,
            ["S/Load"] = 0,
            ["S/LoadMax"] = FleetLoadMax,
            ["S/RotSpeed"] = type.Turn,
            ["LevelMove"] = 1,
            ["LevelFight"] = 1,
            ["LevelLoad"] = 1,
            ["ExpMove"] = 0,
            ["ExpFight"] = 0,
            ["ExpLoad"] = 0,
            ["ExpMaxMove"] = 100,
            ["ExpMaxFight"] = 100,
            ["ExpMaxLoad"] = 100,
        });
    }

    public void SendFleetShip()
    {
        PropertyBag details = FleetShipDetails();
        details.Remove("Index");
        details.Remove("Amount");
        Send(ViewPackets.UpdateItem(ViewId.Ship, 1, details), "(ship details)");
    }

    public bool OpenFleetMenu(int npcId)
    {
        EnsureFleet();
        TalkingTo = npcId;
        OwnedShip owned = ActiveShip;
        ShipDefinition type = ActiveShipType;
        var entries = new List<(DialogueEntry, int, string)>
        {
            (DialogueEntry.Text, 0, $"Your flagship is the {ShipName}, a {type.Label} of grade {owned.Level}: hull {ShipHull}/{FleetHullMax}, "
                + $"{FleetCannonsPerSide} gun ports a side, room for {FleetSailorsMax} sailors. You own {Character.Fleet!.Count} of {Yard.Rules.FleetMax} ships."),
            (DialogueEntry.Option, FleetOption.Market, "Show me the ships for sale"),
            (DialogueEntry.Option, FleetOption.Dock, "Visit the dock"),
            (DialogueEntry.Option, FleetOption.Rebuild, "Rebuild my ship"),
            (DialogueEntry.Option, FleetOption.Dye, "Paint the hull or dye the sails"),
            (DialogueEntry.Option, FleetOption.Supplies, "Buy dyes and shipbuilding materials"),
            (DialogueEntry.Option, ShipyardOption.Menu, "Back"),
        };
        Send(ChatPackets.NpcDialogue(npcId, entries), "(fleet menu)");
        return true;
    }

    public bool HandleFleetOption(int target, int option)
    {
        if (option is < FleetOption.First or > FleetOption.Last) return false;
        EnsureFleet();
        switch (option)
        {
            case FleetOption.Menu:
                OpenFleetMenu(target);
                break;
            case FleetOption.Market:
                if (!ShipyardOpen) OpenShipyard();
                ShowShipMarket();
                FleetSay(target, "The hulls on the slip are listed under Ship. Click one to buy it.");
                break;
            case FleetOption.Dock:
                OpenDock();
                FleetSay(target, "Pick a ship and take it out to make it your flagship.");
                break;
            case FleetOption.Rebuild:
                OpenRebuild(target);
                break;
            case FleetOption.Dye:
                OpenDye(target);
                break;
            case FleetOption.Supplies:
                ShowSupplies(target);
                break;
            case >= FleetOption.FirstSupply and <= FleetOption.LastSupply:
                BuySupply(option - FleetOption.FirstSupply);
                ShowSupplies(target);
                break;
            case >= FleetOption.FirstDye:
                List<DyeDefinition> dyes = OfferedDyes();
                int choice = option - FleetOption.FirstDye;
                if (choice >= dyes.Count) return false;
                _dyeScene = Scene;
                FleetDye(dyes[choice].Sail, dyes[choice].Key);
                ShowDyeChoices(target);
                break;
            default:
                return false;
        }
        return true;
    }

    public void CloseFleetViews()
    {
        CloseDock();
        CloseRebuild();
        _dyeScene = 0;
    }

    public void ShowShipMarket()
    {
        if (_yard.ContainsKey(SeaPackets.YardShipView)) return;
        EnsureFleet();
        _market.Clear();
        var shelf = new List<string>();
        _yard[SeaPackets.YardShipView] = shelf;
        Send(ViewPackets.Create(SeaPackets.YardShipView, ViewId.ShopSlots), "(shipyard ships)");
        foreach (ShipDefinition type in Yard.Ships.Take(ViewId.ShopSlots))
        {
            _market.Add(type);
            shelf.Add(type.Name);
            Send(ViewPackets.AddItem(SeaPackets.YardShipView, shelf.Count, SeaPackets.Known(new PropertyBag
            {
                ["Name"] = type.Name,
                ["Index"] = shelf.Count,
                ["Amount"] = 1,
                ["Photo"] = type.Photo,
                ["Kind"] = 0,
                ["ItemType"] = SeaPackets.ItemTypeShip,
                ["Cost"] = type.Price,
                ["ShowPrice"] = type.Price,
                ["Level"] = type.Level,
                ["ShipType"] = type.Name,
                ["ShipKind"] = type.Kind,
                ["S/MaxHP"] = type.Hull,
                ["S/MaxSailHP"] = type.Sail,
                ["NumSailorMax"] = type.Sailors,
                ["CabinCannonMax"] = 2 * CannonPorts(type, 0),
                ["S/LoadMax"] = type.Load,
            })), "(shipyard ship)");
        }
    }

    public void BuyShip(int index, bool flagship, string name)
    {
        EnsureFleet();
        if (!_yard.ContainsKey(SeaPackets.YardShipView) || index < 1 || index > _market.Count)
        {
            SystemMessage("Ask a shipwright to show you the ships for sale first.");
            return;
        }
        ShipDefinition type = _market[index - 1];
        List<OwnedShip> fleet = Character.Fleet!;
        if (fleet.Count >= Yard.Rules.FleetMax)
        {
            SystemMessage($"The dock has no room: you already own {fleet.Count} ships.");
            return;
        }
        if (Character.Level < type.Level)
        {
            SystemMessage($"You must reach level {type.Level} to command a {type.Label}.");
            return;
        }
        name = name.Replace('\u0003', ' ').Trim();
        if (name.Length == 0) name = type.Label.Length > ShipNameLimit ? type.Label[..ShipNameLimit] : type.Label;
        if (name.Length > ShipNameLimit || name.Any(letter => letter < ' ' || letter > 0xff))
        {
            SystemMessage($"A ship name must be 1 to {ShipNameLimit} letters long.");
            return;
        }
        if (!PayShipyard(type.Price)) return;

        fleet.Add(new OwnedShip
        {
            Model = type.Model,
            Name = name,
            Port = Yard.Rules.Port,
            SailCloth = 2900000,
            CannonName = Character.CannonName,
        });
        Log.Info(Tag, $"{Name} buys a {type.Label} called {name} for {type.Price}");
        if (flagship && !AtSea)
        {
            SwitchShip(fleet.Count - 1);
            SystemMessage($"The {name} is yours and now your flagship. She carries no cannons yet.");
        }
        else
        {
            SystemMessage($"The {name} is yours. She is waiting in the dock.");
            Save();
        }
        if (_dockOpen) OpenDock();
    }

    public void OpenDock()
    {
        EnsureFleet();
        CloseDock();
        List<OwnedShip> fleet = Character.Fleet!;
        _dockOpen = true;
        _dock.Add(Character.ActiveShip);
        for (int i = 0; i < fleet.Count && _dock.Count < DockSlots; i++)
        {
            if (i != Character.ActiveShip) _dock.Add(i);
        }
        Send(ViewPackets.Create(SeaPackets.DockView, DockSlots), "(dock view)");
        for (int i = 0; i < _dock.Count; i++)
        {
            OwnedShip owned = fleet[_dock[i]];
            ShipDefinition type = ShipTypeOf(owned.Model);
            bool active = _dock[i] == Character.ActiveShip;
            Send(ViewPackets.AddItem(SeaPackets.DockView, i + 1, SeaPackets.Known(new PropertyBag
            {
                ["Index"] = i + 1,
                ["Name"] = active ? ShipName : OwnedName(owned),
                ["Photo"] = type.Photo,
                ["Amount"] = 1,
                ["Pierage"] = 0,
                ["ShipType"] = type.Name,
                ["PortCity"] = owned.Port.Length > 0 ? owned.Port : Yard.Rules.Port,
                ["Level"] = owned.Level,
            })), "(docked ship)");
        }
    }

    public void CloseDock()
    {
        _dock.Clear();
        if (!_dockOpen) return;
        _dockOpen = false;
        Send(ViewPackets.Delete(SeaPackets.DockView), "(close dock)");
    }

    public void ChangeShip(int index)
    {
        EnsureFleet();
        if (!_dockOpen || index < 1 || index > _dock.Count)
        {
            SystemMessage("Visit the dock at a shipyard to change ships.");
            return;
        }
        int chosen = _dock[index - 1];
        if (chosen == Character.ActiveShip)
        {
            SystemMessage("That ship is already your flagship.");
            return;
        }
        if (AtSea)
        {
            SystemMessage("You can only change ships in port.");
            return;
        }
        SwitchShip(chosen);
        ShipDefinition type = ActiveShipType;
        SystemMessage($"The {ShipName} ({type.Label}) is now your flagship.");
        Log.Info(Tag, $"{Name} changes ship to {ShipName} ({type.Model})");
        OpenDock();
    }

    public void OpenRebuild(int npcId)
    {
        EnsureFleet();
        CloseRebuild();
        OwnedShip owned = ActiveShip;
        ShipDefinition type = ActiveShipType;
        if (owned.Level >= Yard.Rules.MaxLevel)
        {
            FleetSay(npcId, $"The {ShipName} has been rebuilt as far as her frames allow.");
            return;
        }
        _rebuildOpen = true;
        int cost = RebuildCost(type, owned.Level);
        string materials = string.Join(' ', Yard.Rules.UpgradeMaterials
            .Where(material => material.Section.Length > 0 && material.Count > 0)
            .Select(material => $"{material.Section},{material.Key},{material.Count * (owned.Level + 1)}"));
        Send(ViewPackets.Create(SeaPackets.RebuildView, RebuildSlots), "(rebuild view)");
        for (int kind = 0; kind < RebuildKinds; kind++)
        {
            Send(ViewPackets.AddItem(SeaPackets.RebuildView, kind + 1, SeaPackets.Known(new PropertyBag
            {
                ["Index"] = kind + 1,
                ["Name"] = type.Name,
                ["Photo"] = type.Photo,
                ["Amount"] = 1,
                ["ShipKind"] = kind,
                ["ShipType"] = type.Name,
                ["Level"] = owned.Level + 1,
                ["ShipRebuildCost"] = cost,
                ["ShipRebuildRes"] = materials,
            })), "(rebuild choice)");
        }
        Send(ChatPackets.Script(0, "ShowRebuildShip"), "(rebuild dialog)");
    }

    public void CloseRebuild()
    {
        if (!_rebuildOpen) return;
        _rebuildOpen = false;
        Send(ViewPackets.Delete(SeaPackets.RebuildView), "(close rebuild)");
    }

    public void QueryRebuildShip(int index)
    {
        if (!_rebuildOpen || index < 1 || index > RebuildKinds) return;
        OwnedShip owned = ActiveShip;
        ShipDefinition type = ActiveShipType;
        FleetRules rules = Yard.Rules;
        int next = owned.Level + 1;
        int hull = Scaled(type.Hull, rules.HullPercentPerLevel, next) - Scaled(type.Hull, rules.HullPercentPerLevel, owned.Level);
        int sail = Scaled(type.Sail, rules.SailPercentPerLevel, next) - Scaled(type.Sail, rules.SailPercentPerLevel, owned.Level);
        int load = Scaled(type.Load, rules.LoadPercentPerLevel, next) - Scaled(type.Load, rules.LoadPercentPerLevel, owned.Level);
        int cannons = 2 * (CannonPorts(type, next) - CannonPorts(type, owned.Level));
        Send(ChatPackets.Script(0, $"RebuildShipEffect Hull {hull} Sail {sail} Cannon {cannons} Load {load}"), "(rebuild effect)");
    }

    public void RebuildShip(int index)
    {
        if (!_rebuildOpen || index < 1 || index > RebuildKinds)
        {
            SystemMessage("Ask a shipwright to look at your ship first.");
            return;
        }
        OwnedShip owned = ActiveShip;
        ShipDefinition type = ActiveShipType;
        FleetRules rules = Yard.Rules;
        if (AtSea)
        {
            RebuildResult(false, "The ship can only be rebuilt in port.");
            return;
        }
        if (owned.Level >= rules.MaxLevel)
        {
            RebuildResult(true, "This ship cannot be rebuilt any further.");
            return;
        }
        int cost = RebuildCost(type, owned.Level);
        if (Money < cost)
        {
            RebuildResult(false, $"Not enough money: the rebuild costs {cost} gold.");
            return;
        }
        List<UpgradeMaterial> materials = rules.UpgradeMaterials.Where(material => material.Item.Length > 0 && material.Count > 0).ToList();
        foreach (UpgradeMaterial material in materials)
        {
            int needed = material.Count * (owned.Level + 1);
            if (BagCount(material.Item) >= needed) continue;
            string label = Yard.FindSupply(material.Item)?.Label ?? material.Item;
            RebuildResult(false, $"You need {needed} x {label} for the rebuild.");
            return;
        }

        foreach (UpgradeMaterial material in materials) TakeItems(material.Item, material.Count * (owned.Level + 1), "(rebuild material)");
        SetMoney(Money - cost);
        owned.Level++;
        SetShipHull(FleetHullMax);
        SendFleetShip();
        Save();
        Log.Info(Tag, $"{Name} rebuilds {ShipName} to grade {owned.Level} for {cost}");
        RebuildResult(true, $"The {ShipName} was rebuilt to grade {owned.Level}.");
        SystemMessage($"The {ShipName} was rebuilt to grade {owned.Level}: hull {FleetHullMax}, {FleetCannonsPerSide} gun ports a side.");
    }

    public void OpenDye(int npcId)
    {
        EnsureFleet();
        _dyeScene = Scene;
        if (!FleetTables.Usable)
        {
            ShowDyeChoices(npcId);
            return;
        }
        SendFleetShip();
        foreach ((TableDefinition table, bool sail) in new[] { (FleetTables.ShipDye, false), (FleetTables.SailDye, true) })
        {
            Send(TablePackets.Clear(table), $"(clear {table.Name})");
            List<object[]> rows = Yard.Dyes.Where(dye => dye.Sail == sail).Take(DyeRows)
                .Select(dye => new object[] { dye.Key, dye.Value.ToString(), dye.Item.Length > 0 ? $"{dye.Item} {dye.ItemCount}" : "" })
                .ToList();
            if (rows.Count > 0) Send(TablePackets.Rows(table, rows), $"({table.Name})");
        }
        Send(SeaPackets.ShowShipDye(), "(dye dialog)");
    }

    public void FleetDye(bool sail, string key)
    {
        EnsureFleet();
        DyeDefinition? dye = Yard.FindDye(sail, key.Replace('\u0003', ' ').Trim());
        if (dye == null)
        {
            SystemMessage(sail ? "The shipwright has no such sail cloth." : "The shipwright has no such paint.");
            return;
        }
        if (_dyeScene != Scene || AtSea)
        {
            SystemMessage("Only a shipwright can do that for you.");
            return;
        }
        OwnedShip owned = ActiveShip;
        if (!sail && !ActiveShipType.Dyeable)
        {
            SystemMessage("Current ship colour cannot be changed.");
            return;
        }
        if (sail ? Character.SailCloth == dye.Value : owned.Color == dye.Value)
        {
            SystemMessage(sail ? "Your ship already carries those sails." : "Your ship already has that colour.");
            return;
        }
        if (dye.Item.Length > 0 && BagCount(dye.Item) < dye.ItemCount)
        {
            string label = Yard.FindSupply(dye.Item)?.Label ?? dye.Item;
            SystemMessage($"You need {dye.ItemCount} x {label} for that.");
            return;
        }
        if (!PayShipyard(dye.Price)) return;
        if (dye.Item.Length > 0) TakeItems(dye.Item, dye.ItemCount, "(dye used)");

        if (sail)
        {
            Character.SailCloth = dye.Value;
            owned.SailCloth = dye.Value;
            Properties["S/SailTex"] = dye.Value;
        }
        else
        {
            owned.Color = dye.Value;
        }
        SendFleetShip();
        string what = dye.Label.Length > 0 ? dye.Label : dye.Key;
        SystemMessage(sail ? $"New sails were bent on: {what}." : $"The hull was painted: {what}.");
        Save();
        Log.Info(Tag, $"{Name} dyes the {(sail ? "sails" : "hull")} of {ShipName}: {dye.Value}");
    }

    public bool SelectAmmunitionSide(bool left, int index)
    {
        EnsureShipState();
        List<AmmunitionDefinition> kinds = Sea.Data.Ammunition;
        if (index < 1 || index > kinds.Count || !HasAmmunition(kinds[index - 1].Name)) return false;
        if (left) Character.AmmunitionLeft = kinds[index - 1].Name;
        else Character.AmmunitionRight = kinds[index - 1].Name;
        SendAmmunitionChoice();
        Save();
        return true;
    }

    private void SwitchShip(int index)
    {
        List<OwnedShip> fleet = Character.Fleet!;
        EnsureShipState();
        OwnedShip old = fleet[Character.ActiveShip];
        old.Name = Character.ShipName;
        old.Hull = Character.ShipHull;
        old.SailCloth = Character.SailCloth;
        old.CannonsLeft = Math.Max(0, Character.CannonsLeft);
        old.CannonsRight = Math.Max(0, Character.CannonsRight);
        old.CannonName = Character.CannonName;

        Character.ActiveShip = index;
        OwnedShip now = fleet[index];
        int ports = FleetCannonsPerSide;
        Character.ShipName = now.Name;
        Character.ShipHull = now.Hull < 0 ? FleetHullMax : Math.Min(now.Hull, FleetHullMax);
        Character.SailCloth = now.SailCloth;
        Character.CannonsLeft = Math.Clamp(now.CannonsLeft, 0, ports);
        Character.CannonsRight = Math.Clamp(now.CannonsRight, 0, ports);
        if (now.CannonName.Length > 0) Character.CannonName = now.CannonName;
        Properties["S/SailTex"] = Character.SailCloth;
        if (Character.Sailors > FleetSailorsMax)
        {
            Character.Sailors = FleetSailorsMax;
            SetProperty("NumSailor", Character.Sailors);
        }
        SetShipCannons(Character.CannonsLeft, Character.CannonsRight);
        SendFleetShip();
        Save();
    }

    private void RebuildResult(bool close, string text)
    {
        Send(ChatPackets.Script(0, $"RebuildShipResult {(close ? 1 : 0)} {SeaPackets.OneWord(text)}"), "(rebuild result)");
        if (close) CloseRebuild();
    }

    private void ShowSupplies(int npcId)
    {
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, $"Dyes, paint and timber, captain. You carry {Money} gold.") };
        List<SupplyDefinition> supplies = Yard.Supplies;
        for (int i = 0; i < supplies.Count && FleetOption.FirstSupply + i <= FleetOption.LastSupply; i++)
        {
            int owned = BagCount(supplies[i].Item);
            entries.Add((DialogueEntry.Option, FleetOption.FirstSupply + i,
                $"{supplies[i].Label} ({supplies[i].Price} gold{(owned > 0 ? $", you have {owned}" : "")})"));
        }
        entries.Add((DialogueEntry.Option, FleetOption.Menu, "Back"));
        Send(ChatPackets.NpcDialogue(npcId, entries), "(shipyard supplies)");
    }

    private void BuySupply(int index)
    {
        List<SupplyDefinition> supplies = Yard.Supplies;
        if (index < 0 || index >= supplies.Count) return;
        SupplyDefinition supply = supplies[index];
        if (!HasRoomFor(supply.Item))
        {
            SystemMessage("Your bag is full.");
            return;
        }
        if (!PayShipyard(supply.Price)) return;
        ReceiveIntoBag(supply.Item, supply.Photo, 1, "(shipyard supply)");
        Save();
        Log.Info(Tag, $"{Name} buys {supply.Label} for {supply.Price}");
    }

    private List<DyeDefinition> OfferedDyes()
        => Yard.Dyes.Where(dye => !dye.Sail).Concat(Yard.Dyes.Where(dye => dye.Sail))
            .Take(FleetOption.Last - FleetOption.FirstDye + 1).ToList();

    private void ShowDyeChoices(int npcId)
    {
        OwnedShip owned = ActiveShip;
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, "Which colours shall she wear, captain?") };
        List<DyeDefinition> dyes = OfferedDyes();
        for (int i = 0; i < dyes.Count; i++)
        {
            DyeDefinition dye = dyes[i];
            bool current = dye.Sail ? Character.SailCloth == dye.Value : owned.Color == dye.Value;
            string need = dye.Item.Length > 0 ? $" + {dye.ItemCount} {Yard.FindSupply(dye.Item)?.Label ?? dye.Item}" : "";
            entries.Add((DialogueEntry.Option, FleetOption.FirstDye + i,
                $"{(dye.Sail ? "Sails" : "Hull")}: {dye.Label}{(current ? " (current)" : $" ({dye.Price} gold{need})")}"));
        }
        entries.Add((DialogueEntry.Option, FleetOption.Menu, "Back"));
        Send(ChatPackets.NpcDialogue(npcId, entries), "(dye choices)");
    }

    private void FleetSay(int npcId, string text)
        => Send(ChatPackets.NpcDialogue(npcId,
        [
            (DialogueEntry.Text, 0, text),
            (DialogueEntry.Option, FleetOption.Menu, "Back"),
            (DialogueEntry.Option, ShipyardOption.Leave, "Goodbye"),
        ]), "(fleet dialogue)");

    private int BagCount(string item) => Items.Values.Where(stack => stack.Name == item).Sum(stack => stack.Amount);

    private void TakeItems(string item, int count, string note)
    {
        foreach ((int slot, ItemStack stack) in Items.Where(pair => pair.Value.Name == item).OrderBy(pair => pair.Key).ToList())
        {
            if (count <= 0) break;
            int taken = Math.Min(count, stack.Amount);
            TakeFromBag(slot, taken, note);
            count -= taken;
        }
    }

    private ShipDefinition ShipTypeOf(int model)
    {
        if (Yard.Find(model) is { } known) return known;
        ShipRules rules = Sea.Data.Rules;
        return new ShipDefinition
        {
            Model = model,
            Label = "Ship",
            Photo = 200100 + model % 100,
            Hull = rules.HullMax,
            Sailors = rules.SailorsMax,
            Cannons = rules.CannonsPerSide,
            CannonsMax = rules.CannonsPerSide,
            Dyeable = false,
        };
    }

    private string OwnedName(OwnedShip owned) => owned.Name.Length > 0 ? owned.Name : $"{Name}'s ship";

    private int RebuildCost(ShipDefinition type, int level)
        => (Yard.Rules.UpgradeCost + (int)((long)type.Price * Yard.Rules.UpgradePricePercent / 100)) * (level + 1);

    private int CannonPorts(ShipDefinition type, int level)
        => Math.Max(0, Math.Min(Math.Max(type.Cannons, type.CannonsMax), type.Cannons + level * Yard.Rules.CannonsPerLevel));

    private static int Scaled(int value, int percentPerLevel, int level)
        => (int)((long)value * (100 + percentPerLevel * Math.Max(0, level)) / 100);
}
