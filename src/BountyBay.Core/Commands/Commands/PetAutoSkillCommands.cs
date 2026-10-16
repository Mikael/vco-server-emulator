using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class PetAutoSkillCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csAddAutoSkill", "csRemoveAutoSkill", "csPetSkillAutoOn", "csForgetPetSkill", "PetSkill"];

    public bool Execute(ClientSession session, string[] words)
    {
        switch (words[0])
        {
            case "csAddAutoSkill" when words.Length >= 3 && int.TryParse(words[^1], out int slot):
                session.AddPetAutoSkill(string.Join(' ', words[1..^1]).Replace('\u0003', ' '), slot);
                return true;
            case "csRemoveAutoSkill" when words.Length >= 2 && int.TryParse(words[1], out int slot):
                session.RemovePetAutoSkill(slot);
                return true;
            case "csPetSkillAutoOn" when words.Length >= 2 && int.TryParse(words[1], out int slot):
                session.TogglePetAutoSkill(slot);
                return true;
            case "csForgetPetSkill" when words.Length >= 2 && int.TryParse(words[1], out int index):
                session.ForgetPetSkill(index);
                return true;
            case "PetSkill" when words.Length >= 2:
                session.UsePetSkill(string.Join(' ', words.Skip(1)).Replace('\u0003', ' '));
                return true;
            default:
                return false;
        }
    }
}
