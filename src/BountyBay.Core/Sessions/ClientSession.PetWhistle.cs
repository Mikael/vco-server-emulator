using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public const int WhistleBuyLandOption = 502;
    public const int WhistleBuySeaOption = 503;

    private const string WhistleFirstKey = "Whistle_Bird";
    private const string WhistleSeaKey = "Newwhistle007";
    private const int WhistlePrice = 1000;
    private const int WhistleMostOwned = 10;

    private static readonly string[] WhistleDefaultSkills = ["1*Pet_A", "1*Pet_D"];

    private readonly HashSet<int> _whistleViewsSent = [];

    private PetWhistleBook WhistleBook => PetWhistleBook.For(Context.Settings);

    public List<PetWhistleRecord> PetWhistles => EnsureWhistles();

    public PetWhistleRecord? CalledPetWhistle
        => Character.CalledWhistle.Length == 0 ? null : PetWhistles.FirstOrDefault(whistle => whistle.ItemId == Character.CalledWhistle);

    public int WhistlePetModel
        => CalledPetWhistle is { } whistle && WhistleBook.Whistle(whistle.Key) is { Model: > 0 } definition ? definition.Model : PetModel;

    public int WhistlePetPhoto
        => CalledPetWhistle is { } whistle && WhistleBook.Whistle(whistle.Key) is { Image: > 0 } definition ? definition.Image : WhistlePetModel;

    public void OpenWhistles()
    {
        List<PetWhistleRecord> whistles = PetWhistles;
        SendWhistleView(PetWhistlePackets.LandView, whistles.Where(whistle => !whistle.Sea).ToList());
        SendWhistleView(PetWhistlePackets.SeaView, whistles.Where(whistle => whistle.Sea).ToList());
        if (whistles.Count == 0) SystemMessage("You do not own a pet whistle yet.", SystemMessageKind.Prompt);
        if (CalledPetWhistle is { } called && Character.Pet is { Out: true })
            Send(PetWhistlePackets.PetShow(PetObjectId, called.Sea ? 1 : 0, called.ItemId), "(called pet)");
        Log.Debug(Tag, $"{Name} opens the pet list ({whistles.Count} whistle(s))");
    }

    public void SwitchWhistleTab(int tab)
    {
        Character.PetWhistleTab = tab == 0 ? 0 : 1;
        Log.Debug(Tag, $"{Name} looks at the {(tab == 0 ? "land" : "sea")} pets");
    }

    public void ShowWhistle(int tab, int index)
    {
        int view = tab == 0 ? PetWhistlePackets.LandView : PetWhistlePackets.SeaView;
        PetWhistleRecord? whistle = WhistleAt(view, index);
        if (whistle == null) return;
        if (whistle.ItemId == Character.CalledWhistle) SyncWhistleFromPet(whistle);
        if (_whistleViewsSent.Contains(view)) Send(ViewPackets.UpdateItem(view, index, WhistleItem(whistle, index)), "(pet details)");
    }

    public bool WhistleSelect(int view, int how, int index)
    {
        if (view is not (PetWhistlePackets.LandView or PetWhistlePackets.SeaView)) return false;
        PetWhistleRecord? whistle = WhistleAt(view, index);
        if (whistle == null || how != 2) return true;
        if (whistle.ItemId == Character.CalledWhistle && Character.Pet is { Out: true }) DismissWhistlePet();
        else CallWhistlePet(whistle);
        return true;
    }

    public void CallWhistlePet(PetWhistleRecord whistle)
    {
        if (whistle.Sea != AtSea)
        {
            SystemMessage(whistle.Sea ? "Sea pets can only be called at sea." : "Land pets can only be called on land.", SystemMessageKind.Prompt);
            return;
        }
        if (Character.Pet is { Out: true }) DismissWhistlePet();
        Character.Pet ??= new PetRecord();
        PetRecord pet = Character.Pet;
        pet.Name = whistle.PetName;
        pet.Kind = WhistleBook.Whistle(whistle.Key)?.Label ?? whistle.Key;
        pet.Level = whistle.Level;
        pet.Experience = whistle.Experience;
        pet.Fealty = whistle.Fealty;
        pet.Gorge = whistle.Gorge;
        pet.Health = whistle.Health;
        pet.Out = true;
        Character.CalledWhistle = whistle.ItemId;
        Save();
        if (!AtSea) ShowPet();
        Send(PetWhistlePackets.PetShow(PetObjectId, whistle.Sea ? 1 : 0, whistle.ItemId), "(pet called)");
        SendPetAutoSkills(whistle);
        StartPetAutoSkills();
        SystemMessage($"{pet.Name} answers your whistle.");
        Log.Info(Tag, $"{Name} calls {pet.Name} ({whistle.Key})");
    }

    public void DismissWhistlePet()
    {
        if (CalledPetWhistle is { } whistle) SyncWhistleFromPet(whistle);
        if (Character.Pet != null) Character.Pet.Out = false;
        Character.CalledWhistle = "";
        Save();
        if (!AtSea) Send(ScenePackets.RemoveObject(PetObjectId), "(pet dismissed)");
        Send(PetWhistlePackets.PetHide(PetObjectId), "(pet hidden)");
        Log.Info(Tag, $"{Name} sends the pet away");
    }

    public PetWhistleRecord? GiveWhistle(string key, bool sea, string petName = "")
    {
        List<PetWhistleRecord> whistles = PetWhistles;
        if (whistles.Count >= WhistleMostOwned) return null;
        PetWhistleDefinition? definition = WhistleBook.Whistle(key);
        var whistle = new PetWhistleRecord
        {
            ItemId = $"PW{DateTime.UtcNow.Ticks:x}{whistles.Count}",
            Key = definition?.Key ?? key,
            PetName = petName.Length > 0 ? petName : WhistlePetName(definition?.Label ?? key),
            Sea = sea,
            Quality = Random.Shared.Next(30, 81),
            Skills = [.. WhistleDefaultSkills],
        };
        whistles.Add(whistle);
        Save();
        if (_whistleViewsSent.Contains(sea ? PetWhistlePackets.SeaView : PetWhistlePackets.LandView)) OpenWhistles();
        return whistle;
    }

    public bool HandleWhistleOption(int target, int option)
    {
        if (option is not (WhistleBuyLandOption or WhistleBuySeaOption)) return false;
        bool sea = option == WhistleBuySeaOption;
        if (Money < WhistlePrice)
        {
            SystemMessage($"A pet whistle costs {WhistlePrice} silver coins.", SystemMessageKind.Prompt);
            return true;
        }
        string key = sea ? WhistleSeaKey : WhistleBook.Whistles.Where(whistle => whistle.Key.StartsWith("Whistle_", StringComparison.Ordinal))
            .Select(whistle => whistle.Key).Distinct().OrderBy(_ => Random.Shared.Next()).FirstOrDefault() ?? WhistleFirstKey;
        PetWhistleRecord? whistle = GiveWhistle(key, sea);
        if (whistle == null)
        {
            SystemMessage($"You cannot keep more than {WhistleMostOwned} pet whistles.", SystemMessageKind.Prompt);
            return true;
        }
        SetMoney(Money - WhistlePrice);
        Save();
        SystemMessage($"You bought a {WhistleBook.Whistle(whistle.Key)?.Label ?? whistle.Key}. Open the pet list to call {whistle.PetName}.");
        return true;
    }

    private List<PetWhistleRecord> EnsureWhistles()
    {
        Character.PetWhistles ??= [];
        List<PetWhistleRecord> whistles = Character.PetWhistles;
        if (whistles.Count == 0 && Character.Pet is { } pet)
        {
            var first = new PetWhistleRecord
            {
                ItemId = $"PW{DateTime.UtcNow.Ticks:x}0",
                Key = WhistleFirstKey,
                PetName = pet.Name,
                Level = pet.Level,
                Experience = pet.Experience,
                Fealty = pet.Fealty,
                Gorge = pet.Gorge,
                Health = pet.Health,
                Skills = [.. WhistleDefaultSkills],
            };
            whistles.Add(first);
            if (pet.Out) Character.CalledWhistle = first.ItemId;
        }
        foreach (PetWhistleRecord whistle in whistles)
        {
            while (whistle.AutoSkills.Count < PetWhistlePackets.AutoSlots) whistle.AutoSkills.Add("");
        }
        return whistles;
    }

    private PetWhistleRecord? WhistleAt(int view, int index)
    {
        List<PetWhistleRecord> list = PetWhistles.Where(whistle => whistle.Sea == (view == PetWhistlePackets.SeaView)).ToList();
        return index >= 1 && index <= list.Count ? list[index - 1] : null;
    }

    private void SendWhistleView(int view, List<PetWhistleRecord> whistles)
    {
        if (_whistleViewsSent.Contains(view)) Send(ViewPackets.Delete(view), "(pet list refresh)");
        _whistleViewsSent.Add(view);
        Send(ViewPackets.Create(view, PetWhistlePackets.Slots), view == PetWhistlePackets.SeaView ? "(sea pets)" : "(land pets)");
        for (int i = 0; i < Math.Min(whistles.Count, PetWhistlePackets.Slots); i++)
            Send(ViewPackets.AddItem(view, i + 1, WhistleItem(whistles[i], i + 1)), $"(pet {whistles[i].PetName})");
    }

    private PropertyBag WhistleItem(PetWhistleRecord whistle, int index)
    {
        int health = 40 + 15 * whistle.Level;
        return SeaPackets.Known(new PropertyBag
        {
            ["Name"] = whistle.Key,
            ["Index"] = index,
            ["Amount"] = 1,
            ["Kind"] = 1,
            ["ItemType"] = 1,
            ["ItemID"] = whistle.ItemId,
            ["PetName"] = whistle.PetName,
            ["PetLevel"] = whistle.Level,
            ["Level"] = whistle.Level,
            ["QualityPercent"] = whistle.Quality,
            ["PetWhistleType"] = whistle.Sea ? 1 : 0,
            ["PetAT"] = 4 + 3 * whistle.Level,
            ["PetDF"] = 2 + 2 * whistle.Level,
            ["PetMaxHP"] = health,
            ["HP"] = health,
            ["PetHitRatio"] = 75,
            ["PetDodgeRatio"] = 10,
            ["NowExp"] = whistle.Experience,
            ["UpgradeExp"] = 50 * whistle.Level,
            ["fealty"] = whistle.Fealty,
            ["gorge"] = whistle.Gorge,
            ["health"] = whistle.Health,
            ["PetCurLoad"] = 0,
            ["PetMaxLoad"] = 20f + 5f * whistle.Level,
            ["ExistTime"] = Unlimited,
        });
    }

    private void SyncWhistleFromPet(PetWhistleRecord whistle)
    {
        if (Character.Pet is not { } pet) return;
        whistle.PetName = pet.Name;
        whistle.Level = pet.Level;
        whistle.Experience = pet.Experience;
        whistle.Fealty = pet.Fealty;
        whistle.Gorge = pet.Gorge;
        whistle.Health = pet.Health;
    }

    private static string WhistlePetName(string label)
    {
        string name = label.Replace("'s whistle", "", StringComparison.OrdinalIgnoreCase).Replace(" Whistle", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" whistle", "", StringComparison.OrdinalIgnoreCase).Trim();
        int open = name.IndexOf('(');
        if (open > 0) name = name[..open].Trim();
        return name.Length == 0 ? "Pet" : name.Length > LongestPetName ? name[..LongestPetName] : name;
    }
}
