using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class ShipyardOption
{
    public const int First = 120;
    public const int Buy = 120;
    public const int Repair = 121;
    public const int HireSailors = 122;
    public const int DismissSailors = 123;
    public const int Rename = 124;
    public const int Sails = 125;
    public const int SailOff = 126;
    public const int Leave = 127;
    public const int HealSailors = 128;
    public const int Menu = 139;
    public const int FirstSail = 140;
    public const int Last = 179;
}

public sealed partial class ClientSession
{
    public const string RenameShipCommand = "RenameShip";
    private const int ShipNameLimit = 16;

    private readonly Dictionary<int, List<string>> _yard = [];

    public bool ShipyardOpen => _yard.Count > 0;

    public bool ShipServiceFree => ShipTier <= Sea.Data.Rules.FreeServiceTier;

    public int ShipDamage => Math.Max(0, ShipHullMax - ShipHull) + Math.Max(0, FleetSailMax - ShipSail);

    public int ShipRepairCost => ShipServiceFree ? 0 : ShipDamage * Sea.Data.Rules.RepairPricePerPoint;

    public int SailorCost => ShipServiceFree ? 0 : Sea.Data.Rules.SailorPrice;

    public bool ReadyToSail()
    {
        EnsureShipState();
        if (ShipHull > Sea.Data.Rules.SunkDurability) return true;
        SystemMessage("Your ship is a wreck. You have to have it repaired at the shipyard before you can sail off.", SystemMessageKind.Prompt);
        return false;
    }

    public bool OpenShipyardMenu(int npcId)
    {
        EnsureShipState();
        TalkingTo = npcId;
        ShipRules rules = Sea.Data.Rules;
        var entries = new List<(DialogueEntry, int, string)>
        {
            (DialogueEntry.Text, 0, $"Welcome to the shipyard. The {ShipName}: hull {ShipHull}/{ShipHullMax}, sail {ShipSail}/{FleetSailMax}, "
                + $"{Character.Sailors}/{FleetSailorsMax} sailors ({Character.SailorsWounded} wounded), {Character.CannonsLeft + Character.CannonsRight} cannons."),
            (DialogueEntry.Option, ShipyardOption.Buy, "Buy cannons and ammunition"),
            (DialogueEntry.Option, ShipyardOption.Repair, ShipRepairCost > 0 ? $"Repair hull and sail ({ShipRepairCost} silver)" : "Repair hull and sail"),
            (DialogueEntry.Option, ShipyardOption.HireSailors, SailorCost > 0 ? $"Hire sailors ({SailorCost} silver each)" : "Hire sailors"),
            (DialogueEntry.Option, ShipyardOption.HealSailors, "Tend the wounded sailors"),
            (DialogueEntry.Option, ShipyardOption.DismissSailors, "Dismiss sailors"),
            (DialogueEntry.Option, ShipyardOption.Rename, $"Rename the ship ({rules.RenamePrice} gold)"),
            (DialogueEntry.Option, ShipyardOption.Sails, "Change the sails"),
            (DialogueEntry.Option, FleetOption.Menu, "Ships, dock and refit"),
            (DialogueEntry.Option, ShipyardOption.SailOff, "Sail off"),
            (DialogueEntry.Option, ShipyardOption.Leave, "Goodbye"),
        };
        if (BboHighSeasEntry() is { } highSeas) entries.Insert(entries.Count - 2, highSeas);
        Send(ChatPackets.NpcDialogue(npcId, entries), "(shipyard menu)");
        return true;
    }

    public bool ChooseShipyardOption(int npcId, int option)
    {
        if (option is < ShipyardOption.First or > ShipyardOption.Last) return false;
        EnsureShipState();
        switch (option)
        {
            case ShipyardOption.Buy:
                OpenShipyard();
                ShipyardSay(npcId, "Cannons are on the first shelf, ammunition on the second. Click an item to buy it.");
                break;
            case ShipyardOption.Repair:
                AskShipRepair();
                break;
            case ShipyardOption.HireSailors:
                AskHireSailors();
                break;
            case ShipyardOption.DismissSailors:
                AskDismissSailors();
                break;
            case ShipyardOption.HealSailors:
                HealShipSailors();
                OpenShipyardMenu(npcId);
                break;
            case ShipyardOption.Rename:
                AskRenameShip();
                break;
            case ShipyardOption.Sails:
                ShowSailCloths(npcId);
                break;
            case ShipyardOption.SailOff:
                CloseShipyard();
                SailOff();
                break;
            case ShipyardOption.Leave:
                CloseShipyard();
                CloseNpcDialogue();
                break;
            case ShipyardOption.Menu:
                OpenShipyardMenu(npcId);
                break;
            default:
                int cloth = option - ShipyardOption.FirstSail;
                if (cloth < 0 || cloth >= Sea.Data.SailCloths.Count) return false;
                DyeSail(Sea.Data.SailCloths[cloth]);
                ShowSailCloths(npcId);
                break;
        }
        return true;
    }

