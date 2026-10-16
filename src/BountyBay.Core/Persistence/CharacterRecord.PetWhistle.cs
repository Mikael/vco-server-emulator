namespace BountyBay.Core.Persistence;

public sealed class PetWhistleRecord
{
    public string ItemId { get; set; } = "";
    public string Key { get; set; } = "Whistle_Bird";
    public string PetName { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public float Quality { get; set; } = 50f;
    public bool Sea { get; set; }
    public int Fealty { get; set; } = 60;
    public int Gorge { get; set; } = 80;
    public int Health { get; set; } = 100;
    public List<string> Skills { get; set; } = [];
    public List<string> AutoSkills { get; set; } = ["", "", "", ""];
    public int AutoLock { get; set; }
}

public sealed partial class CharacterRecord
{
    public List<PetWhistleRecord>? PetWhistles { get; set; }
    public string CalledWhistle { get; set; } = "";
    public int PetWhistleTab { get; set; }
}
