using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed class TradeOffer
{
    public int BagSlot { get; init; }
    public string Name { get; init; } = "";
    public int Photo { get; init; }
    public int Amount { get; set; }
}

public sealed partial class ClientSession
{
    public const int TradeSlots = 6;
    public const float TradeRange = 20f;

    private readonly Dictionary<int, TradeOffer> _tradeOffer = [];
    private ClientSession? _tradePartner;
    private ClientSession? _tradeRequested;
    private int _tradeMoney;
    private bool _tradeReady;
    private bool _tradeOk;

    public int ExchangeTargetId { get; set; }

    public bool ExchangeEnabled { get; private set; } = true;

    public bool Trading => _tradePartner != null;

    public void SwitchExchange()
    {
        ExchangeEnabled = !ExchangeEnabled;
        SetProperty("EnableExchange", ExchangeEnabled ? 1 : 0);
        SystemMessage(ExchangeEnabled ? "You now accept trade requests." : "You now refuse trade requests.");
    }

    public void RequestTrade(ClientSession? target)
    {
        if (target == null || target == this || !target.InWorld || target.Scene != Scene)
        {
            SystemMessage("Select the player you want to trade with first.");
            return;
        }
        if (Trading)
        {
            SystemMessage("You are already trading.");
            return;
        }
        if (StallOpen || target.StallOpen)
        {
            SystemMessage("Trading is not possible while a stall is open.");
            return;
        }
        if (target.Trading)
        {
            SystemMessage($"{target.Name} is busy trading.");
            return;
        }
        if (!WithinTradeRange(target))
        {
            SystemMessage($"{target.Name} is too far away.");
            return;
        }
        if (target._tradeRequested == this)
        {
            OpenTrade(this, target);
            return;
        }
        if (!target.ExchangeEnabled)
        {
            SystemMessage($"{target.Name} refuses trade requests.");
            return;
        }
        _tradeRequested = target;
        SystemMessage($"You asked {target.Name} to trade. The trade starts when {target.Name} selects you and clicks Exchange.");
        target.SystemMessage($"{Name} wants to trade with you. Select {Name} and click Exchange to accept.");
    }

    public void OfferTradeItem(int view, int index, int amount)
    {
        ClientSession? partner = _tradePartner;
        if (partner == null) return;
        if (_tradeReady)
        {
            SystemMessage("Untick Confirm before changing your offer.");
            return;
        }
        if (view != ViewId.BagItems)
        {
            SystemMessage("Only articles from the Item tab can be traded.");
            return;
        }
        if (!Items.TryGetValue(index, out ItemStack? item)) return;

        int slot = _tradeOffer.FirstOrDefault(pair => pair.Value.BagSlot == index).Key;
        int offered = slot == 0 ? 0 : _tradeOffer[slot].Amount;
        int available = item.Amount - offered;
        if (amount <= 0) amount = available;
        if (available <= 0 || amount > available)
        {
            SystemMessage("You do not have that many.");
            return;
        }

        if (slot != 0)
        {
            TradeOffer offer = _tradeOffer[slot];
            offer.Amount += amount;
            var update = new PropertyBag { ["Amount"] = offer.Amount };
            Send(ViewPackets.UpdateItem(ViewId.TradeMine, slot, update), "(trade offer)");
            partner.Send(ViewPackets.UpdateItem(ViewId.TradeTheirs, slot, update), "(trade offer)");
        }
        else
        {
            slot = FreeTradeSlot();
            if (slot == 0)
            {
                SystemMessage("The trade window is full.");
                return;
            }
            var offer = new TradeOffer { BagSlot = index, Name = item.Name, Photo = item.Photo, Amount = amount };
            _tradeOffer[slot] = offer;
            PropertyBag shown = ItemProperties(new ItemStack { Name = offer.Name, Photo = offer.Photo, Amount = offer.Amount }, slot);
            Send(ViewPackets.AddItem(ViewId.TradeMine, slot, shown), "(trade offer)");
            partner.Send(ViewPackets.AddItem(ViewId.TradeTheirs, slot, shown), "(trade offer)");
        }
        ResetTradeConfirmation();
    }

    public void TakeBackTradeItem(int slot)
    {
        ClientSession? partner = _tradePartner;
        if (partner == null || !_tradeOffer.ContainsKey(slot)) return;
        if (_tradeReady)
        {
            SystemMessage("Untick Confirm before changing your offer.");
            return;
        }
        _tradeOffer.Remove(slot);
        Send(ViewPackets.RemoveItem(ViewId.TradeMine, slot), "(trade offer removed)");
        partner.Send(ViewPackets.RemoveItem(ViewId.TradeTheirs, slot), "(trade offer removed)");
        ResetTradeConfirmation();
    }