    public void OpenShipyard()
    {
        CloseShop();
        CloseShipyard();
        SeaData data = Sea.Data;

        var cannons = new List<string>();
        _yard[SeaPackets.YardEquipmentView] = cannons;
        Send(ViewPackets.Create(SeaPackets.YardEquipmentView, ViewId.ShopSlots), "(shipyard equipment)");
        foreach (CannonDefinition cannon in data.Cannons.Where(entry => entry.Sold).Take(ViewId.ShopSlots))
        {
            cannons.Add(cannon.Name);
            var item = new PropertyBag
            {
                ["Name"] = cannon.Name,
                ["Index"] = cannons.Count,
                ["Amount"] = 1,
                ["Photo"] = cannon.Photo,
                ["Kind"] = 0,
                ["ItemType"] = SeaPackets.ItemTypeShipEquipment,
                ["ShipArmType"] = SeaPackets.CannonArmType,
                ["AT"] = cannon.Attack,
                ["Level"] = cannon.Grade,
                ["ShotTime"] = (int)(cannon.ReloadSeconds * 1000),
                ["FireAngle"] = cannon.FireAngle,
                ["Flight"] = cannon.Range,
            };
            ItemCatalog.Extend(item, cannon.Name);
            item["Cost"] = cannon.Price;
            item["ShowPrice"] = cannon.Price;
            Send(ViewPackets.AddItem(SeaPackets.YardEquipmentView, cannons.Count, SeaPackets.Known(item)), "(shipyard cannon)");
        }

        var shells = new List<string>();
        _yard[SeaPackets.YardAmmunitionView] = shells;
        Send(ViewPackets.Create(SeaPackets.YardAmmunitionView, ViewId.ShopSlots), "(shipyard ammunition)");
        foreach (AmmunitionDefinition ammunition in data.Ammunition.Where(entry => entry.Sold).Take(ViewId.ShopSlots))
        {
            shells.Add(ammunition.Name);
            int lot = Math.Max(1, ammunition.Lot);
            var item = new PropertyBag
            {
                ["Name"] = ammunition.Name,
                ["Index"] = shells.Count,
                ["Amount"] = lot,
                ["Photo"] = ammunition.Photo,
                ["Kind"] = 0,
                ["ItemType"] = SeaPackets.ItemTypeAmmunition,
            };
            ItemCatalog.Extend(item, ammunition.Name);
            item["Cost"] = ammunition.Price * lot;
            item["ShowPrice"] = ammunition.Price * lot;
            Send(ViewPackets.AddItem(SeaPackets.YardAmmunitionView, shells.Count, SeaPackets.Known(item)), "(shipyard ammunition)");
        }
        BboOpenShipyardTabs();
    }

    public void CloseShipyard()
    {
        foreach (int view in _yard.Keys) Send(ViewPackets.Delete(view), "(close shipyard shelf)");
        _yard.Clear();
    }

    public bool AskShipyard(int view, int index)
    {
        if (!_yard.TryGetValue(view, out List<string>? shelf)) return false;
        if (index < 1 || index > shelf.Count) return true;
        EnsureShipState();
        string name = shelf[index - 1];
        int price = 0;
        int most = 0;
        if (view == SeaPackets.YardAmmunitionView && Sea.Data.FindAmmunition(name) is { } ammunition)
        {
            price = ammunition.Price * Math.Max(1, ammunition.Lot);
            most = 99;
        }
        else if (view == SeaPackets.YardEquipmentView && Sea.Data.FindCannon(name) is { } cannon
            && cannon.Grade <= Math.Max(1, UniqueSkillLevel("US_CANNON")) && cannon.Grade <= Math.Max(1, ShipTier))
        {
            price = cannon.Price;
            most = 2 * FleetCannonsPerSide - (cannon.Name == Character.CannonName ? Character.CannonsLeft + Character.CannonsRight : 0);
        }
        if (price > 0) most = Math.Min(most, Money / price);
        if (price <= 0 || most < 1) return ShipyardBuy(view, index, 1);
        Send(UiPackets.Custom($"BuyInfo {view} {index} {price} {most}"), "(buy offer)");
        return true;
    }

