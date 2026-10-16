using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int NewHeroQuest = 740;

    private static readonly (string Weapon, string Skill)[] CareerWeapons =
    [
        ("", ""),
        ("ZZB005085", "SKILL_SWORD"),
        ("ZZB005001", "SKILL_FALCHION"),
        ("ZZB005001", "SKILL_FALCHION"),
        ("ZZB005169", "SKILL_SHOT"),
        ("ZZB005169", "SKILL_SHOT"),
    ];

    private bool QuestReadyToHandIn(QuestDefinition quest)
    {
        if ((quest.Id != NewHeroQuest && !quest.RequiresWork) || Character.WorkType > 0) return true;
        SystemMessage("Hold CTRL+S to open the profession interface and choose a profession first.", SystemMessageKind.Prompt);
        Send(UiPackets.Custom("FlashWorkButton"), "(flash profession button)");
        return false;
    }

    private void GrantQuestExtras(QuestDefinition quest)
    {
        if (quest.Id != NewHeroQuest || quest.RewardItems.Count > 0) return;
        int work = Character.WorkType;
        if (work <= 0 || work >= CareerWeapons.Length) return;
        (string weapon, string skill) = CareerWeapons[work];
        if (SkillLevel(skill) <= 0)
        {
            if (Context.Data.Creation.Skills.IndexOf(skill) is >= 0 and int id) Character.Skills.Add(new SkillEntry { Id = id, Level = 1 });
            else Character.LearnedSkills[skill] = 1;
            SendSkillState();
        }
        Dictionary<int, ItemStack> bag = Bags[ViewId.BagEquip];
        int slot = FreeSlot(bag, ViewId.BagSlots);
        if (slot == 0)
        {
            SystemMessage("Your equipment bag is full, so the weapon could not be handed over.", SystemMessageKind.Prompt);
            return;
        }
        bag[slot] = new ItemStack { Name = weapon, Photo = Context.Data.PhotoOf(weapon), Amount = 1 };
        Send(ViewPackets.AddItem(ViewId.BagEquip, slot, ItemProperties(bag[slot], slot)), "(new hero weapon)");
        RefreshDurability(ViewId.BagEquip, slot);
        RefreshStats();
        Log.Info(Tag, $"{Name} receives the newbie weapon {weapon}");
    }
}
