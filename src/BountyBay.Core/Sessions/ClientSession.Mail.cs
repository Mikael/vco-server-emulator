using System.Globalization;
using BountyBay.Core.Logging;
using BountyBay.Core.Network;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class MailTables
{
    public static readonly TableDefinition ShortMessage = new(13, "ShortMessage",
    [
        new("Id", PropertyType.U32),
        new("Saved", PropertyType.U32),
        new("Sender", PropertyType.String),
        new("PaperType", PropertyType.U32),
        new("Date", PropertyType.String),
        new("Content", PropertyType.String),
    ]);

    public static readonly TableDefinition RecvItem = new(14, "RecvItem",
    [
        new("Mailer", PropertyType.String),
        new("Time", PropertyType.String),
        new("LeaveWord", PropertyType.String),
        new("Index", PropertyType.U32),
    ]);

    public static readonly TableDefinition[] All = [ShortMessage, RecvItem];
}

public static class LetterPaper
{
    public const string Name = "IDName00004391";
    public const int Kind = 0x17;
    public const int PaperType = 0;
    public const int MaxLength = 70;
}

public static class MailDirectory
{
    public static CharacterRecord? Find(ServerContext context, string name)
        => context.World.FindPlayer(name)?.Character ?? context.Accounts.FindCharacter(name);
}

public sealed partial class ClientSession
{
    public const int ParcelView = 29;
    public const int ParcelSlots = 3;
    public const int MostLetters = 30;
    public const int MostParcels = 12;
    public const int MostRecipients = 10;
    public const int ParcelMessageLength = 40;

    private const int ParcelToBag = 1;
    private const int ParcelToDepot = 2;
    private const int ParcelToCabin = 3;

    private bool _parcelViewOpen;
    private int _parcelsShown;

    public static void ApplyLetterPaper(PropertyBag bag, string name)
    {
        if (name != LetterPaper.Name) return;
        bag["Kind"] = LetterPaper.Kind;
        bag["PaperType"] = LetterPaper.PaperType;
        bag["MaxLen"] = LetterPaper.MaxLength;
    }

    public void SendMail()
    {
        List<object[]> rows = Character.Letters.Where(letter => letter.Delivered).Select(LetterRow).ToList();
        if (rows.Count > 0) Send(TablePackets.Rows(MailTables.ShortMessage, rows), "(letters)");
        AnnounceLetters();
        if (Character.Parcels.Count == 0) return;
        SendParcels();
        Send(ChatPackets.Script(0, "HaveRecvItem"), "(parcel waiting)");
    }

    public void AnnounceLetters()
    {
        int waiting = Character.Letters.Count(letter => !letter.Delivered);
        if (waiting > 0) Send(ChatPackets.Script(0, $"HaveSM {waiting}"), "(letters waiting)");
    }

    public void OpenParcelDesk() => Send(ChatPackets.Script(0, "OpenSendItem"), "(parcel desk)");

    public void ReceiveMail()
    {
        List<LetterEntry> arrived = Character.Letters.Where(letter => !letter.Delivered).ToList();
        if (arrived.Count > 0)
        {
            foreach (LetterEntry letter in arrived) letter.Delivered = true;
            Send(TablePackets.Rows(MailTables.ShortMessage, arrived.Select(LetterRow).ToList()), "(letters)");
            Send(ChatPackets.Script(0, "GotSM"), "(letters arrived)");
            Save();
            Log.Info(Tag, $"{Name} receives {arrived.Count} letter(s)");
        }
        if (Character.Parcels.Count > 0)
        {
            SendParcels();
            Send(ChatPackets.Script(0, "mail"), "(parcel window)");
        }
        else if (arrived.Count == 0)
        {
            SystemMessage("There is no mail for you.");
        }
    }

