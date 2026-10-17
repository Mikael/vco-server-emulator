using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{

    private ModelChangeRules ModelChangeData => LeisureRules.For(Context.Settings).ModelChange;

    public void OpenModelChange() => Send(LeisurePackets.ShowChangeModel(), "(model change window)");

    public void ChangeModel(int view, int index, int model)
    {
        ModelChangeRules rules = ModelChangeData;
        ItemStack? item = view == ViewId.Equipped
            ? Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn) && worn.TryGetValue(index, out ItemStack? equipped) ? equipped : null
            : OwnSlots(view) is { } slots && slots.TryGetValue(index, out ItemStack? held) ? held : null;
        if (item == null)
        {
            SystemMessage("Please put the costume you want to change into the window first.", SystemMessageKind.Prompt);
            return;
        }
        if (ItemCatalog.Number(item.Name, "ArmType") != rules.ArmType)
        {
            SystemMessage("Only costumes can change their appearance.", SystemMessageKind.Prompt);
            return;
        }
        if (rules.Find(model) is null)
        {
            SystemMessage("This appearance is not available.", SystemMessageKind.Prompt);
            return;
        }
        if (Money < rules.Money || rules.Item.Length > 0 && Owned(rules.Item) < 1)
        {
            SystemMessage(rules.Item.Length > 0
                ? $"Changing the appearance needs {rules.Money:N0} silver coins and one {LeisureItemLabel(rules.Item)}."
                : $"Changing the appearance costs {rules.Money:N0} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        if (rules.Money > 0) SetMoney((long)Money - rules.Money);
        if (rules.Item.Length > 0) TakeOwned(rules.Item, 1, "(model change item used)");
        Character.ModelChanges[item.Name] = model;
        Save();
        RefreshAppearance();
        ApplyLeisureLook();
        SystemMessage("The appearance of your costume has been changed.");
        Log.Info(Tag, $"{Name} changes the model of {item.Name} to {model}");
    }

    public void ApplyLeisureLook()
    {
        bool female = WornLook.IsFemale(Character);
        var look = new Dictionary<string, int>();
        if (Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn))
        {
            foreach (ItemStack item in worn.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            {
                if (!Character.ModelChanges.TryGetValue(item.Name, out int model) || ModelChangeData.Find(model) is not { } entry) continue;
                foreach ((string name, int value) in female ? entry.Female : entry.Male)
                {
                    if (name is "BodyType" or "Hat" or "TexColor" && (value != 0 || name != "BodyType")) look[name] = value;
                }
            }
        }
        if ((Character.Special1 & AppearanceHideHatBit) != 0) look["Hat"] = 0;
        foreach ((string name, int value) in look)
        {
            if (!PropertyTable.Has(name) || Properties.Int(name, -1) == value) continue;
            if (InWorld && !AtSea) SetProperty(name, value, visibleToOthers: true);
            else Properties[name] = value;
        }
    }
}
