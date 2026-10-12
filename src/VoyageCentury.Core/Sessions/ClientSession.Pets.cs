using VoyageCentury.Core.Data;
using VoyageCentury.Core.Logging;
using VoyageCentury.Core.Persistence;
using VoyageCentury.Core.Protocol;
using VoyageCentury.Core.Protocol.Packets;

namespace VoyageCentury.Core.Sessions;

public sealed partial class ClientSession
{
    private const int PetObjectOffset = 100000;
    private const int PetModel = 3201003;
    private const int LongestPetName = 16;
    private const int Unlimited = -1;

    public int PetObjectId => PetObjectOffset + ObjectId;

    public void ShowPetMenu(int npc)
    {
        if (Character.Pet == null)
        {
            Character.Pet = new PetRecord();
            Save();
            SystemMessage("You have been given a pet.", SystemMessageKind.Upgrade);
            Log.Info(Tag, $"{Name} receives a pet");
        }
        PetRecord pet = Character.Pet;
        Send(ChatPackets.NpcDialogue(npc,
        [
            (DialogueEntry.Text, 0, $"{pet.Name} is level {pet.Level} and {(pet.Out ? "at your side" : "resting")}."),
            (DialogueEntry.Option, PetCallOption, pet.Out ? "Send my pet to rest" : "Call my pet"),
            (DialogueEntry.Option, PetWindowOption, "Look at my pet"),
            (DialogueEntry.Option, ByeOption, "Goodbye"),
        ]), "(pet dialogue)");
    }

    public void TogglePet()
    {
        if (Character.Pet == null) return;
        Character.Pet.Out = !Character.Pet.Out;
        Save();
        if (Character.Pet.Out) ShowPet();
        else if (!AtSea) Send(ScenePackets.RemoveObject(PetObjectId), "(pet rests)");
    }

    public void ShowPet()
    {
        PetRecord? pet = Character.Pet;
        if (pet is not { Out: true } || Mover == null || AtSea) return;
        (float x, float z) = Mover.Now();
        Send(ScenePackets.CreateObject(PetObjectId, ObjectKind.Npc, PetModel), "(pet)");
        Send(ScenePackets.ObjectProperties(PetObjectId, PetProperties(pet)), "(pet properties)");
        Send(ScenePackets.PlaceObject(PetObjectId, x + 1.5f, Height, z + 1.5f), "(place pet)");
        Send(ScenePackets.ShowObject(PetObjectId), "(show pet)");
    }

    public void OpenPetWindow()
    {
        PetRecord? pet = Character.Pet;
        if (pet == null)
        {
            SystemMessage("You do not have a pet.");
            return;
        }
        if (AtSea)
        {
            SystemMessage("Your pet stays below deck at sea.");
            return;
        }
        if (!pet.Out)
        {
            pet.Out = true;
            Save();
        }
        ShowPet();
        Send(ChatPackets.Script(PetObjectId, "OpenPetWnd"), "(pet window)");
    }

    public bool ClickPet(int target)
    {
        if (target != PetObjectId || Character.Pet is not { Out: true }) return false;
        Send(ChatPackets.Script(PetObjectId, "OpenPetWnd"), "(pet window)");
        return true;
    }

    public void RenamePet(string name)
    {
        PetRecord? pet = Character.Pet;
        if (pet == null || name.Length == 0)
        {
            SystemMessage("You do not have a pet to name.");
            return;
        }
        pet.Name = name.Length > LongestPetName ? name[..LongestPetName] : name;
        Save();
        UpdatePet(new PropertyBag { ["Name"] = pet.Name });
        SystemMessage($"Your pet is now called {pet.Name}.");
    }

    public void CommandPet(string order)
    {
        PetRecord? pet = Character.Pet;
        if (pet is not { Out: true } || Mover == null)
        {
            SystemMessage("Your pet is not with you.");
            return;
        }
        switch (order)
        {
            case "Pet_Attack":
                MonsterDefinition? monster = SkillTarget();
                if (monster == null)
                {
                    SystemMessage("There is no enemy within reach.");
                    return;
                }
                Send(ScenePackets.PlaceObject(PetObjectId, monster.X + 1f, monster.Y, monster.Z), "(pet attacks)");
                Send(ScenePackets.PlayAction(PetObjectId, Context.Data.Actions.Attack, attack: true), "(pet attacks)");
                StrikeWithSkill(monster, 4 + 3 * pet.Level, swing: false);
                break;
            case "Pet_Follow":
                (float x, float z) = Mover.Now();
                Send(ScenePackets.PlaceObject(PetObjectId, x + 1.5f, Height, z + 1.5f), "(pet follows)");
                break;
            case "Pet_Stop":
                SystemMessage($"{pet.Name} stays where it is.");
                break;
            default:
                SystemMessage("Your pet does not understand that order.");
                break;
        }
    }

    public void PetGainExperience(int amount)
    {
        PetRecord? pet = Character.Pet;
        if (pet is not { Out: true }) return;
        pet.Experience += amount;
        while (pet.Experience >= PetUpgradeExperience(pet))
        {
            pet.Experience -= PetUpgradeExperience(pet);
            pet.Level++;
            SystemMessage($"{pet.Name} has reached level {pet.Level}!", SystemMessageKind.Upgrade);
        }
        UpdatePet(PetProperties(pet));
    }

    private static int PetUpgradeExperience(PetRecord pet) => 50 * pet.Level;

    private void UpdatePet(PropertyBag values)
    {
        if (Character.Pet is { Out: true } && InWorld && !AtSea)
            Send(ScenePackets.ObjectProperties(PetObjectId, values), "(pet properties)");
    }

    private PropertyBag PetProperties(PetRecord pet)
    {
        int health = 40 + 15 * pet.Level;
        var values = new PropertyBag
        {
            ["Name"] = pet.Name,
            ["Model"] = PetModel,
            ["ActionSet"] = 0,
            ["CollideRadius"] = 0.5f,
            ["HP"] = health,
            ["MaxHP"] = health,
            ["RatioHP"] = 100,
            ["RatioSP"] = 100,
            ["Level"] = pet.Level,
            ["AT"] = 4 + 3 * pet.Level,
            ["DF"] = 2 + 2 * pet.Level,
            ["HitRatio"] = 75,
            ["DodgeRatio"] = 10,
            ["fealty"] = pet.Fealty,
            ["PetCurLoad"] = 0,
            ["life"] = Unlimited,
            ["ExistTime"] = Unlimited,
            ["PetLevel"] = pet.Level,
            ["gorge"] = pet.Gorge,
            ["health"] = pet.Health,
            ["appetency"] = pet.Appetency,
            ["NowExp"] = pet.Experience,
            ["UpgradeExp"] = PetUpgradeExperience(pet),
            ["PetTypeDesc"] = pet.Kind,
        };
        foreach (string missing in values.Keys.Where(key => !PropertyTable.Has(key)).ToList()) values.Remove(missing);
        return values;
    }
}
