namespace BountyBay.Core.Persistence;

public sealed class ItemStack
{
    public string Name { get; set; } = "";
    public int Photo { get; set; }
    public int Amount { get; set; } = 1;
    public int Cost { get; set; }
    public ArmUpgradeState? Upgrade { get; set; }
    public ArmRebuildState? Rebuild { get; set; }
    public RankArmState? Rank { get; set; }
    public AideArmState? AideArm { get; set; }

    public ItemStack Clone() => new() { Name = Name, Photo = Photo, Amount = Amount, Cost = Cost, Upgrade = Upgrade?.Clone(), Rebuild = Rebuild?.Clone(), Rank = Rank?.Clone(), AideArm = AideArm?.Clone() };
}

public sealed class ShortcutEntry
{
    public int Kind { get; set; }
    public string Name { get; set; } = "";
}

public sealed class SkillEntry
{
    public int Id { get; set; }
    public int Level { get; set; } = 1;
}

public sealed partial class CharacterRecord
{
    public string Name { get; set; } = "Player";
    public int Role { get; set; } = 3001001;
    public int Face { get; set; } = 3101001;
    public int Body { get; set; } = 3401001;
    public int Hair { get; set; }
    public int HairColor { get; set; }
    public int City { get; set; }
    public int ShortcutPage { get; set; }
    public List<string> Titles { get; set; } = [];
    public int Sailors { get; set; } = 10;
    public int Money { get; set; } = -1;
    public int Bank { get; set; }
    public int Reputation { get; set; }
    public int Amity { get; set; }
    public int Rank { get; set; }
    public int WarehouseLoad { get; set; }
    public int LastPort { get; set; }
    public int RulesVersion { get; set; }
    public int Scene { get; set; }
    public float[]? Position { get; set; }
    public string Title { get; set; } = "";
    public List<SkillEntry> Skills { get; set; } = [];
    public Dictionary<int, ItemStack>? Items { get; set; }
    public Dictionary<int, Dictionary<int, ItemStack>>? Bags { get; set; }
    public Dictionary<int, ShortcutEntry> Shortcuts { get; set; } = [];
    public SortedSet<string> Friends { get; set; } = [];
    public SortedSet<string> Blacklist { get; set; } = [];
}

public sealed class AccountRecord
{
    public string PasswordHash { get; set; } = "";
    public List<CharacterRecord> Characters { get; set; } = [];
    public DateTime? BannedUntil { get; set; }
    public string BanReason { get; set; } = "";
}