    public bool ShipyardBuy(int view, int index, int quantity)
    {
        if (!_yard.TryGetValue(view, out List<string>? shelf)) return false;
        if (index < 1 || index > shelf.Count) return true;
        EnsureShipState();
        string name = shelf[index - 1];
        quantity = Math.Clamp(quantity, 1, 99);

        if (view == SeaPackets.YardAmmunitionView && Sea.Data.FindAmmunition(name) is { } ammunition)
        {
            int lot = Math.Max(1, ammunition.Lot);
            int cost = ammunition.Price * lot * quantity;
            if (!PayShipyard(cost)) return true;
            AddAmmunition(name, lot * quantity);
            SystemMessage($"{lot * quantity} rounds were loaded into the magazine ({Character.Ammunition![name]} aboard).");
            Save();
            Log.Info(Tag, $"{Name} buys {lot * quantity} x {name} for {cost}");
            return true;
        }

        if (view == SeaPackets.YardEquipmentView && Sea.Data.FindCannon(name) is { } cannon)
        {
            if (cannon.Grade > Math.Max(1, UniqueSkillLevel("US_CANNON")))
            {
                SystemMessage($"That cannon needs Cannon Fight Level {cannon.Grade}.");
                return true;
            }
            if (cannon.Grade > Math.Max(1, ShipTier))
            {
                SystemMessage($"That cannon can only be installed on a ship of level {cannon.Grade}.");
                return true;
            }
            if (cannon.Name != Character.CannonName && Character.CannonsLeft + Character.CannonsRight > 0)
            {
                Character.CannonsLeft = 0;
                Character.CannonsRight = 0;
                SystemMessage("Your old cannons were taken off to make room for the new type.");
            }
            int most = FleetCannonsPerSide;
            int mounted = 0;
            for (int i = 0; i < quantity; i++)
            {
                bool left = Character.CannonsLeft <= Character.CannonsRight;
                if ((left ? Character.CannonsLeft : Character.CannonsRight) >= most)
                {
                    SystemMessage("Every gun port of your ship already holds a cannon.");
                    break;
                }
                if (!PayShipyard(cannon.Price)) break;
                if (left) Character.CannonsLeft++;
                else Character.CannonsRight++;
                mounted++;
            }
            if (mounted == 0) return true;
            Character.CannonName = cannon.Name;
            SetShipCannons(Character.CannonsLeft, Character.CannonsRight);
            SendShipDetails();
            SystemMessage($"{mounted} cannon(s) mounted: {Character.CannonsLeft} to port, {Character.CannonsRight} to starboard.");
            Save();
            Log.Info(Tag, $"{Name} mounts {mounted} x {name}");
        }
        return true;
    }

    public void AskShipRepair()
    {
        EnsureShipState();
        int cost = ShipRepairCost;
        if (ShipDamage <= 0)
        {
            SystemMessage("Your ship does not need any repairs.");
            return;
        }
        Send(SeaPackets.RepairShipInfo($"RepairFee:{cost}"), "(repair offer)");
    }

    public void RepairShipHull()
    {
        EnsureShipState();
        int cost = ShipRepairCost;
        if (ShipDamage <= 0)
        {
            SystemMessage("Your ship does not need any repairs.");
            return;
        }
        if (AtSea)
        {
            SystemMessage("The ship can only be repaired at a shipyard.");
            return;
        }
        if (!PayShipyard(cost)) return;
        SetShipHull(ShipHullMax);
        SetShipSail(FleetSailMax);
        SystemMessage(cost > 0 ? $"Your ship has been repaired for {cost} silver coins." : "Your ship has been repaired free of charge.");
        Save();
        Log.Info(Tag, $"{Name} repairs the ship for {cost}");
    }

    public void AskHireSailors()
    {
        if (Character.Sailors + Character.SailorsWounded >= FleetSailorsMax)
        {
            SystemMessage("Your ship cannot carry more sailors.");
            return;
        }
        Send(SeaPackets.SailorPrice(SailorCost), "(sailor price)");
    }

    public void AskDismissSailors()
    {
        if (Character.Sailors <= 0)
        {
            SystemMessage("You have no sailors to dismiss.");
            return;
        }
        Send(SeaPackets.ShowFireSailor(Character.Sailors), "(dismiss sailors)");
    }

    public void HireShipSailors(int count)
    {
        count = Math.Min(count, FleetSailorsMax - Character.Sailors - Character.SailorsWounded);
        if (count <= 0)
        {
            SystemMessage("Your ship cannot carry more sailors.");
            return;
        }
        if (!PayShipyard(count * SailorCost)) return;
        Character.Sailors += count;
        ShipSailorsChanged($"hires {count} sailor(s)");
    }

