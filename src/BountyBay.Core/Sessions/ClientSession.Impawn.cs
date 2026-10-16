using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int ImpawnView = 89;

    private const int ImpawnInputSlot = 1;
    private const int ImpawnOutputSlot = 2;

    private readonly List<DateTime> _impawnCancels = [];
    private (int View, int Index, string Name, int Amount)? _impawnItem;
    private ItemStack? _impawnCoins;
    private long _impawnSilver;
    private bool _impawnOpen;
    private bool _impawnInputShown;
    private bool _impawnOutputShown;
    private DateTime _impawnBannedUntil;

    public bool ImpawnOpen => _impawnOpen;

    public void OpenImpawn()
    {
        if (!_impawnOpen)
        {
            _impawnOpen = true;
            _impawnInputShown = false;
            _impawnOutputShown = false;
            Send(ViewPackets.Create(ImpawnView, 2), "(pawn window)");
        }
        ClearImpawnOffer();
        ClearImpawnInput();
        Send(AuctionPackets.ImpawnDone(), "(pawn buttons reset)");
        Log.Debug(Tag, $"{Name} opens the pawn shop");
    }

    public void CloseImpawn()
    {
        if (!_impawnOpen) return;
        ClearImpawnOffer();
        ClearImpawnInput();
        _impawnOpen = false;
        Send(ViewPackets.Delete(ImpawnView), "(close pawn window)");
    }

    public bool ImpawnSlotClick(int index)
    {
        if (!_impawnOpen) return false;
        if (index == ImpawnInputSlot && Hand != null && HandSource() is { } from)
        {
            int amount = Hand.Amount;
            DropHand();
            PutImpawn(from.View, from.Index, amount);
            return true;
        }
        DropHand();
        if (index == ImpawnInputSlot && _impawnItem != null)
        {
            ClearImpawnOffer();
            ClearImpawnInput();
            Send(AuctionPackets.ImpawnDone(), "(pawn slot emptied)");
        }
        return true;
    }

    public void ImpawnBargain()
    {
        if (!_impawnOpen) return;
        if (DateTime.UtcNow < _impawnBannedUntil)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((_impawnBannedUntil - DateTime.UtcNow).TotalMinutes));
            SystemMessage($"You have haggled too often. The pawnbroker will not talk prices with you for {minutes} minute(s).", SystemMessageKind.Prompt);
            Send(AuctionPackets.ImpawnCancel(), "(pawn bargain refused)");
            return;
        }
        if (ImpawnStack() is not { } item)
        {
            ClearImpawnInput();
            SystemMessage("Put the articles you want to pawn into the slot first.", SystemMessageKind.Prompt);
            Send(AuctionPackets.ImpawnDone(), "(pawn bargain refused)");
            return;
        }
        ImpawnRules rules = AuctionRules.For(Context.Settings).Pawn;
        int amount = _impawnItem!.Value.Amount;
        long value = (long)Context.Data.PriceOf(item.Name) * amount;
        long silver = Math.Max(1, value * Random.Shared.Next(rules.MinPercent, rules.MaxPercent + 1) / 100);
        ClearImpawnOffer();
        if (rules.PayItem.Length > 0)
        {
            int coins = (int)Math.Clamp(silver / rules.SilverPerCoin, 1, int.MaxValue);
            _impawnCoins = new ItemStack { Name = rules.PayItem, Photo = Context.Data.PhotoOf(rules.PayItem), Amount = coins };
            _impawnOutputShown = true;
            Send(ViewPackets.AddItem(ImpawnView, ImpawnOutputSlot, ItemProperties(_impawnCoins, ImpawnOutputSlot)), "(pawn offer)");
            SystemMessage($"The pawnbroker offers {coins} x {AuctionHouse.Label(Context, rules.PayItem)} for {amount} x {AuctionHouse.Label(Context, item.Name)}.");
        }
        else
        {
            _impawnSilver = silver;
            SystemMessage($"The pawnbroker offers {silver} silver coins for {amount} x {AuctionHouse.Label(Context, item.Name)}.");
        }
        Send(AuctionPackets.ImpawnBargain(), "(pawn offer)");
        Log.Info(Tag, $"{Name} haggles over {amount} x {item.Name}: {(_impawnCoins != null ? $"{_impawnCoins.Amount} x {_impawnCoins.Name}" : $"{silver} silver")}");
    }

    public void ImpawnDone()
    {
        if (!_impawnOpen) return;
        if (_impawnCoins == null && _impawnSilver <= 0)
        {
            SystemMessage("Ask the pawnbroker for a price first.", SystemMessageKind.Prompt);
            return;
        }
        if (ImpawnStack() is not { } item)
        {
            ClearImpawnOffer();
            ClearImpawnInput();
            SystemMessage("The articles are no longer in your bag.", SystemMessageKind.Prompt);
            Send(AuctionPackets.ImpawnDone(), "(pawn deal refused)");
            return;
        }
        (int view, int index, string name, int amount) = _impawnItem!.Value;
        if (_impawnCoins != null)
        {
            if (!GiveItem(_impawnCoins.Clone(), "(pawn payment)"))
            {
                SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
                return;
            }
            Log.Info(Tag, $"{Name} pawns {amount} x {name} for {_impawnCoins.Amount} x {_impawnCoins.Name}");
            SystemMessage($"You pawned {amount} x {AuctionHouse.Label(Context, item.Name)} for {_impawnCoins.Amount} x {AuctionHouse.Label(Context, _impawnCoins.Name)}.");
        }
        else
        {
            SetMoney(Money + _impawnSilver);
            Log.Info(Tag, $"{Name} pawns {amount} x {name} for {_impawnSilver} silver");
            SystemMessage($"You pawned {amount} x {AuctionHouse.Label(Context, item.Name)} for {_impawnSilver} silver coins.");
        }
        TakeFrom(view, index, amount, "(article pawned)");
        ClearImpawnOffer();
        ClearImpawnInput();
        RefreshLoad();
        Save();
        Send(AuctionPackets.ImpawnDone(), "(pawn deal)");
    }

    public void ImpawnCancel()
    {
        if (!_impawnOpen) return;
        bool offered = _impawnCoins != null || _impawnSilver > 0;
        ClearImpawnOffer();
        if (offered)
        {
            ImpawnRules rules = AuctionRules.For(Context.Settings).Pawn;
            DateTime now = DateTime.UtcNow;
            _impawnCancels.Add(now);
            _impawnCancels.RemoveAll(at => at < now.AddMinutes(-rules.CancelWindowMinutes));
            if (rules.MaxCancels > 0 && _impawnCancels.Count >= rules.MaxCancels)
            {
                _impawnCancels.Clear();
                _impawnBannedUntil = now.AddMinutes(rules.BanMinutes);
                SystemMessage($"You have haggled too often. The pawnbroker will not talk prices with you for {rules.BanMinutes} minute(s).", SystemMessageKind.Prompt);
                Log.Info(Tag, $"{Name} is barred from haggling for {rules.BanMinutes} minute(s)");
            }
        }
        Send(_impawnItem != null ? AuctionPackets.ImpawnCancel() : AuctionPackets.ImpawnDone(), "(pawn offer withdrawn)");
    }

    private void PutImpawn(int view, int index, int amount)
    {
        if (MailBagOf(view) is not { } bag || !bag.TryGetValue(index, out ItemStack? item)) return;
        ImpawnRules rules = AuctionRules.For(Context.Settings).Pawn;
        if (item.Name == rules.PayItem || rules.Refuse.Contains(item.Name, StringComparer.OrdinalIgnoreCase)
            || item.Name == LetterPaper.Name || Context.Data.PriceOf(item.Name) <= 0)
        {
            SystemMessage("The pawnbroker does not take that article.", SystemMessageKind.Prompt);
            return;
        }
        ClearImpawnOffer();
        ClearImpawnInput();
        amount = Math.Clamp(amount, 1, item.Amount);
        _impawnItem = (view, index, item.Name, amount);
        _impawnInputShown = true;
        var shown = new ItemStack { Name = item.Name, Photo = item.Photo, Amount = amount, Cost = item.Cost };
        Send(ViewPackets.AddItem(ImpawnView, ImpawnInputSlot, ItemProperties(shown, ImpawnInputSlot)), "(pawn article)");
        Send(AuctionPackets.ImpawnCancel(), "(pawn bargain ready)");
    }

    private ItemStack? ImpawnStack()
    {
        if (_impawnItem is not { } slot || MailBagOf(slot.View) is not { } bag || !bag.TryGetValue(slot.Index, out ItemStack? item)
            || item.Name != slot.Name || item.Amount < slot.Amount)
            return null;
        return item;
    }

    private void ClearImpawnOffer()
    {
        _impawnCoins = null;
        _impawnSilver = 0;
        if (!_impawnOutputShown) return;
        _impawnOutputShown = false;
        Send(ViewPackets.RemoveItem(ImpawnView, ImpawnOutputSlot), "(pawn offer cleared)");
    }

    private void ClearImpawnInput()
    {
        _impawnItem = null;
        if (!_impawnInputShown) return;
        _impawnInputShown = false;
        Send(ViewPackets.RemoveItem(ImpawnView, ImpawnInputSlot), "(pawn slot cleared)");
    }
}