    public void OfferTradeMoney(int amount)
    {
        ClientSession? partner = _tradePartner;
        if (partner == null)
        {
            SystemMessage("You are not trading.");
            return;
        }
        if (_tradeReady)
        {
            SystemMessage("Untick Confirm before changing your offer.");
            return;
        }
        if (amount < 0 || amount > Money)
        {
            SystemMessage("Not enough money.");
            return;
        }
        _tradeMoney = amount;
        SystemMessage($"You offer {amount} gold.");
        partner.SystemMessage($"{Name} offers {amount} gold.");
        ResetTradeConfirmation();
    }

    public void SetTradeReady(bool ready)
    {
        ClientSession? partner = _tradePartner;
        if (partner == null) return;
        _tradeReady = ready;
        if (!ready)
        {
            _tradeOk = false;
            partner._tradeOk = false;
        }
        SendTradeState();
        partner.SendTradeState();
    }

    public void ConfirmTrade()
    {
        ClientSession? partner = _tradePartner;
        if (partner == null) return;
        if (!_tradeReady || !partner._tradeReady)
        {
            SystemMessage("Both sides have to tick Confirm before the trade can be completed.");
            SendTradeState();
            return;
        }
        _tradeOk = true;
        SendTradeState();
        if (partner._tradeOk) CompleteTrade(partner);
        else partner.SystemMessage($"{Name} is ready to complete the trade. Click OK to finish.");
    }

    public void CancelTrade(string? reason = null)
    {
        ClientSession? partner = _tradePartner;
        if (partner == null) return;
        EndTrade();
        partner.EndTrade();
        SystemMessage(reason ?? "The trade was cancelled.");
        partner.SystemMessage(reason ?? $"{Name} cancelled the trade.");
        Log.Info(Tag, $"{Name} and {partner.Name} cancel their trade");
    }

    public bool OnTradeSelect(int view, int how, int index)
    {
        if (view is not (ViewId.TradeMine or ViewId.TradeTheirs)) return false;
        if (view == ViewId.TradeMine && Trading) TakeBackTradeItem(index);
        return true;
    }

    public void CancelTradeAndStall()
    {
        CancelTrade();
        _tradeRequested = null;
        foreach (ClientSession other in Context.World.Sessions)
        {
            if (other._tradeRequested == this) other._tradeRequested = null;
        }
        StopStall();
        CloseBooth();
    }

    private static void OpenTrade(ClientSession first, ClientSession second)
    {
        first.BeginTrade(second);
        second.BeginTrade(first);
        Log.Info(first.Tag, $"{first.Name} and {second.Name} start trading");
    }

    private void BeginTrade(ClientSession partner)
    {
        CloseBooth();
        _tradePartner = partner;
        _tradeRequested = null;
        _tradeOffer.Clear();
        _tradeMoney = 0;
        _tradeReady = false;
        _tradeOk = false;
        Send(ViewPackets.Create(ViewId.TradeTheirs, TradeSlots), "(trade view theirs)");
        Send(ViewPackets.Create(ViewId.TradeMine, TradeSlots), "(trade view mine)");
        SendTradeState();
        SystemMessage($"You are trading with {partner.Name}.");
    }

    private void EndTrade()
    {
        if (_tradePartner == null) return;
        _tradePartner = null;
        _tradeOffer.Clear();
        _tradeMoney = 0;
        _tradeReady = false;
        _tradeOk = false;
        Send(ViewPackets.Delete(ViewId.TradeMine), "(close trade)");
        Send(ViewPackets.Delete(ViewId.TradeTheirs), "(close trade)");
    }

    private void SendTradeState()
    {
        ClientSession? partner = _tradePartner;
        if (partner == null) return;
        Send(ViewPackets.Properties(ViewId.TradeMine, new PropertyBag
        {
            ["IsReady"] = _tradeReady ? 1 : 0,
            ["IsOk"] = _tradeOk ? 1 : 0,
            ["Money"] = _tradeMoney,
        }), "(trade state mine)");
        Send(ViewPackets.Properties(ViewId.TradeTheirs, new PropertyBag
        {
            ["IsReady"] = partner._tradeReady ? 1 : 0,
            ["Money"] = partner._tradeMoney,
        }), "(trade state theirs)");
    }

    private void ResetTradeConfirmation()
    {
        ClientSession? partner = _tradePartner;
        if (partner == null) return;
        bool changed = _tradeReady || _tradeOk || partner._tradeReady || partner._tradeOk;
        _tradeReady = false;
        _tradeOk = false;
        partner._tradeReady = false;
        partner._tradeOk = false;
        if (!changed) return;
        SendTradeState();
        partner.SendTradeState();
    }