    public void HealShipSailors()
    {
        int wounded = Character.SailorsWounded;
        if (wounded <= 0)
        {
            SystemMessage("None of your sailors is wounded.");
            return;
        }
        if (!PayShipyard(wounded * SailorCost / 2)) return;
        SetSailors(Character.Sailors + wounded, 0);
        SystemMessage($"{wounded} wounded sailor(s) are fit for duty again.");
        Save();
        Log.Info(Tag, $"{Name} has {wounded} sailor(s) healed");
    }

    public void DismissShipSailors(int count)
    {
        count = Math.Min(count, Character.Sailors);
        if (count <= 0) return;
        Character.Sailors -= count;
        ShipSailorsChanged($"dismisses {count} sailor(s)");
    }

    public void AskRenameShip()
        => Send(SeaPackets.ShowRenameShip(RenameShipCommand, "Pleasenametheship:"), "(rename ship)");

    public bool RenameShip(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > ShipNameLimit || name.Any(letter => letter < ' ' || letter > 0xff))
        {
            SystemMessage($"A ship name must be 1 to {ShipNameLimit} letters long.");
            return true;
        }
        if (name == ShipName) return true;
        if (!PayShipyard(Sea.Data.Rules.RenamePrice)) return true;
        Character.ShipName = name;
        SendShipDetails();
        if (AtSea && PropertyTable.Has("S/Name")) SetProperty("S/Name", name, visibleToOthers: true);
        SystemMessage($"Your ship is now called {name}.");
        Save();
        Log.Info(Tag, $"{Name} renames the ship to {name}");
        return true;
    }

    public bool DyeSail(string what)
    {
        SailClothDefinition? cloth = Sea.Data.SailCloths.FirstOrDefault(entry =>
            entry.Cloth.ToString() == what
            || (entry.Item.Length > 0 && entry.Item == what)
            || entry.Name.Equals(what, StringComparison.OrdinalIgnoreCase));
        if (cloth == null) return false;
        DyeSail(cloth);
        return true;
    }

    public void DyeSail(SailClothDefinition cloth)
    {
        if (cloth.Cloth == Character.SailCloth)
        {
            SystemMessage("Your ship already carries those sails.");
            return;
        }
        if (!PayShipyard(cloth.Price)) return;
        Character.SailCloth = cloth.Cloth;
        Properties["S/SailTex"] = cloth.Cloth;
        SendShipDetails();
        if (AtSea) SetProperty("S/SailTex", cloth.Cloth, visibleToOthers: true);
        SystemMessage($"New sails were bent on: {cloth.Name}. You will see them when you next put to sea.");
        Save();
        Log.Info(Tag, $"{Name} changes sails to {cloth.Cloth}");
    }

    private void ShowSailCloths(int npcId)
    {
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, "Which sails shall we bend on, captain?") };
        List<SailClothDefinition> cloths = Sea.Data.SailCloths;
        for (int i = 0; i < cloths.Count && ShipyardOption.FirstSail + i <= ShipyardOption.Last; i++)
        {
            string mark = cloths[i].Cloth == Character.SailCloth ? " (current)" : cloths[i].Price > 0 ? $" ({cloths[i].Price} gold)" : "";
            entries.Add((DialogueEntry.Option, ShipyardOption.FirstSail + i, cloths[i].Name + mark));
        }
        entries.Add((DialogueEntry.Option, ShipyardOption.Menu, "Back"));
        Send(ChatPackets.NpcDialogue(npcId, entries), "(sail cloths)");
    }

    private void ShipyardSay(int npcId, string text)
        => Send(ChatPackets.NpcDialogue(npcId,
        [
            (DialogueEntry.Text, 0, text),
            (DialogueEntry.Option, ShipyardOption.Menu, "Back"),
            (DialogueEntry.Option, ShipyardOption.Leave, "Goodbye"),
        ]), "(shipyard dialogue)");

    private bool PayShipyard(int cost)
    {
        if (cost <= 0) return true;
        if (Money < cost)
        {
            SystemMessage($"Not enough money: that costs {cost} gold.");
            return false;
        }
        SetMoney(Money - cost);
        return true;
    }

    private void ShipSailorsChanged(string what)
    {
        SetSailors(Character.Sailors, Character.SailorsWounded);
        SystemMessage($"You now have {Character.Sailors} sailors.");
        Save();
        Log.Info(Tag, $"{Name} {what}");
    }
}
