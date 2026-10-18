using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Data;

public readonly record struct WornLook(int Body, int TexColor, int Hat, int CapColor, int LeftHand, int RightHand, int StartEffect = 0, int EvolColor = 0)
{
    private const int FemaleRole = 3001002;

    public static bool IsFemale(CharacterRecord character) => character.Role == FemaleRole;

    private static readonly System.Text.RegularExpressions.Regex FemaleName =
        new(@"\bfemale\b|\bfor wom[ae]n\b|\bwom[ae]n's\b|\blad(y|ies)('s)?\b|\(F\)|\b\w*woman\b|^(Grand )?(Baroness|Baronetess|Viscountess|Countess|Marchioness|Duchess|Princess) Captain", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex MaleName =
        new(@"\bmale\b|\bfor m[ae]n\b|\bm[ae]n's\b|\bgentleman('s)?\b|\(M\)|\bMoneyman\b|^(Grand )?(Baron|Baronet|Viscount|Count|Marquess|Duke|Prince|Knight|Chevalier) Captain", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool? ForFemale(string label)
        => FemaleName.IsMatch(label) ? true : MaleName.IsMatch(label) ? false : null;

    public static WornLook Of(CharacterRecord character)
    {
        var look = new WornLook(character.Body, 0, 0, 0, 0, 0);
        if (character.Bags == null || !character.Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn)) return look;
        bool female = IsFemale(character);
        int suitHat = 0;
        bool hatWorn = false;
        int weaponStars = 0, weaponEvol = 0, bodyStars = 0, bodyEvol = 0;
        foreach (ItemStack item in worn.OrderBy(pair => pair.Key).Select(pair => pair.Value))
        {
            int model = Model(item.Name, female, "WearModel", "WearModelF");
            if (model == 0) continue;
            int color = ItemCatalog.Number(item.Name, "WearColor");
            string part = ItemCatalog.Text(item.Name, "WearProp");
            switch (part)
            {
                case "BodyType":
                    look = look with { Body = model, TexColor = color };
                    suitHat = Model(item.Name, female, "WearHat", "WearHatF");
                    bodyStars = item.Upgrade?.Stars ?? 0;
                    bodyEvol = item.Upgrade?.Evol ?? 0;
                    break;
                case "Hat":
                    look = look with { Hat = model, CapColor = color };
                    hatWorn = true;
                    break;
                case "LeftHand" or "RightHand":
                    look = part == "LeftHand" ? look with { LeftHand = model } : look with { RightHand = model };
                    weaponStars = Math.Max(weaponStars, item.Upgrade?.Stars ?? 0);
                    weaponEvol = Math.Max(weaponEvol, item.Upgrade?.Evol ?? 0);
                    break;
            }
        }
        if (!hatWorn && suitHat != 0) look = look with { Hat = suitHat };
        return look with
        {
            StartEffect = WeaponStarEffect(weaponStars) | BodyStarEffect(bodyStars) << 8,
            EvolColor = (weaponEvol > 0 ? 1 : 0) | (bodyEvol > 0 ? 1 << 16 : 0),
        };
    }

    private static int Model(string item, bool female, string male, string other)
    {
        int model = female ? ItemCatalog.Number(item, other) : 0;
        return model != 0 ? model : ItemCatalog.Number(item, male);
    }

    private static int WeaponStarEffect(int stars) => stars switch
    {
        <= 0 => 0,
        < 10 => 1,
        < 16 => 2,
        < 26 => 3,
        < 36 => 4,
        < 40 => 5,
        _ => 6,
    };

    private static int BodyStarEffect(int stars) => stars switch
    {
        < 16 => 0,
        < 26 => 1,
        < 36 => 2,
        < 40 => 3,
        _ => 4,
    };

    public IEnumerable<(string Name, int Value)> Properties()
    {
        var properties = new List<(string Name, int Value)>
        {
            ("BodyType", Body), ("TexColor", TexColor), ("Hat", Hat), ("CapColor", CapColor), ("LeftHand", LeftHand), ("RightHand", RightHand),
        };
        if (PropertyTable.Has("StartEffect")) properties.Add(("StartEffect", StartEffect));
        if (PropertyTable.Has("EvolColor")) properties.Add(("EvolColor", EvolColor));
        return properties;
    }
}