    private void CompleteTrade(ClientSession partner)
    {
        string? problem = TradeProblem(partner) ?? partner.TradeProblem(this);
        if (problem != null)
        {
            ResetTradeConfirmation();
            SystemMessage(problem);
            partner.SystemMessage(problem);
            return;
        }

        List<TradeOffer> mine = _tradeOffer.Values.ToList();
        List<TradeOffer> theirs = partner._tradeOffer.Values.ToList();
        int myMoney = _tradeMoney;
        int theirMoney = partner._tradeMoney;

        EndTrade();
        partner.EndTrade();

        foreach (TradeOffer offer in mine) TakeFromBag(offer.BagSlot, offer.Amount, "(traded away)");
        foreach (TradeOffer offer in theirs) partner.TakeFromBag(offer.BagSlot, offer.Amount, "(traded away)");
        foreach (TradeOffer offer in mine) partner.ReceiveIntoBag(offer.Name, offer.Photo, offer.Amount, "(traded item)");
        foreach (TradeOffer offer in theirs) ReceiveIntoBag(offer.Name, offer.Photo, offer.Amount, "(traded item)");
        if (myMoney != theirMoney)
        {
            SetMoney(Money - myMoney + theirMoney);
            partner.SetMoney(partner.Money - theirMoney + myMoney);
        }

        Save();
        partner.Save();
        SystemMessage($"The trade with {partner.Name} is complete.");
        partner.SystemMessage($"The trade with {Name} is complete.");
        Log.Info(Tag, $"{Name} trades {Describe(mine, myMoney)} for {Describe(theirs, theirMoney)} with {partner.Name}");
    }

    private string? TradeProblem(ClientSession receiver)
    {
        if (!InWorld || !receiver.InWorld || Scene != receiver.Scene) return "The trade failed: the other player is gone.";
        if (_tradeMoney < 0 || _tradeMoney > Money) return $"The trade failed: {Name} does not have enough money.";
        if ((long)receiver.Money - receiver._tradeMoney + _tradeMoney > int.MaxValue) return $"The trade failed: {receiver.Name} cannot carry that much money.";
        foreach (TradeOffer offer in _tradeOffer.Values)
        {
            if (offer.Amount <= 0 || !Items.TryGetValue(offer.BagSlot, out ItemStack? item) || item.Name != offer.Name || item.Amount < offer.Amount)
                return $"The trade failed: {Name} no longer has the offered articles.";
        }

        Dictionary<int, (string Name, int Amount)> bag = receiver.Items.ToDictionary(pair => pair.Key, pair => (pair.Value.Name, pair.Value.Amount));
        foreach (TradeOffer offer in receiver._tradeOffer.Values)
        {
            if (!bag.TryGetValue(offer.BagSlot, out (string Name, int Amount) held)) continue;
            if (held.Amount <= offer.Amount) bag.Remove(offer.BagSlot);
            else bag[offer.BagSlot] = (held.Name, held.Amount - offer.Amount);
        }
        foreach (TradeOffer offer in _tradeOffer.Values)
        {
            if (bag.Values.Any(held => held.Name == offer.Name)) continue;
            int free = Enumerable.Range(1, ViewId.BagSlots).FirstOrDefault(slot => !bag.ContainsKey(slot));
            if (free == 0) return $"The trade failed: the bag of {receiver.Name} is full.";
            bag[free] = (offer.Name, offer.Amount);
        }
        return null;
    }

    private bool ReceiveIntoBag(string name, int photo, int amount, string note)
    {
        int slot = Items.FirstOrDefault(pair => pair.Value.Name == name).Key;
        if (slot != 0)
        {
            Items[slot].Amount += amount;
            Send(ViewPackets.UpdateItem(ViewId.BagItems, slot, new PropertyBag { ["Amount"] = Items[slot].Amount }), note);
            return true;
        }
        slot = FreeSlot(Items, ViewId.BagSlots);
        if (slot == 0) return false;
        Items[slot] = new ItemStack { Name = name, Photo = photo, Amount = amount };
        Send(ViewPackets.AddItem(ViewId.BagItems, slot, ItemProperties(Items[slot], slot)), note);
        return true;
    }

    private bool HasRoomFor(string name)
        => Items.Values.Any(item => item.Name == name) || FreeSlot(Items, ViewId.BagSlots) != 0;

    private bool WithinTradeRange(ClientSession other)
    {
        if (Mover == null || other.Mover == null) return false;
        (float x, float z) = Mover.Now();
        (float otherX, float otherZ) = other.Mover.Now();
        float dx = x - otherX;
        float dz = z - otherZ;
        return dx * dx + dz * dz <= TradeRange * TradeRange;
    }

    private int FreeTradeSlot()
    {
        for (int slot = 1; slot <= TradeSlots; slot++)
        {
            if (!_tradeOffer.ContainsKey(slot)) return slot;
        }
        return 0;
    }

    private static string Describe(List<TradeOffer> offers, int money)
    {
        List<string> parts = offers.Select(offer => $"{offer.Amount} x {offer.Name}").ToList();
        if (money > 0) parts.Add($"{money} gold");
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }
}