    public void SendLetter(int view, int index, string recipient, string content)
    {
        if (view != ViewId.BagItems || !Items.TryGetValue(index, out ItemStack? paper) || paper.Name != LetterPaper.Name)
        {
            SystemMessage("You need letter paper to write a letter.");
            return;
        }
        string[] names = recipient.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0 || names.Length > MostRecipients)
        {
            SystemMessage($"A letter can go to at most {MostRecipients} names, separated by semicolons without spaces.");
            return;
        }
        var targets = new List<CharacterRecord>();
        foreach (string name in names)
        {
            CharacterRecord? target = MailDirectory.Find(Context, name);
            if (target == null)
            {
                SystemMessage($"Nobody called {name} is known here.");
                return;
            }
            if (target.Blacklist.Contains(Name))
            {
                SystemMessage($"{target.Name} does not accept your letters.");
                return;
            }
            if (target.Letters.Count >= MostLetters)
            {
                SystemMessage($"The message book of {target.Name} is full.");
                return;
            }
            targets.Add(target);
        }
        if (content.Length > LetterPaper.MaxLength)
        {
            content = content[..LetterPaper.MaxLength];
            SystemMessage($"Input no more than {LetterPaper.MaxLength} characters: your letter was shortened.");
        }
        foreach (CharacterRecord target in targets)
        {
            target.Letters.Add(new LetterEntry
            {
                Id = target.NextLetterId++,
                Sender = Name,
                PaperType = LetterPaper.PaperType,
                Date = MailDate(),
                Content = content,
            });
        }
        MailTake(ViewId.BagItems, index, 1, "(letter paper used)");
        Context.Accounts.Save();
        SystemMessage($"Your letter to {string.Join(", ", targets.Select(target => target.Name))} has been sent.");
        foreach (CharacterRecord target in targets) Context.World.FindPlayer(target.Name)?.AnnounceLetters();
        Log.Info(Tag, $"{Name} writes to {string.Join(", ", targets.Select(target => target.Name))} ({content.Length} characters)");
    }

    public void KeepLetter(int id)
    {
        LetterEntry? letter = Character.Letters.FirstOrDefault(entry => entry.Id == id);
        if (letter == null) return;
        letter.Saved = true;
        Save();
    }

    public void DiscardLetter(int id)
    {
        if (Character.Letters.RemoveAll(entry => entry.Id == id) > 0) Save();
    }

    public void SendParcel(int view, int index, int amount, string recipient, string message)
    {
        Dictionary<int, ItemStack>? source = MailBagOf(view);
        if (source == null || !source.TryGetValue(index, out ItemStack? item) || amount <= 0 || amount > item.Amount)
        {
            SystemMessage("That cannot be mailed.");
            return;
        }
        CharacterRecord? target = MailDirectory.Find(Context, recipient);
        if (target == null)
        {
            SystemMessage($"Nobody called {recipient} is known here.");
            return;
        }
        if (target.Blacklist.Contains(Name))
        {
            SystemMessage($"{target.Name} does not accept your parcels.");
            return;
        }
        if (target.Parcels.Count >= MostParcels)
        {
            SystemMessage($"{target.Name} has too many parcels waiting.");
            return;
        }
        if (message.Length > ParcelMessageLength) message = message[..ParcelMessageLength];
        var sent = new ItemStack { Name = item.Name, Photo = item.Photo, Amount = amount };
        if (Hand != null && Hand.Name == item.Name) SetHand(null);
        MailTake(view, index, amount, "(item mailed)");
        target.Parcels.Add(new ParcelEntry { Sender = Name, Date = MailDate(), Message = message, View = view, Item = sent });
        Context.Accounts.Save();
        Send(new PacketWriter(ServerMsg.MailAcknowledge).ToArray(), "(parcel accepted)");
        SystemMessage($"Your parcel for {target.Name} is on its way.");
        ClientSession? online = Context.World.FindPlayer(target.Name);
        if (online != null)
        {
            online.SendParcels();
            online.Send(ChatPackets.Script(0, "HaveRecvItem"), "(parcel waiting)");
        }
        Log.Info(Tag, $"{Name} mails {amount} x {sent.Name} to {target.Name}");
    }

    public void TakeParcel(int slot, int destination)
    {
        if (slot < 1 || slot > Math.Min(ParcelSlots, Character.Parcels.Count)) return;
        ParcelEntry parcel = Character.Parcels[slot - 1];
        switch (destination)
        {
            case ParcelToBag:
                int view = MailBagOf(parcel.View) != null ? parcel.View : ViewId.BagItems;
                if (!MailStore(view, parcel.Item.Clone(), "(parcel item)"))
                {
                    SystemMessage("Your bag is full.");
                    return;
                }
                Log.Info(Tag, $"{Name} takes {parcel.Item.Amount} x {parcel.Item.Name} sent by {parcel.Sender}");
                break;
            case ParcelToDepot:
                if (!WarehouseStore(parcel.Item.Clone()))
                {
                    SystemMessage("Your warehouse is full.");
                    return;
                }
                Log.Info(Tag, $"{Name} sends {parcel.Item.Amount} x {parcel.Item.Name} from {parcel.Sender} to the warehouse");
                break;
            case ParcelToCabin:
                SystemMessage("Parcels can only be taken into your bag or your warehouse here.");
                return;
            default:
                Log.Info(Tag, $"{Name} destroys the parcel sent by {parcel.Sender}");
                break;
        }
        Character.Parcels.RemoveAt(slot - 1);
        SendParcels();
        Save();
    }

    public void SendParcels()
    {
        if (!_parcelViewOpen)
        {
            _parcelViewOpen = true;
            Send(ViewPackets.Create(ParcelView, ParcelSlots), "(parcel view)");
        }
        for (int slot = 1; slot <= _parcelsShown; slot++)
            Send(ViewPackets.RemoveItem(ParcelView, slot), "(parcel slot cleared)");
        Send(TablePackets.Clear(MailTables.RecvItem), "(clear parcels)");
        _parcelsShown = Math.Min(ParcelSlots, Character.Parcels.Count);
        var rows = new List<object[]>();
        for (int slot = 1; slot <= _parcelsShown; slot++)
        {
            ParcelEntry parcel = Character.Parcels[slot - 1];
            Send(ViewPackets.AddItem(ParcelView, slot, ItemProperties(parcel.Item, slot)), $"(parcel {parcel.Item.Name})");
            rows.Add([parcel.Sender, parcel.Date, parcel.Message, slot]);
        }
        if (rows.Count > 0) Send(TablePackets.Rows(MailTables.RecvItem, rows), "(parcels)");
    }

    private static object[] LetterRow(LetterEntry letter)
        => [letter.Id, letter.Saved ? 1 : 0, letter.Sender, letter.PaperType, letter.Date, letter.Content];

    private static string MailDate() => DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private Dictionary<int, ItemStack>? MailBagOf(int view) => view switch
    {
        ViewId.BagItems => Items,
        ViewId.BagEquip or ViewId.BagArticle => Bags[view],
        _ => null,
    };

    private void MailTake(int view, int index, int amount, string note)
    {
        Dictionary<int, ItemStack> slots = MailBagOf(view)!;
        ItemStack item = slots[index];
        item.Amount -= amount;
        if (item.Amount > 0)
        {
            Send(ViewPackets.UpdateItem(view, index, new PropertyBag { ["Amount"] = item.Amount }), note);
        }
        else
        {
            slots.Remove(index);
            Send(ViewPackets.RemoveItem(view, index), note);
        }
    }

    private bool MailStore(int view, ItemStack item, string note)
    {
        Dictionary<int, ItemStack> slots = MailBagOf(view)!;
        if (view == ViewId.BagItems)
        {
            foreach ((int used, ItemStack stack) in slots)
            {
                if (stack.Name != item.Name) continue;
                stack.Amount += item.Amount;
                Send(ViewPackets.UpdateItem(view, used, new PropertyBag { ["Amount"] = stack.Amount }), note);
                return true;
            }
        }
        int slot = FreeSlot(slots, ViewId.BagSlots);
        if (slot == 0) return false;
        slots[slot] = item;
        Send(ViewPackets.AddItem(view, slot, ItemProperties(item, slot)), note);
        return true;
    }
}
