using BountyBay.Core.Data;
using BountyBay.Core.Persistence;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private IEnumerable<ItemStack> QuestDrops(MonsterDefinition monster)
    {
        string[] words = monster.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) yield break;
        foreach (QuestProgress progress in Character.Quests)
        {
            QuestDefinition? quest = QuestBook.Find(progress.Id);
            if (quest == null || quest.Kind != QuestKind.Bring || quest.Item.Length == 0) continue;
            if (Owned(quest.Item) >= quest.Count) continue;
            if (!Context.Data.Items.Goods.TryGetValue(quest.Item, out GoodsDefinition? goods) || goods.Label.Length == 0) continue;
            if (!words.Any(word => goods.Label.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;
            yield return new ItemStack { Name = quest.Item, Photo = goods.Photo, Amount = 1 };
        }
    }
}
