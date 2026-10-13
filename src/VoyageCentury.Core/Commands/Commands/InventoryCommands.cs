using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Sessions;

namespace VoyageCentury.Core.Commands.Commands;

internal static class Arguments
{
    public static bool Numbers(string[] words, int count, out int[] values)
    {
        values = new int[count];
        if (words.Length != count + 1) return false;
        for (int i = 0; i < count; i++)
        {
            if (!int.TryParse(words[i + 1], out values[i])) return false;
        }
        return true;
    }
}

public sealed class SelectCommand : ITextCommand
{
    private const int LeftClick = 0;
    private const int RightClick = 2;

    public IEnumerable<string> Names => ["Select"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (!Arguments.Numbers(words, 3, out int[] values)) return false;
        (int view, int how, int index) = (values[0], values[1], values[2]);
        if (session.OnTradeSelect(view, how, index) || session.OnStallSelect(view, how, index)) return true;
        if (session.OnWarehouseSelect(view, how, index)) return true;

        if (how == RightClick)
        {
            if (session.MoveGear(view, index)) return true;
            return view == ViewId.BagItems && session.UseItem(index);
        }
        if (how != LeftClick) return false;
        if (session.ShipyardBuy(view, index, 1)) return true;
        if (view == Protocol.Packets.SeaPackets.AmmunitionView)
        {
            session.SelectAmmunition(index);
            return true;
        }

        if (session.ShopOpen && session.BuyOne(view, index)) return true;
        if (view == ViewId.BagItems && session.Items.TryGetValue(index, out ItemStack? item))
        {
            if (session.ShopOpen) return session.SellFromBag(index, 1);
            session.SetHand(session.Hand == null ? item.Clone() : null);
            return true;
        }
        session.SetHand(null);
        return true;
    }
}

public sealed class SellCommand : ITextCommand
{
    public IEnumerable<string> Names => ["Sell"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (!Arguments.Numbers(words, 4, out int[] values)) return false;
        if (values[0] == ViewId.Booth)
        {
            session.SellToStall(values[1], values[2], values[3]);
            return true;
        }
        if (values[1] == ViewId.BagItems) session.SellFromBag(values[2], values[3]);
        return true;
    }
}

public sealed class BuyCommand : ITextCommand
{
    private const int MostAtOnce = 99;

    public IEnumerable<string> Names => ["Buy"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (!Arguments.Numbers(words, 3, out int[] values)) return false;
        if (values[0] == ViewId.Booth)
        {
            session.BuyFromStall(values[1], values[2]);
            return true;
        }
        if (session.ShipyardBuy(values[0], values[1], values[2])) return true;
        for (int i = 0; i < Math.Min(values[2], MostAtOnce); i++)
        {
            if (!session.BuyOne(values[0], values[1])) break;
        }
        return true;
    }
}

public sealed class RemoveItemCommand : ITextCommand
{
    public IEnumerable<string> Names => ["RemoveItem"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (!Arguments.Numbers(words, 2, out int[] values)) return false;
        if (values[0] == ViewId.BagItems) session.ThrowAway(values[1]);
        return true;
    }
}

public sealed class ShortcutCommands : ITextCommand
{
    public IEnumerable<string> Names => ["HandToShortcut", "AddShortcut", "RemoveShortcut", "ShortcutPage"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "ShortcutPage":
                if (words.Length == 2 && int.TryParse(words[1], out int page))
                {
                    session.Character.ShortcutPage = page;
                    session.SetProperty("ShortcutPage", page);
                    session.Save();
                }
                return true;
            case "HandToShortcut" when words.Length == 2 && int.TryParse(words[1], out int slot):
                if (session.Hand != null)
                {
                    session.Character.Shortcuts[slot] = new ShortcutEntry { Kind = ViewId.BagItems, Name = session.Hand.Name };
                    session.SetHand(null);
                    session.SendShortcuts();
                }
                return true;
            case "AddShortcut" when words.Length >= 4 && int.TryParse(words[1], out int slot) && int.TryParse(words[2], out int kind):
                session.Character.Shortcuts[slot] = new ShortcutEntry { Kind = kind, Name = string.Join(' ', words.Skip(3)) };
                session.SendShortcuts();
                return true;
            case "RemoveShortcut" when words.Length == 2 && int.TryParse(words[1], out int row):
                List<int> keys = session.Character.Shortcuts.Keys.Order().ToList();
                if (row >= 0 && row < keys.Count) session.Character.Shortcuts.Remove(keys[row]);
                session.SendShortcuts();
                return true;
            default:
                return false;
        }
    }
}
