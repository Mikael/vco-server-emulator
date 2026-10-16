using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int LootView = 25;

    private const int LootSlots = 8;
    private const int MaterialDropPercent = 50;
    private const int GearDropPercent = 8;
    private const int LevelsPerTier = 10;

    private readonly Dictionary<int, ItemStack> _loot = [];
    private bool _lootOpen;

    public bool LootOpen => _lootOpen;

    public void DropLoot(MonsterDefinition monster)
    {
        List<ItemStack> drops = RollLoot(monster);
        drops.AddRange(QuestDrops(monster));
        if (drops.Count == 0) return;
        foreach (ItemStack drop in drops)
        {
            int slot = FreeSlot(_loot, LootSlots);
            if (slot == 0) break;
            _loot[slot] = drop;
        }
        if (_lootOpen) Send(ViewPackets.Delete(LootView), "(refresh spoils)");
        _lootOpen = true;
        Send(ViewPackets.Create(LootView, LootSlots), "(spoils)");
        foreach ((int slot, ItemStack item) in _loot.OrderBy(pair => pair.Key))
            Send(ViewPackets.AddItem(LootView, slot, LootProperties(item, slot)), $"(spoil {item.Name})");
        Log.Info(Tag, $"{monster.Name} leaves {string.Join(", ", drops.Select(drop => $"{drop.Amount} x {drop.Name}"))} for {Name}");
    }

    public bool PickLoot(int index, string? receiver)
    {
        if (!_lootOpen) return false;
        if (!_loot.TryGetValue(index, out ItemStack? item)) return true;
        ClientSession taker = this;
        if (receiver != null && !receiver.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            ClientSession? member = Team?.Members.FirstOrDefault(other => other.Name.Equals(receiver, StringComparison.OrdinalIgnoreCase));
            if (member == null || member.Scene != Scene || !member.InWorld)
            {
                SystemMessage($"{receiver} cannot receive it now.", SystemMessageKind.Prompt);
                return true;
            }
            taker = member;
        }
        if (!taker.GiveItem(item.Clone(), "(spoils taken)"))
        {
            SystemMessage(taker == this ? "Your bag is full." : $"The bag of {taker.Name} is full.", SystemMessageKind.Prompt);
            return true;
        }
        _loot.Remove(index);
        Send(ViewPackets.RemoveItem(LootView, index), "(spoil taken)");
        taker.RefreshLoad();
        taker.Save();
        if (taker != this) taker.SystemMessage($"{Name} hands you {item.Amount} article(s) from the spoils.");
        Log.Info(Tag, $"{taker.Name} takes {item.Amount} x {item.Name} from the spoils");
        if (_loot.Count == 0) CloseLoot();
        return true;
    }

    public void CloseLoot()
    {
        if (!_lootOpen) return;
        _lootOpen = false;
        _loot.Clear();
        Send(ViewPackets.Delete(LootView), "(spoils gone)");
    }

    public void CollectGlobal(int index)
    {
        SystemMessage("There is nothing waiting for you to collect.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} clicks global collection slot {index}");
    }

    private PropertyBag LootProperties(ItemStack item, int slot)
    {
        PropertyBag bag = ItemProperties(item, slot);
        if (PropertyTable.Has("QualityLevel") && !bag.ContainsKey("QualityLevel")) bag["QualityLevel"] = 0;
        if (PropertyTable.Has("Binding") && !bag.ContainsKey("Binding")) bag["Binding"] = 0;
        return bag;
    }

    private List<ItemStack> RollLoot(MonsterDefinition monster)
    {
        var drops = new List<ItemStack>();
        int tier = 1 + Math.Max(0, monster.Level) / LevelsPerTier;
        List<string> goods = Context.Data.Items.Goods.Keys.Where(name => ItemCatalog.Number(name, "Level", 1) <= tier).ToList();
        List<string> materials = goods.Where(name => ItemCatalog.Number(name, "ItemType") == SeaPackets.ItemTypeMaterial).ToList();
        List<string> gear = goods.Where(name => ItemCatalog.Number(name, "ItemType") == SeaPackets.ItemTypeEquipment).ToList();
        if (materials.Count > 0 && Random.Shared.Next(100) < MaterialDropPercent)
        {
            string name = materials[Random.Shared.Next(materials.Count)];
            drops.Add(new ItemStack { Name = name, Photo = Context.Data.PhotoOf(name), Amount = 1 + Random.Shared.Next(2) });
        }
        if (gear.Count > 0 && Random.Shared.Next(100) < GearDropPercent)
        {
            string name = gear[Random.Shared.Next(gear.Count)];
            drops.Add(new ItemStack { Name = name, Photo = Context.Data.PhotoOf(name), Amount = 1 });
        }
        return drops;
    }
}
