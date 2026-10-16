using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int RoleBankBankBoxView = 30;
    private const int RoleBankRowsPerMessage = 8;
    private const string RoleBankBinding = "0";

    private int _roleBankShown;

    private RoleBankData RoleBankRules => RoleBankCatalog.Get(Context.Settings);

    public void RoleBankList(int id)
    {
        EnsureRoleBank();
        RoleBankIndex? index = id == 0 ? Character.RoleBank[0] : RoleBankFind(id);
        if (index == null) return;
        if (id == 0) SendRoleBankIndexes();
        ShowRoleBankIndex(index);
    }

    public void RoleBankPut(int id)
    {
        EnsureRoleBank();
        RoleBankIndex? index = RoleBankFind(id);
        if (index == null || Hand == null) return;
        if (HandSource() is not { } from || from.View is not (ViewId.BagItems or ViewId.BagArticle))
        {
            SystemMessage("Articles can only be put in the carrying bank from the item and props bars.", SystemMessageKind.Prompt);
            DropHand();
            return;
        }
        ItemStack held = MailBagOf(from.View)![from.Index];
        RoleBankArticle? article = RoleBankRules.Find(held.Name);
        if (article == null)
        {
            SystemMessage("This article cannot be put in the carrying bank.", SystemMessageKind.Prompt);
            DropHand();
            return;
        }
        int amount = Math.Clamp(Hand.Amount, 1, held.Amount);
        RoleBankEntry? entry = index.Entries.FirstOrDefault(stored => stored.Name == held.Name);
        if (entry == null && index.Entries.Count >= RoleBankRules.EntriesPerIndex)
        {
            SystemMessage("This page of the carrying bank is full.", SystemMessageKind.Prompt);
            DropHand();
            return;
        }
        if (entry == null)
        {
            entry = new RoleBankEntry { Key = Character.NextRoleBankKey++, Name = held.Name, Photo = article.Photo != 0 ? article.Photo : held.Photo };
            index.Entries.Add(entry);
        }
        entry.Amount += amount;
        DropHand();
        MailTake(from.View, from.Index, amount, "(put in the carrying bank)");
        Save();
        ShowRoleBankIndex(index);
        Log.Info(Tag, $"{Name} puts {amount} x {held.Name} in carrying bank {index.Id}");
    }

    public void RoleBankTake(int key, string item, int amount, int id)
    {
        EnsureRoleBank();
        RoleBankIndex? index = id == 0 ? RoleBankFind(_roleBankShown) : RoleBankFind(id);
        RoleBankEntry? entry = index?.Entries.FirstOrDefault(stored => stored.Key == key && stored.Name == item)
            ?? index?.Entries.FirstOrDefault(stored => stored.Name == item);
        if (index == null || entry == null || amount <= 0) return;
        amount = Math.Min(amount, entry.Amount);
        int view = ItemCatalog.Number(entry.Name, "ItemType") == SeaPackets.ItemTypeEquipment ? ViewId.BagEquip : ViewId.BagItems;
        if (!MailStore(view, new ItemStack { Name = entry.Name, Photo = entry.Photo, Amount = amount }, "(taken from the carrying bank)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        entry.Amount -= amount;
        if (entry.Amount <= 0) index.Entries.Remove(entry);
        Save();
        ShowRoleBankIndex(index);
        Log.Info(Tag, $"{Name} takes {amount} x {entry.Name} from carrying bank {index.Id}");
    }

    public void RoleBankBuild(string name)
    {
        EnsureRoleBank();
        name = RoleBankName(name);
        if (name.Length == 0) return;
        if (Character.RoleBank.Count >= RoleBankRules.Indexes)
        {
            SystemMessage($"The carrying bank holds at most {RoleBankRules.Indexes} labels.", SystemMessageKind.Prompt);
            return;
        }
        if (Character.RoleBank.Any(index => index.Name == name))
        {
            SystemMessage("A label with this name already exists.", SystemMessageKind.Prompt);
            return;
        }
        var created = new RoleBankIndex { Id = Character.NextRoleBankId++, Name = name };
        Character.RoleBank.Add(created);
        Save();
        SendRoleBankIndexes();
        ShowRoleBankIndex(created);
        Log.Info(Tag, $"{Name} adds carrying bank label {created.Id} '{name}'");
    }

    public void RoleBankRename(string oldName, string newName)
    {
        EnsureRoleBank();
        newName = RoleBankName(newName);
        RoleBankIndex? index = Character.RoleBank.FirstOrDefault(entry => entry.Name == oldName);
        if (index == null || newName.Length == 0) return;
        if (Character.RoleBank.Any(other => other != index && other.Name == newName))
        {
            SystemMessage("A label with this name already exists.", SystemMessageKind.Prompt);
            return;
        }
        index.Name = newName;
        Save();
        SendRoleBankIndexes();
    }

    public void RoleBankDelete(string name)
    {
        EnsureRoleBank();
        RoleBankIndex? index = Character.RoleBank.FirstOrDefault(entry => entry.Name == name);
        if (index == null) return;
        if (index == Character.RoleBank[0])
        {
            SystemMessage("The first label cannot be deleted.", SystemMessageKind.Prompt);
            return;
        }
        if (index.Entries.Count > 0)
        {
            SystemMessage("Take every article out of this label before deleting it.", SystemMessageKind.Prompt);
            return;
        }
        Character.RoleBank.Remove(index);
        Save();
        SendRoleBankIndexes();
        ShowRoleBankIndex(Character.RoleBank[0]);
        Log.Info(Tag, $"{Name} deletes carrying bank label {index.Id}");
    }

    public void SaveItemToBox(int box, int view, int index)
    {
        if (box is not (RoleBankBankBoxView or ViewId.Warehouse) || !WarehouseOpen)
        {
            Log.Debug(Tag, $"{Name} quick-stores {view}/{index} without an open deposit box");
            return;
        }
        OnWarehouseSelect(view, 0, index);
    }

    private void EnsureRoleBank()
    {
        if (Character.RoleBank.Count > 0) return;
        Character.RoleBank.Add(new RoleBankIndex { Id = Character.NextRoleBankId++, Name = "1" });
    }

    private RoleBankIndex? RoleBankFind(int id) => Character.RoleBank.FirstOrDefault(index => index.Id == id);

    private string RoleBankName(string typed)
    {
        string name = typed.Replace(UiPackets.FieldSeparator, ' ').Replace('\u0003', ' ').Trim();
        if (name.Length == 0 || name.Length > RoleBankRules.NameLength || name.Contains(' '))
        {
            SystemMessage($"A label name has 1 to {RoleBankRules.NameLength} characters without spaces.", SystemMessageKind.Prompt);
            return "";
        }
        return name;
    }

    private void SendRoleBankIndexes()
    {
        string pairs = string.Concat(Character.RoleBank.Take(RoleBankRules.Indexes).Select(index => $" {index.Id} {UiPackets.Token(index.Name)}"));
        Send(ChatPackets.Script(0, "scBankIndex" + pairs), "(carrying bank labels)");
    }

    private void ShowRoleBankIndex(RoleBankIndex index)
    {
        _roleBankShown = index.Id;
        int table = RoleBankRules.Table;
        Send(CharacterPackets.ClearCustomTable(table), "(carrying bank cleared)");
        foreach (RoleBankEntry[] chunk in index.Entries.Chunk(RoleBankRowsPerMessage))
        {
            IEnumerable<string> cells = chunk.SelectMany(entry => new[]
            {
                entry.Key.ToString(), entry.Key.ToString(), entry.Name, entry.Amount.ToString(), RoleBankBinding,
            });
            Send(CharacterPackets.CustomTableRows(table, cells), "(carrying bank articles)");
        }
    }
}
