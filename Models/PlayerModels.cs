namespace SboxDumper.Models;

class PlayerDump
{
    public string Address { get; set; } = "";
    public string GameObjectAddress { get; set; } = "";
    public string? GameObjectName { get; set; }
    public string? TransformAddress { get; set; }
    public string? ControllerAddress { get; set; }

    // Identity
    public long SteamId { get; set; }
    public string? SteamName { get; set; }
    public string? RpName { get; set; }
    public string? PreferredTitle { get; set; }
    public string? CustomJob { get; set; }

    // Job
    public JobDump? Job { get; set; }

    // Economy
    public uint WalletBalance { get; set; }
    public uint BankBalance { get; set; }
    public int Level { get; set; }

    // Stats
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int PlayTime { get; set; }

    // State
    public bool IsTyping { get; set; }
    public bool Restricted { get; set; }
    public bool IsThirdPerson { get; set; }
    public bool IsDebugPlayer { get; set; }
    public float Spread { get; set; }

    // Faction
    public string? FactionId { get; set; }
    public string? FactionRoleId { get; set; }

    // Health + Armor
    public HealthDump? Health { get; set; }
    public ArmorDump? Armor { get; set; }

    // Transform
    public TransformDump? Transform { get; set; }

    // Equipment
    public string? EquipmentAddress { get; set; }
    public string? EquipmentType { get; set; }
    public string? WeaponGameObjectAddress { get; set; }
    public string? WeaponName { get; set; }

    // Components on this player
    public List<ComponentRef> Components { get; set; } = [];
}

class JobDump
{
    public string Address { get; set; } = "";
    public string? Name { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int Salary { get; set; }
    public int MaxSlots { get; set; }
    public string? ResourceName { get; set; }
    public string? ResourcePath { get; set; }
}

class HealthDump
{
    public float Current { get; set; }
    public float Max { get; set; }
    public bool IsDead { get; set; }
}

class ArmorDump
{
    public float Current { get; set; }
    public float Max { get; set; }
    public bool HasHelmet { get; set; }
}

class TransformDump
{
    public int FieldOffset { get; set; }
    public Vec3 Position { get; set; } = new();
    public Vec3 Scale { get; set; } = new();
    public Vec4 Rotation { get; set; } = new();
}

class Vec3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

class Vec4
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
}

class ComponentRef
{
    public string Address { get; set; } = "";
    public string Type { get; set; } = "";
}
