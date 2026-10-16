using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const double PetAutoSkillSeconds = 2.0;
    private const int PetAutoSkillMinCooldown = 5;
    private const string PetAutoFury = "Pet_A";
    private const string PetAutoAlert = "Pet_D";

    private readonly Dictionary<string, long> _petSkillReadyAt = [];
    private bool _petAutoSkillRunning;
    private string _petAutoStance = "";

    public void AddPetAutoSkill(string key, int slot)
    {
        if (CalledPetWhistle is not { } whistle)
        {
            SystemMessage("Call your pet first.", SystemMessageKind.Prompt);
            return;
        }
        if (slot is < 0 or >= PetWhistlePackets.AutoSlots) return;
        if (WhistleBook.Skill(key) == null)
        {
            Log.Info(Tag, $"{Name} drags unknown pet skill {key}");
            return;
        }
        if (!whistle.Skills.Contains(key)) whistle.Skills.Add(key);
        for (int i = 0; i < whistle.AutoSkills.Count; i++)
        {
            if (whistle.AutoSkills[i] == key) whistle.AutoSkills[i] = "";
        }
        whistle.AutoSkills[slot] = key;
        Save();
        SendPetAutoSkills(whistle);
    }

    public void RemovePetAutoSkill(int slot)
    {
        if (CalledPetWhistle is not { } whistle || slot is < 0 or >= PetWhistlePackets.AutoSlots) return;
        whistle.AutoSkills[slot] = "";
        whistle.AutoLock &= ~(1 << slot);
        Save();
        SendPetAutoSkills(whistle);
    }

    public void TogglePetAutoSkill(int slot)
    {
        if (CalledPetWhistle is not { } whistle || slot is < 0 or >= PetWhistlePackets.AutoSlots) return;
        if (whistle.AutoSkills[slot].Length == 0)
        {
            SystemMessage("Place a pet skill in this slot first.", SystemMessageKind.Prompt);
            return;
        }
        whistle.AutoLock ^= 1 << slot;
        Save();
        SendPetAutoSkills(whistle);
        StartPetAutoSkills();
    }

    public void ForgetPetSkill(int index)
    {
        PetWhistleRecord? whistle = CalledPetWhistle ?? WhistleAt(Character.PetWhistleTab == 0 ? PetWhistlePackets.LandView : PetWhistlePackets.SeaView, 1);
        if (whistle == null || whistle.Skills.Count == 0) return;
        if (index >= whistle.Skills.Count) index = whistle.Skills.Count - 1;
        if (index < 0) return;
        string key = whistle.Skills[index];
        whistle.Skills.RemoveAt(index);
        for (int i = 0; i < whistle.AutoSkills.Count; i++)
        {
            if (whistle.AutoSkills[i] != key) continue;
            whistle.AutoSkills[i] = "";
            whistle.AutoLock &= ~(1 << i);
        }
        Save();
        SendPetAutoSkills(whistle);
        SystemMessage($"Your pet has forgotten {WhistleBook.Skill(key)?.Name ?? key}.");
    }

    public void UsePetSkill(string key)
    {
        if (CalledPetWhistle is not { } whistle || Character.Pet is not { Out: true })
        {
            SystemMessage("Your pet is not with you.", SystemMessageKind.Prompt);
            return;
        }
        PetAutoSkillDefinition? skill = WhistleBook.Skill(key);
        if (skill == null || !whistle.Skills.Contains(key))
        {
            SystemMessage("Your pet has not learned this skill.", SystemMessageKind.Prompt);
            return;
        }
        CastPetSkill(skill, false);
    }

    public void SendPetAutoSkills(PetWhistleRecord whistle)
    {
        string list = string.Join(',', whistle.AutoSkills.Take(PetWhistlePackets.AutoSlots));
        var values = SeaPackets.Known(new PropertyBag
        {
            ["AutoSkillList"] = list,
            ["AutoSkillLock"] = whistle.AutoLock,
            ["dictate"] = _petAutoStance == PetAutoFury ? 1 : _petAutoStance == PetAutoAlert ? 2 : 0,
        });
        if (values.Count > 0 && !AtSea) Send(ScenePackets.ObjectProperties(PetObjectId, values), "(pet skill bar)");
        Send(PetWhistlePackets.SkillUpdate(PetObjectId, list), "(pet skill bar)");
    }

    private void StartPetAutoSkills()
    {
        if (_petAutoSkillRunning) return;
        _petAutoSkillRunning = true;
        Sea.Later(PetAutoSkillSeconds, PetAutoSkillTick);
    }

    private void PetAutoSkillTick()
    {
        if (!InWorld || CalledPetWhistle is not { } whistle || Character.Pet is not { Out: true } || whistle.AutoLock == 0)
        {
            _petAutoSkillRunning = false;
            return;
        }
        if (!AtSea && Mover != null && SkillTarget() is { } monster && Context.World.Monster(monster).Target == this)
        {
            for (int slot = 0; slot < PetWhistlePackets.AutoSlots; slot++)
            {
                if ((whistle.AutoLock & (1 << slot)) == 0 || WhistleBook.Skill(whistle.AutoSkills[slot]) is not { } skill) continue;
                if (CastPetSkill(skill, true)) break;
            }
        }
        Sea.Later(PetAutoSkillSeconds, PetAutoSkillTick);
    }

    private bool CastPetSkill(PetAutoSkillDefinition skill, bool automatic)
    {
        long now = Environment.TickCount64;
        if (_petSkillReadyAt.TryGetValue(skill.Key, out long ready) && now < ready)
        {
            if (!automatic) SystemMessage($"{skill.Name} is not ready yet.", SystemMessageKind.Prompt);
            return false;
        }
        PetRecord pet = Character.Pet!;
        if (skill.Skill is PetAutoFury or PetAutoAlert)
        {
            _petAutoStance = skill.Skill;
            _petSkillReadyAt[skill.Key] = now + Math.Max(PetAutoSkillMinCooldown, skill.CooldownSeconds) * 1000L;
            if (CalledPetWhistle is { } whistle) SendPetAutoSkills(whistle);
            SystemMessage($"{pet.Name}: {skill.Name}.");
            Send(PetWhistlePackets.SkillSet(PetObjectId, 1), "(pet skill used)");
            return true;
        }
        if (skill.Damage <= 0)
        {
            if (automatic) return false;
            _petSkillReadyAt[skill.Key] = now + Math.Max(PetAutoSkillMinCooldown, skill.CooldownSeconds) * 1000L;
            SystemMessage($"{pet.Name} uses {skill.Name}.");
            Send(PetWhistlePackets.SkillSet(PetObjectId, 1), "(pet skill used)");
            return true;
        }
        if (AtSea || Mover == null || SkillTarget() is not { } monster)
        {
            if (!automatic) SystemMessage("There is no enemy within reach.", SystemMessageKind.Prompt);
            return false;
        }
        MonsterState state = Context.World.Monster(monster);
        _petSkillReadyAt[skill.Key] = now + Math.Max(PetAutoSkillMinCooldown, skill.CooldownSeconds) * 1000L;
        Send(ScenePackets.PlaceObject(PetObjectId, monster.X + 1f, monster.Y, monster.Z), "(pet skill)");
        Send(ScenePackets.PlayAction(PetObjectId, Context.Data.Actions.Attack, attack: true), "(pet skill)");
        Context.World.Provoke(monster, this);
        Wound(monster, state, CombatRules.Damage(Rule.Combat, skill.Damage + 4 + 3 * pet.Level, MonsterDefence(monster, state)), 0xff4000);
        Send(PetWhistlePackets.SkillSet(PetObjectId, 1), "(pet skill used)");
        Log.Debug(Tag, $"{pet.Name} uses {skill.Key}{(automatic ? " (auto)" : "")} on {monster.Name}");
        return true;
    }
}
