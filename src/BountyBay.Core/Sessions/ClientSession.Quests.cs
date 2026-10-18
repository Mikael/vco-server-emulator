using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int SkillTutorOption = 7;
    public const int QuestsOption = 8;
    public const int PetOption = 9;
    public const int PetCallOption = 90;
    public const int PetWindowOption = 91;
    public const int FirstRewardOption = 560;
    public const int LastRewardOption = 575;
    public const int QuestAcceptOption = 576;
    public const int QuestDeclineOption = 577;
    public const int OtherBusinessOption = 578;
    public const int FirstQuestOption = 10000;

    private const int ByeOption = 3;
    private const int RecordAmountColumn = 5;
    private const int LongestDialogueText = 1500;
    private const DialogueEntry LightText = (DialogueEntry)1;

    private (int Quest, int Npc)? _rewardChoice;
    private (int Quest, int Npc)? _questOffer;

    private QuestBook QuestBook => QuestBook.For(Context.Settings);

    public bool HandleProgressOption(int target, int option)
    {
        switch (option)
        {
            case SkillTutorOption:
                OpenSkillTutor();
                return true;
            case QuestsOption:
                ShowQuests(target);
                return true;
            case PetOption:
                ShowPetMenu(target);
                return true;
            case PetCallOption:
                TogglePet();
                ShowPetMenu(target);
                return true;
            case PetWindowOption:
                OpenPetWindow();
                return true;
            case OtherBusinessOption:
                if (Context.Data.Npcs.TryGetValue(target, out NpcDefinition? npc)) Talk(npc);
                return true;
            case QuestAcceptOption:
                AcceptOfferedInDialogue(target);
                return true;
            case QuestDeclineOption:
                _questOffer = null;
                ShowQuests(target);
                return true;
            case >= FirstRewardOption and <= LastRewardOption:
                ChooseQuestReward(target, option - FirstRewardOption);
                return true;
        }
        int index = option - FirstQuestOption;
        if (index < 0 || index >= QuestBook.Quests.Count) return false;
        ShowQuestOffer(target, QuestBook.Quests[index]);
        return true;
    }

    public void ShowQuestOffer(int npc, QuestDefinition quest)
    {
        if (quest.Giver != npc || !QuestOpen(quest) || !QuestUnlocked(quest))
        {
            SystemMessage("That task is not available.");
            ShowQuests(npc);
            return;
        }
        _questOffer = (quest.Id, npc);
        _clientMenuOptions.Clear();
        TalkingTo = npc;
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, DialogueText(quest.Offer)) };
        if (quest.Objective.Length > 0 && quest.Objective != quest.Offer)
            entries.Add((LightText, 0, DialogueText(quest.Objective)));
        entries.Add((DialogueEntry.Option, QuestAcceptOption, "Accept"));
        entries.Add((DialogueEntry.Option, QuestDeclineOption, "Decline"));
        Send(ChatPackets.NpcDialogue(npc, entries), $"(quest offer {quest.Id})");
    }

    private void AcceptOfferedInDialogue(int npc)
    {
        if (_questOffer is not { } offer || offer.Npc != npc || QuestBook.Find(offer.Quest) is not { } quest)
        {
            ShowQuests(npc);
            return;
        }
        _questOffer = null;
        AcceptQuest(npc, quest);
    }

    private static string DialogueText(string text)
    {
        string clean = text.Replace("\\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length > LongestDialogueText ? clean[..LongestDialogueText] : clean;
    }

    public void SendProgress()
    {
        SendSkillState(clear: false);
        SendQuestState(clear: false);
        if (!Properties.ContainsKey("SeeRw_ListNum")) SetKnownProperty("SeeRw_ListNum", 0);
        if (!Properties.ContainsKey("SeeMemoryNum")) SetKnownProperty("SeeMemoryNum", 0);
        StartStoryline();
        foreach (int done in Character.CompletedQuests.Select(progress => progress.Id).ToList()) AutoAcceptAfter(done);
        CheckAutoComplete();
    }

    public void SendQuestState(bool clear = true)
    {
        List<(QuestDefinition Quest, QuestProgress Progress, int Index, QuestTarget Target)> rows = RecordRows();
        SendTable(ProgressTables.TaskAccepted, Character.Quests.Select(progress => new object[] { progress.Id, TaskAcceptedState(progress.Id), progress.Date }), clear);
        SendTable(ProgressTables.TaskRecord, rows.Select(row => new object[]
        {
            row.Quest.Id, row.Target.Kind == QuestKind.Kill ? 1 : 2, 0, 0, row.Target.Count, TargetAmount(row.Quest, row.Progress, row.Index, row.Target), 0, row.Target.Name,
        }), clear);
        SendTable(ProgressTables.TaskCompleted, Character.CompletedQuests.Select(progress => new object[] { progress.Id, progress.Date }), clear);
        SendQuestMarks();
    }

    public void ShowQuests(int npc)
    {
        SendQuestState();
        var entries = new List<(DialogueEntry, int, string)>();
        IReadOnlyList<QuestDefinition> quests = QuestBook.Quests;
        for (int i = 0; i < quests.Count; i++)
        {
            if (quests[i].Giver == npc && QuestOpen(quests[i]) && QuestUnlocked(quests[i]))
                entries.Add((DialogueEntry.Option, FirstQuestOption + i, quests[i].Name));
        }
        List<string> waiting = Character.Quests
            .Select(progress => QuestBook.Find(progress.Id))
            .Where(quest => quest != null && (quest.Giver == npc || QuestTurnIn(quest) == npc))
            .Select(quest => quest!.Name)
            .ToList();
        string text = entries.Count > 0 ? "I could use a capable hand." : "I have no work for you right now.";
        if (waiting.Count > 0) text += $" Still open: {string.Join(", ", waiting)}.";
        entries.Insert(0, (DialogueEntry.Text, 0, text));
        if (HasOtherBusiness(npc)) entries.Add((DialogueEntry.Option, OtherBusinessOption, "Other business"));
        entries.Add((DialogueEntry.Option, ByeOption, "Goodbye"));
        TalkingTo = npc;
        _clientMenuOptions.Clear();
        Send(ChatPackets.NpcDialogue(npc, entries), "(quest dialogue)");
    }

    public void AcceptQuest(int npc, QuestDefinition quest)
    {
        if (quest.Giver != npc || !QuestOpen(quest) || !QuestUnlocked(quest))
        {
            SystemMessage("That task is not available.");
            return;
        }
        BeginQuest(quest);
        if (npc == 0) return;
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, $"Task accepted: {quest.Name}.") };
        if (quest.Objective.Length > 0) entries.Add((LightText, 0, DialogueText(quest.Objective)));
        if (HasOpenQuests(npc)) entries.Add((DialogueEntry.Option, QuestsOption, "Other tasks"));
        entries.Add((DialogueEntry.Option, ByeOption, "Goodbye"));
        _clientMenuOptions.Clear();
        Send(ChatPackets.NpcDialogue(npc, entries), "(quest dialogue)");
    }

    public bool MayAcceptFromLog(QuestDefinition quest) => quest.Giver == 0 && QuestOpen(quest) && QuestUnlocked(quest);

    public void OnMonsterKilled(int monsterId)
    {
        GuildCopyMonsterKilled();
        PetGainExperience(10);
        Context.Data.Monsters.TryGetValue(monsterId, out MonsterDefinition? killed);
        if (killed != null) GainExploreKill(killed);
        CountKill(monsterId, killed?.Name ?? "");
    }

    public void OnSeaNpcSunk(string name) => CountKill(0, name);

    public bool OnNpcTalked(int npcId)
    {
        SendNpcPortrait(npcId);
        _questOffer = null;
        RefreshCarriedProgress();
        foreach (QuestProgress progress in Character.Quests.ToList())
        {
            QuestDefinition? quest = QuestBook.Find(progress.Id);
            if (quest == null || quest.AutoComplete || QuestTurnIn(quest) != npcId) continue;
            if (!QuestObjectivesDone(quest, progress) || !QuestReadyToHandIn(quest)) continue;
            AnnounceQuestFinished(quest);
            if (NeedsRewardChoice(quest))
            {
                ShowRewardChoice(npcId, quest);
                return true;
            }
            CompleteQuest(quest, progress, -1);
        }
        if (!OffersQuestsHere(npcId)) return false;
        ShowQuests(npcId);
        return true;
    }

    public IEnumerable<ItemStack> QuestTargetDrops(MonsterDefinition monster)
    {
        foreach ((QuestDefinition quest, QuestProgress progress, int index, QuestTarget target) in RecordRows())
        {
            if (target.Kind != QuestKind.Bring || target.Key.Length == 0 || target.Buy) continue;
            if (QuestOwned(target.Key) >= target.Count) continue;
            if (target.DropFrom.Length > 0 ? !SameQuestName(target.DropFrom, monster.Name) : !LabelMatches(target, monster)) continue;
            yield return new ItemStack { Name = target.Key, Photo = Context.Data.PhotoOf(target.Key), Amount = 1 };
        }
    }

    public void WatchTask(string property, int id)
    {
        if (property is "SeeRw_ListNum" or "SeeMemoryNum") SetKnownProperty(property, id);
    }

    private bool QuestOpen(int id) => QuestBook.Find(id) is { } quest ? QuestOpen(quest) : Character.Quests.All(progress => progress.Id != id);

    private bool QuestOpen(QuestDefinition quest)
    {
        if (Character.Quests.Any(progress => progress.Id == quest.Id)) return false;
        QuestProgress? done = Character.CompletedQuests.FirstOrDefault(progress => progress.Id == quest.Id);
        return done == null || (quest.Daily && done.Date != Today());
    }

    private bool OffersQuestsHere(int npcId)
    {
        if (!Context.Data.Npcs.TryGetValue(npcId, out NpcDefinition? npc)) return false;
        if (npc.Options.Any(option => (int)option.Action == QuestsOption)) return false;
        return HasOpenQuests(npcId);
    }

    private bool HasOpenQuests(int npcId)
        => QuestBook.Quests.Any(quest => quest.Giver == npcId && !quest.AutoAccept && QuestOpen(quest) && QuestUnlocked(quest));

    private bool HasOtherBusiness(int npcId)
        => Context.Data.Npcs.TryGetValue(npcId, out NpcDefinition? npc)
            && (npc.Options.Any(option => option.Action != DialogueAction.Bye) || npc.Shop != null);

    private void BeginQuest(QuestDefinition quest)
    {
        if (quest.Daily) Character.CompletedQuests.RemoveAll(progress => progress.Id == quest.Id);
        Character.Quests.Add(new QuestProgress { Id = quest.Id, Date = Today() });
        Character.QuestCounts[quest.Id] = Enumerable.Repeat(0, quest.Objectives.Count).ToList();
        _finishedAnnounced.Remove(quest.Id);
        Save();
        Send(BboSocialPackets.NewTaskFlash(quest.Id), $"(quest start talk {quest.Id})");
        SendQuestState();
        OnBboQuestAccepted(quest);
        SystemMessage($"Task accepted: {quest.Name}.", SystemMessageKind.Prompt);
        if (quest.RequiresWork && Character.WorkType == 0) Send(UiPackets.Custom("FlashWorkButton"), "(flash profession button)");
        Log.Info(Tag, $"{Name} accepts quest {quest.Id} ({quest.Name})");
    }

    private void StartStoryline()
    {
        if (Character.CompletedQuests.Count > 0 || Character.Quests.Count > 0) return;
        QuestDefinition? first = QuestBook.Quests.FirstOrDefault(quest => quest.StartsStoryline);
        if (first == null || !Character.AutoQuestsGiven.Add(first.Id) || !QuestOpen(first)) return;
        BeginQuest(first);
    }

    private void AutoAcceptAfter(int completed)
    {
        foreach (QuestDefinition next in QuestBook.Following(completed))
        {
            if (!next.AutoAccept || Character.AutoQuestsGiven.Contains(next.Id) || !QuestOpen(next) || !QuestUnlocked(next)) continue;
            Character.AutoQuestsGiven.Add(next.Id);
            BeginQuest(next);
        }
    }

    private void CheckAutoComplete()
    {
        foreach (QuestProgress progress in Character.Quests.ToList())
        {
            QuestDefinition? quest = QuestBook.Find(progress.Id);
            if (quest is not { AutoComplete: true } || !QuestObjectivesDone(quest, progress) || !QuestReadyToHandIn(quest)) continue;
            CompleteQuest(quest, progress, quest.RewardChooseOne ? FittingRewardIndex(quest) : -1);
        }
    }

    private List<int> CountsOf(QuestDefinition quest, QuestProgress progress)
    {
        int size = quest.Objectives.Count;
        if (!Character.QuestCounts.TryGetValue(quest.Id, out List<int>? counts))
        {
            counts = Enumerable.Repeat(0, size).ToList();
            if (size > 0 && progress.Count > 0) counts[0] = progress.Count;
            Character.QuestCounts[quest.Id] = counts;
        }
        while (counts.Count < size) counts.Add(0);
        return counts;
    }

    private int TargetAmount(QuestDefinition quest, QuestProgress progress, int index, QuestTarget target) => target.Kind switch
    {
        QuestKind.Kill => Math.Min(target.Count, CountsOf(quest, progress)[index]),
        QuestKind.Bring => Math.Min(target.Count, QuestOwned(target.Key)),
        QuestKind.Level => target.Skill == QuestDefinition.ProfessionLevel ? Character.WorkLevel : SkillLevel(target.Skill),
        QuestKind.Money => (int)Math.Min(target.Count, Money),
        _ => target.Count,
    };

    private List<(QuestDefinition Quest, QuestProgress Progress, int Index, QuestTarget Target)> RecordRows()
    {
        var rows = new List<(QuestDefinition, QuestProgress, int, QuestTarget)>();
        foreach (QuestProgress progress in Character.Quests)
        {
            QuestDefinition? quest = QuestBook.Find(progress.Id);
            if (quest == null) continue;
            IReadOnlyList<QuestTarget> targets = quest.Objectives;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Recorded) rows.Add((quest, progress, i, targets[i]));
            }
        }
        return rows;
    }

    private void CountKill(int monsterId, string name)
    {
        if (monsterId != 0 && name.Length == 0 && Context.Data.Monsters.TryGetValue(monsterId, out MonsterDefinition? monster)) name = monster.Name;
        if (name.Length == 0) return;
        List<(QuestDefinition Quest, QuestProgress Progress, int Index, QuestTarget Target)> rows = RecordRows();
        bool changed = false;
        for (int row = 0; row < rows.Count; row++)
        {
            (QuestDefinition quest, QuestProgress progress, int index, QuestTarget target) = rows[row];
            if (target.Kind != QuestKind.Kill || !SameQuestName(target.Name, name)) continue;
            List<int> counts = CountsOf(quest, progress);
            if (counts[index] >= target.Count) continue;
            counts[index]++;
            progress.Count = counts[0];
            changed = true;
            ReportProgress(row, counts[index]);
            if (!QuestObjectivesDone(quest, progress)) continue;
            if (!quest.AutoComplete) SystemMessage($"{quest.Name}: done. Return for your reward.", SystemMessageKind.Prompt);
        }
        if (!changed) return;
        Save();
        CheckAutoComplete();
    }

    private void RefreshCarriedProgress()
    {
        List<(QuestDefinition Quest, QuestProgress Progress, int Index, QuestTarget Target)> rows = RecordRows();
        for (int row = 0; row < rows.Count; row++)
        {
            (QuestDefinition quest, QuestProgress progress, int index, QuestTarget target) = rows[row];
            if (target.Kind != QuestKind.Bring) continue;
            List<int> counts = CountsOf(quest, progress);
            int carried = TargetAmount(quest, progress, index, target);
            if (carried == counts[index]) continue;
            counts[index] = carried;
            if (index == 0) progress.Count = carried;
            ReportProgress(row, carried);
        }
        CheckAutoComplete();
    }

    private bool LabelMatches(QuestTarget target, MonsterDefinition monster)
    {
        if (!Context.Data.Items.Goods.TryGetValue(target.Key, out GoodsDefinition? goods) || goods.Label.Length == 0) return false;
        string[] words = monster.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => word.Length > 2 && goods.Label.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private int QuestOwned(string key)
    {
        if (key.Length == 0) return 0;
        int total = 0;
        foreach (int view in QuestBagViews)
        {
            if (OwnSlots(view) is { } slots) total += slots.Values.Where(stack => stack.Name == key).Sum(stack => stack.Amount);
        }
        return total;
    }

    private void QuestTake(string key, int count)
    {
        foreach (int view in QuestBagViews)
        {
            if (OwnSlots(view) is not { } slots) continue;
            foreach (int slot in slots.Where(pair => pair.Value.Name == key).Select(pair => pair.Key).Order().ToList())
            {
                if (count <= 0) return;
                int taken = Math.Min(count, slots[slot].Amount);
                TakeFrom(view, slot, taken, "(task item handed in)");
                count -= taken;
            }
        }
    }

    private static readonly int[] QuestBagViews = [ViewId.BagItems, ViewId.BagArticle, ViewId.BagEquip, ViewId.Cargo];

    private void ReportProgress(int row, int amount)
    {
        Send(TablePackets.UpdateCell(ProgressTables.TaskRecord, row, RecordAmountColumn, amount), "(task progress)");
        Send(ChatPackets.Script(0, $"UpdateTaskState {row} {amount}"), "(task notice)");
        SendQuestMarks();
    }

    private bool NeedsRewardChoice(QuestDefinition quest) => quest.RewardChooseOne && quest.RewardItems.Count > 1;

    private void ShowRewardChoice(int npc, QuestDefinition quest)
    {
        _rewardChoice = (quest.Id, npc);
        TalkingTo = npc;
        _clientMenuOptions.Clear();
        var entries = new List<(DialogueEntry, int, string)> { (DialogueEntry.Text, 0, $"{quest.Name}: well done. Choose your reward.") };
        int shown = Math.Min(quest.RewardItems.Count, LastRewardOption - FirstRewardOption + 1);
        for (int i = 0; i < shown; i++)
            entries.Add((DialogueEntry.Option, FirstRewardOption + i, RewardLabel(quest.RewardItems[i])));
        entries.Add((DialogueEntry.Option, ByeOption, "Later"));
        Send(ChatPackets.NpcDialogue(npc, entries), $"(reward choice {quest.Id})");
    }

    private void ChooseQuestReward(int npc, int choice)
    {
        if (_rewardChoice is not { } pending) return;
        _rewardChoice = null;
        QuestDefinition? quest = QuestBook.Find(pending.Quest);
        QuestProgress? progress = Character.Quests.FirstOrDefault(entry => entry.Id == pending.Quest);
        if (quest == null || progress == null || choice >= quest.RewardItems.Count) return;
        if (QuestTurnIn(quest) != pending.Npc || !QuestObjectivesDone(quest, progress) || !QuestReadyToHandIn(quest)) return;
        CompleteQuest(quest, progress, choice);
        Send(ChatPackets.NpcDialogue(pending.Npc,
        [
            (DialogueEntry.Text, 0, quest.Done),
            (DialogueEntry.Option, ByeOption, "Goodbye"),
        ]), "(quest dialogue)");
    }

    private static string RewardLabel(QuestRewardItem item)
        => item.Count > 1 ? $"{item.Count} x {item.Name}" : item.Name;

    private void CompleteQuest(QuestDefinition quest, QuestProgress progress, int choice)
    {
        AnnounceQuestFinished(quest);
        _finishedAnnounced.Remove(quest.Id);
        OnBboQuestCompleted(quest);
        PlayQuestCompleteEffect();
        foreach (QuestTarget target in quest.Objectives)
        {
            if (target.Kind == QuestKind.Bring) QuestTake(target.Key, target.Count);
            if (target.Kind == QuestKind.Money) SetMoney(Money - target.Count);
        }
        Character.Quests.Remove(progress);
        Character.QuestCounts.Remove(quest.Id);
        Character.CompletedQuests.RemoveAll(entry => entry.Id == quest.Id);
        Character.CompletedQuests.Add(new QuestProgress { Id = quest.Id, Count = quest.Count, Date = Today() });
        if (Properties.Int("SeeRw_ListNum") == -quest.Id) SetKnownProperty("SeeRw_ListNum", 0);
        SendQuestState();
        SystemMessage(quest.Done);
        GrantQuestRewards(quest, choice);
        GrantQuestExtras(quest);
        RefreshLoad();
        Log.Info(Tag, $"{Name} completes quest {quest.Id} ({quest.Name})");
        Save();
        AutoAcceptAfter(quest.Id);
    }

    private void GrantQuestRewards(QuestDefinition quest, int choice)
    {
        var parts = new List<string>();
        if (quest.Gold > 0)
        {
            SetMoney(Money + quest.Gold);
            parts.Add($"{quest.Gold} silver coins");
        }
        if (quest.ProfessionExp > 0)
        {
            GainProfessionExperience(quest.ProfessionExp);
            parts.Add($"{quest.ProfessionExp} profession experience");
        }
        if (quest.Experience > 0 && quest.Skill.Length > 0) GainQuestSkillExperience(quest.Skill, quest.Experience);
        foreach ((string skill, long amount) in quest.SkillExp) GainQuestSkillExperience(skill, amount);
        bool gotItems = false;
        foreach (QuestRewardItem item in RewardItemsFor(quest, choice))
        {
            GiveQuestItem(item);
            parts.Add(RewardLabel(item));
            gotItems = true;
        }
        if (gotItems) ItemObtained("", 0, "");
        if (quest.Reputation > 0) GainReputation(quest.Reputation);
        if (quest.Amity > 0)
        {
            Character.Amity += quest.Amity;
            SystemMessage($"Your amity with {Properties.Text("CountryName", "this country")} rises by {quest.Amity}.");
        }
        SystemMessage(parts.Count > 0
            ? $"Task completed: {quest.Name}. You receive {string.Join(", ", parts)}."
            : $"Task completed: {quest.Name}.", SystemMessageKind.Upgrade);
    }

    private IEnumerable<QuestRewardItem> RewardItemsFor(QuestDefinition quest, int choice)
    {
        if (quest.RewardItems.Count == 0) return [];
        if (quest.RewardRandomOne) return [quest.RewardItems[Random.Shared.Next(quest.RewardItems.Count)]];
        if (!quest.RewardChooseOne) return quest.RewardItems;
        int index = choice >= 0 && choice < quest.RewardItems.Count ? choice : FittingRewardIndex(quest);
        return [quest.RewardItems[index]];
    }

    private int FittingRewardIndex(QuestDefinition quest)
    {
        string fight = FightingSkill();
        int index = quest.RewardItems.FindIndex(item => ItemCatalog.Text(RewardKey(item), "NeedSkill") == fight);
        return Math.Max(0, index);
    }

    private void GainQuestSkillExperience(string key, long amount)
    {
        switch (key)
        {
            case "SHIP_MANEUVERABILITY":
                GainShipExperience(ShipAttribute.Move, amount);
                return;
            case "SHIP_STRUCTURE":
                GainShipExperience(ShipAttribute.Load, amount);
                return;
        }
        if (key.StartsWith("SKILL_", StringComparison.Ordinal)) GainSkillExperience(key, amount);
        else Log.Info(Tag, $"{Name}: quest experience {amount} for {key} is not modelled");
    }

    private void GainProfessionExperience(long amount)
    {
        Character.WorkExperience = Math.Min(long.MaxValue - amount, Character.WorkExperience) + amount;
        Log.Info(Tag, $"{Name} gains {amount} profession experience ({Character.WorkExperience} in total)");
    }

    private string RewardKey(QuestRewardItem item)
    {
        if (ItemCatalog.Find(item.Key) != null || Context.Data.PhotoOf(item.Key) != 0 || item.Fallback.Length == 0) return item.Key;
        return item.Fallback;
    }

    private void GiveQuestItem(QuestRewardItem reward)
    {
        string key = RewardKey(reward);
        int photo = Context.Data.PhotoOf(key);
        int count = Math.Max(1, reward.Count);
        if (TradeGoods(key))
        {
            int stack = Cargo.FirstOrDefault(pair => pair.Value.Name == key).Key;
            if (stack != 0 || FreeSlot(Cargo, ViewId.CargoSlots) != 0)
            {
                AddBought(ViewId.Cargo, ViewId.CargoSlots, new ShopEntry(key, photo, 0), count, 0);
                return;
            }
        }
        else if (ItemCatalog.Number(key, "ItemType") == EquipmentType)
        {
            Dictionary<int, ItemStack> gear = Bags[ViewId.BagEquip];
            for (; count > 0; count--)
            {
                int slot = FreeSlot(gear, ViewId.BagSlots);
                if (slot == 0) break;
                gear[slot] = new ItemStack { Name = key, Photo = photo, Amount = 1 };
                Send(ViewPackets.AddItem(ViewId.BagEquip, slot, ItemProperties(gear[slot], slot)), "(task reward)");
                RefreshDurability(ViewId.BagEquip, slot);
            }
            if (count == 0)
            {
                RefreshStats();
                return;
            }
        }
        else if (ReceiveIntoBag(key, photo, count, "(task reward)"))
        {
            return;
        }
        WarehouseStore(new ItemStack { Name = key, Photo = photo, Amount = count }, anySlot: true);
        SystemMessage($"Your bag is full, so {RewardLabel(reward)} was sent to your warehouse.", SystemMessageKind.Prompt);
    }

    private static string Today()
    {
        DateTime now = DateTime.Now;
        return $"{now.Year}-{now.Month}-{now.Day}";
    }
}
