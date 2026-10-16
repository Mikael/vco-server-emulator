using System.Globalization;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private TreasureCardRules TreasureCardData => LeisureData.TreasureCard;

    private int TreasureCardsLeft => Math.Max(0, TreasureCardData.TurnsPerDay - Character.TurnCardUsed);

    private int TreasureCardsFree => Math.Max(0, TreasureCardData.FreePerDay - Character.TurnCardUsed);

    public void OpenTreasureCards()
    {
        ResetTreasureCards();
        Send(LeisurePackets.ShowTurnCard(TreasureCardsLeft, TreasureCardsFree), "(treasure box game)");
        foreach ((int position, string prize) in Character.TurnCardOpened.OrderBy(pair => pair.Key)) SendTreasureCard(position, prize);
    }

    public void TurnTreasureCard(int position)
    {
        ResetTreasureCards();
        TreasureCardRules rules = TreasureCardData;
        if (position < 1 || position > Character.TurnCardBoard.Count || Character.TurnCardOpened.ContainsKey(position)) return;
        if (TreasureCardsLeft <= 0)
        {
            SystemMessage("You cannot open any more boxes today.", SystemMessageKind.Prompt);
            return;
        }
        bool gold = position >= rules.GoldFrom;
        int cost = TreasureCardsFree > 0 ? 0 : gold ? rules.GoldTurnMoney : rules.TurnMoney;
        if (Money < cost)
        {
            SystemMessage($"Opening this box costs {cost:N0} silver coins.", SystemMessageKind.Prompt);
            return;
        }
        string prize = Character.TurnCardBoard[position - 1];
        (string item, int count) = TreasureCardPrizeParts(prize);
        if (item.Length > 0 && !ReceiveIntoBag(item, Context.Data.PhotoOf(item), count, "(treasure box prize)"))
        {
            SystemMessage("Your bag is full.", SystemMessageKind.Prompt);
            return;
        }
        if (cost > 0) SetMoney((long)Money - cost);
        Character.TurnCardUsed++;
        Character.TurnCardOpened[position] = prize;
        Save();
        SendTreasureCard(position, prize);
        Log.Info(Tag, $"{Name} opens treasure box {position}: {count} x {item}");
    }

    public void LookAllTreasureCards()
    {
        ResetTreasureCards();
        for (int position = 1; position <= Character.TurnCardBoard.Count; position++)
            SendTreasureCard(position, Character.TurnCardBoard[position - 1]);
    }

    private void SendTreasureCard(int position, string prize)
    {
        (string item, int count) = TreasureCardPrizeParts(prize);
        Send(LeisurePackets.TurnCardInfo(position, Context.Data.PhotoOf(item), LeisureItemLabel(item), count, TreasureCardsLeft, TreasureCardsFree),
            "(treasure box card)");
    }

    private void ResetTreasureCards()
    {
        TreasureCardRules rules = TreasureCardData;
        if (Character.TurnCardDay == LeisureToday && Character.TurnCardBoard.Count == rules.Cards) return;
        Character.TurnCardDay = LeisureToday;
        Character.TurnCardUsed = 0;
        Character.TurnCardOpened = [];
        Character.TurnCardBoard = Enumerable.Range(1, rules.Cards)
            .Select(position => DrawTreasurePrize(position >= rules.GoldFrom && rules.Gold.Count > 0 ? rules.Gold : rules.Silver))
            .ToList();
    }

    private static string DrawTreasurePrize(List<TreasureCardPrize> pool)
    {
        int total = pool.Sum(prize => Math.Max(0, prize.Weight));
        if (total <= 0) return "";
        int roll = Random.Shared.Next(total);
        foreach (TreasureCardPrize prize in pool)
        {
            roll -= Math.Max(0, prize.Weight);
            if (roll < 0) return $"{prize.Item}|{Math.Max(1, prize.Count).ToString(CultureInfo.InvariantCulture)}";
        }
        return "";
    }

    private static (string Item, int Count) TreasureCardPrizeParts(string prize)
    {
        string[] parts = prize.Split('|');
        int count = parts.Length > 1 && int.TryParse(parts[1], out int value) ? value : 1;
        return (parts[0], Math.Max(1, count));
    }
}
