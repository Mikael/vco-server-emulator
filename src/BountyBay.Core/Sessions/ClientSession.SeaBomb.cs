using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const float SeaBombAreaRadius = 12f;
    private const float SeaBombReach = 80f;
    private const int SeaBombDamagePerTier = 150;
    private const int SeaBombFirstNumber = 4807;
    private const int SeaBombLastNumber = 4856;
    private const int SeaBombTiers = 10;
    private const int OffshoreSceneType = 1;

    private bool _seaBombArmed;

    public void PutDownSeaBomb(float x, float z)
    {
        if (!AtSea || Mover == null || SeaSunk) return;
        if (SceneType != OffshoreSceneType)
        {
            SystemMessage("This Function can only be used in the Offshore.", SystemMessageKind.Prompt);
            return;
        }
        (float shipX, float shipZ) = Mover.Now();
        if ((x - shipX) * (x - shipX) + (z - shipZ) * (z - shipZ) > SeaBombReach * SeaBombReach)
        {
            SystemMessage("That spot is too far away from your ship.", SystemMessageKind.Prompt);
            return;
        }
        string? mine = FirstSeaBomb();
        if (mine == null)
        {
            SystemMessage("You have no mines aboard.", SystemMessageKind.Prompt);
            return;
        }
        if (!_seaBombArmed) Log.Debug(Tag, $"{Name} lays a mine without SetupTorpedo");
        TakeSeaBomb(mine);
        _seaBombArmed = false;
        string label = SeaBombLabel(mine);
        SeaBombField.Of(Context).Lay(new SeaBomb
        {
            Owner = this,
            OwnerName = Name,
            Scene = Scene,
            X = x,
            Z = z,
            Item = mine,
            Label = label,
            Damage = SeaBombDamage(mine),
            ExpiresAt = SeaWorld.Now + SeaBombField.LifetimeSeconds * 1000L,
        });
        Save();
        SystemMessage($"You lay a {label}.");
        Log.Info(Tag, $"{Name} lays {mine} at {x:F1},{z:F1} in scene {Scene}");
    }

    public void SeaBombSank(SeaNpcDefinition definition)
    {
        Character.ShipsSunk++;
        SetMoney(Money + definition.Gold);
        SystemMessage($"Your mine sank the {definition.Name}. You salvaged {definition.Gold} silver coins.");
        GainReputation(definition.Reputation > 0 ? definition.Reputation : Rule.Economy.PirateReputationPerLevel * definition.Level);
        OnSeaNpcSunk(definition.Name);
        SeaInstanceShipSunk(definition);
        Save();
    }

    private string? FirstSeaBomb()
    {
        if (Character.Ammunition?.FirstOrDefault(pair => pair.Value > 0 && IsSeaBomb(pair.Key)).Key is { } stocked) return stocked;
        foreach (int view in BboBagViews)
        {
            if (MailBagOf(view)!.Values.FirstOrDefault(stack => IsSeaBomb(stack.Name)) is { } stack) return stack.Name;
        }
        return null;
    }

    private void TakeSeaBomb(string name)
    {
        if (Character.Ammunition != null && Character.Ammunition.GetValueOrDefault(name) > 0)
        {
            AddAmmunition(name, -1);
            return;
        }
        foreach (int view in BboBagViews)
        {
            foreach ((int slot, ItemStack stack) in MailBagOf(view)!.OrderBy(pair => pair.Key))
            {
                if (stack.Name != name) continue;
                TakeFrom(view, slot, 1, "(mine laid)");
                RefreshLoad();
                return;
            }
        }
    }

    private static bool IsSeaBomb(string name)
        => SeaBombNumber(name) is >= SeaBombFirstNumber and <= SeaBombLastNumber
            || ItemCatalog.Text(name, "GoodsType") == "Mine"
            || ItemCatalog.Text(name, "Name").EndsWith(" Mine", StringComparison.Ordinal);

    private static int SeaBombNumber(string name)
        => name.StartsWith("IDName", StringComparison.Ordinal) && int.TryParse(name.AsSpan(6), out int number) ? number : 0;

    private static int SeaBombDamage(string name)
    {
        int number = SeaBombNumber(name);
        int tier = number is >= SeaBombFirstNumber and <= SeaBombLastNumber ? (number - SeaBombFirstNumber) % SeaBombTiers + 1 : 1;
        return SeaBombDamagePerTier * tier;
    }

    private static string SeaBombLabel(string name)
        => ItemCatalog.Text(name, "Name") is { Length: > 0 } label ? label : "mine";
}
