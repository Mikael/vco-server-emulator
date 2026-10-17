namespace BountyBay.Core.Data;

public sealed class MonsterDefinition
{
    public int Id { get; set; }
    public int Scene { get; set; }
    public int Model { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Hp { get; set; }
    public int Attack { get; set; } = 3;
    public int Defence { get; set; }
    public int Hit { get; set; }
    public int Dodge { get; set; }
    public bool Aggressive { get; set; }
    public bool Flees { get; set; }
    public int Gold { get; set; }
    public string Key { get; set; } = "";
    public int RespawnSeconds { get; set; }
    public float RoamRadius { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 1.8f;
    public List<int> Attacks { get; set; } = [];
    public int DieAction { get; set; } = -1;
    public int DeathAction { get; set; } = -1;
    public int DieMilliseconds { get; set; }

    public float CollideRadius => Radius > 0f ? Radius : 0.5f;
}
