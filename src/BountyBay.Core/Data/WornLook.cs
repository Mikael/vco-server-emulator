using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Data;

public readonly record struct WornLook(int Body, int TexColor, int Hat, int CapColor, int LeftHand, int RightHand)
{
    private const int FemaleBodies = 3402;

    public static WornLook Of(CharacterRecord character)
    {
        var look = new WornLook(character.Body, 0, 0, 0, 0, 0);
        if (character.Bags == null || !character.Bags.TryGetValue(ViewId.Equipped, out Dictionary<int, ItemStack>? worn)) return look;
        bool female = character.Body / 1000 == FemaleBodies;
        foreach (ItemStack item in worn.OrderBy(pair => pair.Key).Select(pair => pair.Value))
        {
            int model = ItemCatalog.Number(item.Name, female ? "WearModelF" : "WearModel");
            if (model == 0) continue;
            int color = ItemCatalog.Number(item.Name, "WearColor");
            look = ItemCatalog.Text(item.Name, "WearProp") switch
            {
                "BodyType" => look with { Body = model, TexColor = color },
                "Hat" => look with { Hat = model, CapColor = color },
                "LeftHand" => look with { LeftHand = model },
                "RightHand" => look with { RightHand = model },
                _ => look,
            };
        }
        return look;
    }

    public IEnumerable<(string Name, int Value)> Properties() =>
    [
        ("BodyType", Body), ("TexColor", TexColor), ("Hat", Hat), ("CapColor", CapColor), ("LeftHand", LeftHand), ("RightHand", RightHand),
    ];
}
