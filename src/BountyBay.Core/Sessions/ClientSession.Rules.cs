using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int SeaStanceGuard = 1;
    public const int SeaStancePursuit = 2;
    public const int SeaStanceFullSpeed = 3;
    public const int ChatLength = 64;
    public const int LeaveSceneLockSeconds = 30;
    public const string ProvisionItem = "IDName00004590";

    private const int ChatSlackMilliseconds = 250;
    private const int CourseChangeTenths = 50;

    private static readonly Dictionary<string, (string Label, int Milliseconds)> ChatIntervals = new()
    {
        ["say"] = ("Normal", 2000),
        ["whisper"] = ("Private Chat", 1000),
        ["world"] = ("Public Chat", 30000),
        ["team"] = ("Team", 1000),
        ["city"] = ("City", 2000),
        ["guild"] = ("Guild Speech", 2000),
        ["guildnotice"] = ("Guild Bulletin", 2000),
        ["department"] = ("Department Speech", 2000),
        ["departmentnotice"] = ("Department Bulletin", 2000),
    };

    private readonly Dictionary<string, long> _chatAt = [];
    private readonly Dictionary<string, long> _stateUntil = [];
    private readonly Dictionary<int, long> _coolingUntil = [];
    private long _attackedAt;
    private long _seaStanceUntil;
    private int _healPool;
    private int _spPool;
    private int _supplyClock;
    private int _voyageClock;
    private int _overflowStored;
    private bool _warehouseOpen;
    private bool _suppliesWarned;
    private bool _overflowAnnounced;
    private int _shipLoadSent = -1;
    private int _shipLoadMaxSent = -1;

    public int Sp { get; private set; } = 100;

    public int MaxSp { get; private set; } = 100;

    public int SeaStance { get; private set; }

    public int SeaTarget { get; set; }

    public bool WarehouseOpen => _warehouseOpen;

    public float EffectiveSail => SeaStance == SeaStanceGuard ? SailLowest : Sail;

    public float SeaStanceSpeedFactor => SeaStance switch
    {
        SeaStancePursuit => MathF.Max(1f, Sea.Data.Rules.PursuitSpeedFactor),
        SeaStanceFullSpeed => MathF.Max(1f, Sea.Data.Rules.FullSpeedFactor),
        _ => 1f,
    };

    public int ShipLoadMax => FleetLoadMax + 20 * UniqueSkillLevel("US_HALIEUTICS2");

    public int ShipLoad => Character.Items == null ? 0
        : Items.Values.Where(InHold).Sum(Weight) + (Bags.TryGetValue(ViewId.Cargo, out Dictionary<int, ItemStack>? cargo) ? cargo.Values.Sum(Weight) : 0);

    public void SetSp(int sp)
    {
        Sp = Math.Clamp(sp, 0, MaxSp);
        SetProperty("SP", Sp);
        SetProperty("RatioSP", 100 * Sp / Math.Max(1, MaxSp), visibleToOthers: true);
    }

    public int WarehouseSlots => Rule.Economy.WarehouseSlots;

    public float WarehouseLoadMax => Rule.Economy.WarehouseLoad + Character.WarehouseLoad;

    public void RefreshStats()
    {
        Dictionary<string, int> before = StatProperties.ToDictionary(name => name, name => Properties.Int(name, -1));
        ComputeStats();
        if (!InWorld) return;
        foreach (string name in StatProperties)
        {
            if (Properties.Int(name) != before[name]) SetProperty(name, Properties[name], visibleToOthers: name is "MaxHP" or "Level");
        }
        if (Properties.Int("MaxHP") != before["MaxHP"]) SetHp(Hp);
        if (Properties.Int("MaxSP") != before["MaxSP"]) SetSp(Sp);
        string fighting = FightingSkill();
        if (Properties.Text("FightSkill") != fighting) SetKnownProperty("FightSkill", fighting);
        RefreshLoad();
    }

    public string SceneName => SceneNames.MapNameKey(Context.Settings, Scene);

    public string CityName => Rule.CityNames.TryGetValue(Scene, out string? name) ? name : SceneNames.CityKey(Context.Settings, Scene);

    public void SendSceneState(GameTime time)
    {
        if (!InWorld) return;
        Send(ScenePackets.SceneState(time, WorldSettings.Weather, CityName, SceneName, scene: Scene), "(time, weather, wind)");
        if (CurrentBattle is { } room) Send(BattlePackets.SceneKind(room.Definition.SceneKind), "(battle scene kind)");
    }

    public void TickSecond(long tick)
    {
        if (!InWorld) return;
        long now = Environment.TickCount64;
        foreach (string state in _stateUntil.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList()) EndState(state);
        if (SeaStance != 0 && now >= _seaStanceUntil) ClearSeaStance();
        PulsePotions();
        TickTotems(tick);
        if (AtSea && !SeaSunk)
        {
            ConsumeSupplies();
            GainVoyageExperience();
        }
        RefreshSupplies();
        RefreshLoad();
        if (tick % Math.Max(1, Rule.Character.RegenerationSeconds) == 0) Regenerate();
    }

    public bool ChatAllowed(string channel, ref string text)
    {
        if (text.Length > ChatLength)
        {
            text = text[..ChatLength];
            SystemMessage($"You can only enter {ChatLength} letters in the Chat Text Box, so your message was shortened.", SystemMessageKind.Prompt);
        }
        if (!ChatIntervals.TryGetValue(channel, out (string Label, int Milliseconds) rule)) return true;
        long now = Environment.TickCount64;
        if (_chatAt.TryGetValue(channel, out long last) && now - last < rule.Milliseconds - ChatSlackMilliseconds)
        {
            long wait = (rule.Milliseconds - (now - last) + 999) / 1000;
            SystemMessage($"In the {rule.Label} channel, messages can be sent at intervals of {rule.Milliseconds / 1000} seconds. "
                + $"Please wait {wait} more second{(wait == 1 ? "" : "s")} to speak.", SystemMessageKind.Prompt);
            return false;
        }
        _chatAt[channel] = now;
        return true;
    }

    public void MarkAttacked() => _attackedAt = Environment.TickCount64;

    public bool SceneLocked()
    {
        if (_attackedAt == 0) return false;
        long left = LeaveSceneLockSeconds * 1000L - (Environment.TickCount64 - _attackedAt);
        if (left <= 0) return false;
        SystemMessage($"You are allowed to leave if you can keep {LeaveSceneLockSeconds} seconds without being attacked "
            + $"({(left + 999) / 1000} to go).", SystemMessageKind.Prompt);
        return true;
    }

    public void ResetSceneState()
    {
        SeaStance = 0;
        _seaStanceUntil = 0;
        _attackedAt = 0;
        SeaTarget = 0;
        CloseWarehouse();
    }

    public void StartSeaStance(int stance, int seconds)
    {
        SeaStance = stance;
        _seaStanceUntil = Environment.TickCount64 + seconds * 1000L;
        SendSeaStance();
        ReissueShipMove("sea stance");
    }

    public void ClearSeaStance(bool reissue = true)
    {
        if (SeaStance == 0) return;
        SeaStance = 0;
        _seaStanceUntil = 0;
        if (!InWorld || !AtSea) return;
        SendSeaStance();
        if (reissue) ReissueShipMove("sea stance ends");
    }

    public bool FiringEndsFullSpeed()
    {
        if (SeaStance != SeaStanceFullSpeed) return false;
        ClearSeaStance();
        SystemMessage("No attack is allowed at Full Speed. Full Speed was cancelled.", SystemMessageKind.Prompt);
        return true;
    }

    public void RefreshLoad()
    {
        if (Character.Items == null || Character.Bags == null) return;
        float carried = Items.Values.Where(item => !InHold(item)).Sum(Weight)
            + Bags.Where(pair => pair.Key != ViewId.Cargo).SelectMany(pair => pair.Value.Values).Sum(Weight);
        float most = Rule.Character.BaseLoad + Rule.Character.LoadPerPracticeStuntLevel * StuntLevels(LoadStunts) + WornSum("AddMaxLoad");
        if (Convert.ToSingle(Properties.GetValueOrDefault("Load", -1f)) != carried) SetProperty("Load", carried);
        if (Convert.ToSingle(Properties.GetValueOrDefault("LoadMax", -1f)) != most) SetProperty("LoadMax", most);
        int hold = ShipLoad;
        int holdMax = ShipLoadMax;
        if (hold == _shipLoadSent && holdMax == _shipLoadMaxSent) return;
        _shipLoadSent = hold;
        _shipLoadMaxSent = holdMax;
        PropertyBag update = SeaPackets.Known(new PropertyBag { ["S/Load"] = hold, ["S/LoadMax"] = holdMax });
        if (update.Count > 0) Send(ViewPackets.UpdateItem(ViewId.Ship, 1, update), "(ship load)");
    }

    public void RefreshSupplies()
    {
        if (Character.Items == null) return;
        int sum = ProvisionCount();
        int cost = SupplyCost;
        int days = sum / (cost * Math.Max(1, Sea.Data.Rules.SupplyTicksPerDay));
        if (Properties.Int("SupplySum", -1) != sum) SetProperty("SupplySum", sum);
        if (Properties.Int("SupplyCost", -1) != cost) SetProperty("SupplyCost", cost);
        if (Properties.Int("SupplyDay", -1) != days) SetProperty("SupplyDay", days);
        if (sum > 0) _suppliesWarned = false;
    }

    public int SupplyCost
    {
        get
        {
            int sailors = Math.Max(1, Sea.Data.Rules.SailorsPerProvision);
            return Math.Max(1, (Character.Sailors + Character.SailorsWounded + sailors - 1) / sailors);
        }
    }

    public void OpenWarehouse(int npc)
    {
        CloseShop();
        if (!_warehouseOpen)
        {
            _warehouseOpen = true;
            int capacity = Math.Max(WarehouseSlots, Character.Warehouse.Count == 0 ? 0 : Character.Warehouse.Keys.Max());
            Send(ViewPackets.Create(ViewId.Warehouse, capacity), "(warehouse view)");
            foreach ((int slot, ItemStack item) in Character.Warehouse.OrderBy(pair => pair.Key))
                Send(ViewPackets.AddItem(ViewId.Warehouse, slot, ItemProperties(item, slot)), $"(warehouse item {item.Name})");
        }
        SendWarehouseLoad();
        Send(ChatPackets.NpcDialogue(npc,
        [
            (DialogueEntry.Text, 0, $"Click something in your bag to store it here. Click something in the warehouse to take it back. "
                + $"It holds {WarehouseSlots} articles within {WarehouseLoadMax:F0} loads in total."),
            (DialogueEntry.Option, (int)DialogueAction.WarehouseExpand,
                $"Buy {Rule.Economy.WarehouseLoadLot} more loads ({(long)Rule.Economy.WarehouseLoadPrice * Rule.Economy.WarehouseLoadLot} silver)"),
            (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
        ]), "(npc dialogue)");
        Log.Info(Tag, $"{Name} opens the warehouse ({Character.Warehouse.Count} stacks)");
    }

    public void CloseWarehouse()
    {
        if (!_warehouseOpen) return;
        _warehouseOpen = false;
        Send(ViewPackets.Delete(ViewId.Warehouse), "(close warehouse)");
    }

    public bool OnWarehouseSelect(int view, int how, int index)
    {
        if (!_warehouseOpen || how != 0) return false;
        if (view == ViewId.Warehouse)
        {
            if (Hand != null) StoreHand();
            else WarehouseTake(index, int.MaxValue);
            return true;
        }
        Dictionary<int, ItemStack>? bag = MailBagOf(view);
        if (bag == null || !bag.TryGetValue(index, out ItemStack? item)) return false;
        WarehouseDeposit(view, index, item.Amount);
        return true;
    }

    public void WarehouseTake(int index, int amount)
    {
        if (!_warehouseOpen || !Character.Warehouse.TryGetValue(index, out ItemStack? stored) || amount <= 0) return;
        amount = Math.Min(amount, stored.Amount);
        int view = ItemCatalog.Number(stored.Name, "ItemType") == SeaPackets.ItemTypeEquipment ? ViewId.BagEquip : ViewId.BagItems;
        if (!MailStore(view, new ItemStack { Name = stored.Name, Photo = stored.Photo, Amount = amount }, "(taken from warehouse)"))
        {
            SystemMessage("Your bag is full.");
            return;
        }
        stored.Amount -= amount;
        if (stored.Amount > 0)
        {
            Send(ViewPackets.UpdateItem(ViewId.Warehouse, index, new PropertyBag { ["Amount"] = stored.Amount }), "(warehouse item)");
        }
        else
        {
            Character.Warehouse.Remove(index);
            Send(ViewPackets.RemoveItem(ViewId.Warehouse, index), "(warehouse item)");
        }
        SendWarehouseLoad();
        Save();
        Log.Info(Tag, $"{Name} takes {amount} x {stored.Name} from the warehouse");
    }

    public void ExpandWarehouse()
    {
        EconomyRules rules = Rule.Economy;
        int lot = Math.Max(1, rules.WarehouseLoadLot);
        long price = (long)rules.WarehouseLoadPrice * lot;
        if (Money < price)
        {
            SystemMessage($"It will cost you {rules.WarehouseLoadPrice} silver coins for one load: {price} for {lot}.");
            return;
        }
        SetMoney((int)(Money - price));
        Character.WarehouseLoad += lot;
        SendWarehouseLoad();
        Save();
        SystemMessage($"Your warehouse now holds {WarehouseLoadMax:F0} loads.");
        Log.Info(Tag, $"{Name} enlarges the warehouse to {WarehouseLoadMax:F0} loads");
    }

    public bool WarehouseStore(ItemStack item, bool anySlot = false)
    {
        Dictionary<int, ItemStack> stored = Character.Warehouse;
        bool stacks = ItemCatalog.Number(item.Name, "ItemType") != SeaPackets.ItemTypeEquipment;
        int slot = stacks ? stored.FirstOrDefault(pair => pair.Value.Name == item.Name).Key : 0;
        if (slot != 0)
        {
            stored[slot].Amount += item.Amount;
            if (_warehouseOpen)
                Send(ViewPackets.UpdateItem(ViewId.Warehouse, slot, new PropertyBag { ["Amount"] = stored[slot].Amount }), "(warehouse item)");
            return true;
        }
        slot = FreeSlot(stored, anySlot ? int.MaxValue - 1 : WarehouseSlots);
        if (slot == 0) return false;
        stored[slot] = item;
        if (_warehouseOpen) Send(ViewPackets.AddItem(ViewId.Warehouse, slot, ItemProperties(item, slot)), "(warehouse item)");
        return true;
    }

    private void StoreHand()
    {
        ItemStack held = Hand!;
        int slot = Items.FirstOrDefault(pair => pair.Value.Name == held.Name && pair.Value.Amount >= held.Amount).Key;
        SetHand(null);
        if (slot != 0) WarehouseDeposit(ViewId.BagItems, slot, held.Amount);
    }

    private void WarehouseDeposit(int view, int index, int amount)
    {
        Dictionary<int, ItemStack> bag = MailBagOf(view)!;
        ItemStack item = bag[index];
        amount = Math.Clamp(amount, 1, item.Amount);
        if (Hand != null && Hand.Name == item.Name) SetHand(null);
        float room = WarehouseLoadMax - Character.Warehouse.Values.Sum(Weight);
        int unit = ItemCatalog.Number(item.Name, "Weight");
        if (unit > 0 && unit * amount > room)
        {
            amount = (int)(room / unit);
            if (amount <= 0)
            {
                SystemMessage($"Your warehouse cannot hold more than {WarehouseLoadMax:F0} loads.");
                return;
            }
        }
        if (!WarehouseStore(new ItemStack { Name = item.Name, Photo = item.Photo, Amount = amount }))
        {
            SystemMessage("Your warehouse is full.");
            return;
        }
        MailTake(view, index, amount, "(stored in warehouse)");
        SendWarehouseLoad();
        Save();
        Log.Info(Tag, $"{Name} stores {amount} x {item.Name} in the warehouse");
    }

    private void SendWarehouseLoad()
    {
        SetKnownProperty("BoxLoad", (float)Character.Warehouse.Values.Sum(Weight));
        SetKnownProperty("BoxMaxLoad", WarehouseLoadMax);
    }

    private void CompactBags()
    {
        var tabs = new List<Dictionary<int, ItemStack>> { Items, Bags[ViewId.BagEquip], Bags[ViewId.BagArticle] };
        foreach (Dictionary<int, ItemStack> tab in tabs) Compact(tab, ViewId.BagSlots, null);
        Compact(Bags[ViewId.Equipped], ViewId.EquippedSlots, Bags[ViewId.BagEquip]);
        if (_overflowStored > 0) Log.Info(Tag, $"{Name}: {_overflowStored} stack(s) did not fit the bag and were moved to the warehouse");
    }

    private void Compact(Dictionary<int, ItemStack> slots, int capacity, Dictionary<int, ItemStack>? spill)
    {
        foreach (int slot in slots.Keys.Where(key => key < 1 || key > capacity).Order().ToList())
        {
            ItemStack item = slots[slot];
            slots.Remove(slot);
            int free = FreeSlot(slots, capacity);
            if (free != 0)
            {
                slots[free] = item;
                continue;
            }
            free = spill == null ? 0 : FreeSlot(spill, ViewId.BagSlots);
            if (free != 0)
            {
                spill![free] = item;
                continue;
            }
            WarehouseStore(item, anySlot: true);
            _overflowStored++;
        }
    }

    private void AnnounceOverflow()
    {
        if (_overflowAnnounced) return;
        _overflowAnnounced = true;
        if (_overflowStored > 0)
            SystemMessage($"Your bag holds {ViewId.BagSlots} articles per tab. {_overflowStored} stack(s) that did not fit were moved to your warehouse.");
    }

    private void StartState(string name, int seconds)
    {
        _stateUntil[name] = Environment.TickCount64 + seconds * 1000L;
        SetKnownProperty(name, 1);
        if (StatStates.Contains(name)) RefreshStats();
    }

    private bool StateActive(string name) => _stateUntil.TryGetValue(name, out long until) && until > Environment.TickCount64;

    private void EndState(string name)
    {
        if (!_stateUntil.Remove(name)) return;
        SetKnownProperty(name, 0);
        if (StatStates.Contains(name)) RefreshStats();
    }

    private void SendSeaStance()
    {
        PropertyBag update = SeaPackets.Known(new PropertyBag { ["MarineStrategy"] = SeaStance, ["ShipSpeedAdj"] = EffectiveSail });
        if (update.Count == 0) return;
        Send(ScenePackets.ObjectProperties(ProtocolConstants.SelfObjectId, update), "(sea stance)");
        update.Remove("ShipSpeedAdj");
        if (update.Count > 0) ToOthers(ScenePackets.ObjectProperties(ObjectId, update), $"({Name} sea stance)");
    }

    private bool OnCourseChanged(int before, int after)
    {
        if (!AtSea)
        {
            EndState("IsUSAxe3");
            EndState("IsUSShot4");
            return false;
        }
        if (SeaStance is not (SeaStancePursuit or SeaStanceFullSpeed)) return false;
        int turned = Math.Abs(after - before) % 3600;
        if (Math.Min(turned, 3600 - turned) <= CourseChangeTenths) return false;
        ClearSeaStance(reissue: false);
        SystemMessage("You changed course and lost your speed.", SystemMessageKind.Prompt);
        return true;
    }

    private int ShipEquipmentArmType(int slot)
    {
        if (slot == SeaPackets.LeftCannonSlot) return Character.CannonsLeft > 0 ? SeaPackets.CannonArmType : -1;
        if (slot == SeaPackets.RightCannonSlot) return Character.CannonsRight > 0 ? SeaPackets.CannonArmType : -1;
        return -1;
    }

    private void PulsePotions()
    {
        if (_healPool <= 0 && _spPool <= 0) return;
        CharacterRules rules = Rule.Character;
        int pulse = rules.PotionPerSecond + rules.PotionPerHealingLevel * UniqueSkillLevel("US_HEAL");
        int restored = 0;
        if (_healPool > 0)
        {
            int amount = Math.Min(pulse, _healPool);
            _healPool -= amount;
            int before = Hp;
            if (Hp > 0 && Hp < MaxHp) SetHp(Hp + amount);
            restored += Hp - before;
            if (_healPool <= 0 || Hp >= MaxHp || Hp <= 0)
            {
                _healPool = 0;
                SetKnownProperty("HealCondition", 0);
            }
        }
        if (_spPool > 0)
        {
            int amount = Math.Min(pulse, _spPool);
            _spPool -= amount;
            int before = Sp;
            if (Sp < MaxSp) SetSp(Sp + amount);
            restored += Sp - before;
            if (_spPool <= 0 || Sp >= MaxSp)
            {
                _spPool = 0;
                SetKnownProperty("FreshCondition", 0);
            }
        }
        if (restored > 0) GainSkillExperience(Medicine, (long)restored * Rule.Skills.MedicineExperiencePerPoint);
    }

    private int LastingPotion(int amount) => amount * (100 + 10 * UniqueSkillLevel("US_ALCHEMY3")) / 100;

    private bool ItemCooling(string name)
    {
        int kind = ItemCatalog.Number(name, "CoolingKind");
        if (kind <= 0 || !_coolingUntil.TryGetValue(kind, out long until)) return false;
        long left = until - Environment.TickCount64;
        if (left <= 0) return false;
        SystemMessage($"That article is cooling down: {(left + 999) / 1000} seconds to go.", SystemMessageKind.Prompt);
        return true;
    }

    private void StartItemCooling(string name)
    {
        int kind = ItemCatalog.Number(name, "CoolingKind");
        int milliseconds = ItemCatalog.Number(name, "CoolTime");
        if (kind <= 0 || milliseconds <= 0) return;
        _coolingUntil[kind] = Environment.TickCount64 + milliseconds;
        Send(ChatPackets.Script(0, $"ToolCooling {kind} {milliseconds}"), "(item cooldown)");
    }

    private int ProvisionCount() => BagCount(ProvisionItem) + Cargo.Values.Where(stack => stack.Name == ProvisionItem).Sum(stack => stack.Amount);

    private void ConsumeSupplies()
    {
        if (++_supplyClock < Math.Max(1, Sea.Data.Rules.SupplyDaySeconds)) return;
        _supplyClock = 0;
        int owed = Math.Min(SupplyCost, ProvisionCount());
        foreach (int view in new[] { ViewId.Cargo, ViewId.BagItems })
        {
            Dictionary<int, ItemStack> slots = view == ViewId.Cargo ? Cargo : Items;
            foreach (int slot in slots.Where(pair => pair.Value.Name == ProvisionItem).Select(pair => pair.Key).Order().ToList())
            {
                if (owed <= 0) break;
                int taken = Math.Min(owed, slots[slot].Amount);
                TakeFrom(view, slot, taken, "(provisions eaten)");
                owed -= taken;
            }
        }
        if (ProvisionCount() > 0 || _suppliesWarned) return;
        _suppliesWarned = true;
        SystemMessage("Provision Insufficient, please replenish as soon as possible...", SystemMessageKind.Prompt);
    }

    private void GainVoyageExperience()
    {
        (float x, float z) = Mover!.Now();
        if (MathF.Abs(x - Mover.DestinationX) < 0.01f && MathF.Abs(z - Mover.DestinationZ) < 0.01f) return;
        if (++_voyageClock < Math.Max(1, Rule.Skills.VoyageExperienceSeconds)) return;
        _voyageClock = 0;
        GainSkillExperience("SKILL_NAVIGATE", Rule.Skills.VoyageExperience);
        GainShipExperience(ShipAttribute.Move, Rule.Skills.ManeuverExperience);
    }

    private static bool InHold(ItemStack item)
        => item.Name == ProvisionItem || ItemCatalog.Number(item.Name, "ItemType") == SeaPackets.ItemTypeGoods;

    private static int Weight(ItemStack item) => ItemCatalog.Number(item.Name, "Weight") * item.Amount;
}
