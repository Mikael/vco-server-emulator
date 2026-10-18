using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public void SpawnItem(string key, int amount)
    {
        if (ItemCatalog.Find(key) == null)
        {
            SystemMessage($"There is no item {key}.");
            return;
        }
        var item = new ItemStack { Name = key, Photo = Context.Data.PhotoOf(key), Amount = Math.Clamp(amount, 1, 999) };
        if (!GiveItem(item, "(spawned item)"))
        {
            SystemMessage("Your bag is full.");
            return;
        }
        Save();
        ItemObtained(key, item.Amount, "received");
        Log.Info(Tag, $"{Name} spawns {key} x{item.Amount}");
    }
}
