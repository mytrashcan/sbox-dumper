using Microsoft.Diagnostics.Runtime;
using SboxDumper.Models;
using SboxDumper.Readers;

namespace SboxDumper.Services;

/// Builds offset tables and DMA offset maps from cached ClrTypes.
/// No heap walk needed — uses the TypeCache from HeapSnapshot.
static class OffsetDumper
{
    static readonly (string TypeName, string Category)[] Targets =
    [
        ("Sandbox.GameObject",                   "GameObject"),
        ("Sandbox.GameTransform",                "GameTransform"),
        ("Sandbox.PlayerController",             "PlayerController"),
        ("Sandbox.CharacterController",          "CharacterController"),
        ("Sandbox.CameraComponent",              "CameraComponent"),
        ("Sandbox.ModelRenderer",                "ModelRenderer"),
        ("Sandbox.SkinnedModelRenderer",         "SkinnedModelRenderer"),
        ("Sandbox.Dresser",                      "Dresser"),
        ("Dxura.RP.Game.Player",                 "DxrpPlayer"),
        ("Dxura.RP.Game.HealthComponent",        "HealthComponent"),
        ("Dxura.RP.Game.ArmorComponent",         "ArmorComponent"),
        ("Dxura.RP.Game.PlayerVoiceComponent",   "PlayerVoiceComponent"),
        ("Dxura.RP.Game.Equipment",              "Equipment"),
        ("Dxura.RP.Game.JobResource",            "JobResource"),
        ("Dxura.RP.Game.AnimationHelper",        "AnimationHelper"),
        ("Dxura.RP.Game.TagBinder",              "TagBinder"),
        ("Dxura.RP.Game.Door",                   "Door"),
        ("Dxura.RP.Game.Prop",                   "Prop"),
        ("Dxura.RP.Game.Tools.FadingDoorTool",   "FadingDoorTool"),
        ("Dxura.RP.Game.Entities.PrinterEntity", "PrinterEntity"),
    ];

    public static void Dump(HeapSnapshot snap, DumpResult dump)
    {
        Console.WriteLine("\n── Offset Table ─────────────────────────────────────");

        foreach (var (typeName, category) in Targets)
        {
            if (!snap.TypeCache.TryGetValue(typeName, out var type)) continue;

            var entry = new OffsetTableEntry
            {
                TypeName = typeName,
                MethodTable = $"0x{type.MethodTable:X}",
                BaseSize = type.StaticSize,
                Parent = type.BaseType?.Name,
            };

            foreach (var f in FieldReaders.CollectAllFields(type))
            {
                var name = f.Name ?? "?";
                entry.Fields.Add(new OffsetField
                {
                    Name = name,
                    CleanName = FieldReaders.CleanFieldName(name),
                    Offset = f.Offset,
                    OffsetHex = $"0x{f.Offset:X}",
                    Type = f.Type?.Name ?? "?",
                    Size = f.Size,
                    IsValueType = f.IsValueType,
                    IsSync = name.Contains("_SyncAttribute__"),
                });
            }

            dump.Offsets[category] = entry;
            Console.WriteLine($"  {category,-25} ({typeName}) — {entry.Fields.Count} fields");
        }

        BuildDmaOffsetMap(dump);
    }

    // ── DMA offset map (condensed, named) ───────────────
    static void BuildDmaOffsetMap(DumpResult dump)
    {
        Console.WriteLine("\n── DMA Offset Map ───────────────────────────────────");

        // GameObject
        Map(dump, "GameObject", "<Network>k__BackingField", "Network");
        Map(dump, "GameObject", "<Scene>k__BackingField",   "Scene");
        Map(dump, "GameObject", "_gameTransform",           "GameTransform");
        Map(dump, "GameObject", "_name",                    "Name");
        Map(dump, "GameObject", "_parent",                  "Parent");
        Map(dump, "GameObject", "_enabled",                 "Enabled");
        Map(dump, "GameObject", "_components",              "Components");

        // GameTransform
        Map(dump, "GameTransform", "_targetLocal",       "TargetLocal");
        Map(dump, "GameTransform", "_interpolatedLocal", "InterpolatedLocal");

        // PlayerController
        Map(dump, "PlayerController", "<WishVelocity>k__BackingField", "WishVelocity");
        Map(dump, "PlayerController", "<Velocity>k__BackingField",     "Velocity");
        Map(dump, "PlayerController", "<EyeAngles>k__BackingField",    "EyeAngles");
        Map(dump, "PlayerController", "<IsDucking>k__BackingField",    "IsDucking");
        Map(dump, "PlayerController", "<IsClimbing>k__BackingField",   "IsClimbing");
        Map(dump, "PlayerController", "<IsSwimming>k__BackingField",   "IsSwimming");
        Map(dump, "PlayerController", "<IsOnGround>k__BackingField",   "IsOnGround");

        // CharacterController
        Map(dump, "CharacterController", "<Velocity>k__BackingField",   "Velocity");
        Map(dump, "CharacterController", "<IsOnGround>k__BackingField", "IsOnGround");

        // CameraComponent
        Map(dump, "CameraComponent", "<FieldOfView>k__BackingField", "FieldOfView");

        // DXRP Player
        Map(dump, "DxrpPlayer", "<GameObject>k__BackingField",        "GameObject");
        Map(dump, "DxrpPlayer", "<Renderer>k__BackingField",          "Renderer");
        Map(dump, "DxrpPlayer", "<Rigidbody>k__BackingField",         "Rigidbody");
        Map(dump, "DxrpPlayer", "<AnimationHelper>k__BackingField",   "AnimationHelper");
        Map(dump, "DxrpPlayer", "<CurrentEquipment>k__BackingField",  "CurrentEquipment");
        Map(dump, "DxrpPlayer", "<WeaponGameObject>k__BackingField",  "WeaponGameObject");
        Map(dump, "DxrpPlayer", "<HealthComponent>k__BackingField",   "HealthComponent");
        Map(dump, "DxrpPlayer", "<ArmorComponent>k__BackingField",    "ArmorComponent");
        Map(dump, "DxrpPlayer", "<Voice>k__BackingField",             "Voice");
        Map(dump, "DxrpPlayer", "<CustomJob>k__BackingField",         "CustomJob");
        Map(dump, "DxrpPlayer", "<SteamName>k__BackingField",         "SteamName");
        Map(dump, "DxrpPlayer", "<RpName>k__BackingField",            "RpName");
        Map(dump, "DxrpPlayer", "<PreferredTitle>k__BackingField",    "PreferredTitle");
        Map(dump, "DxrpPlayer", "<Job>k__BackingField",               "Job");
        Map(dump, "DxrpPlayer", "<Statuses>k__BackingField",          "Statuses");
        Map(dump, "DxrpPlayer", "<Controller>k__BackingField",        "Controller");
        Map(dump, "DxrpPlayer", "<SteamId>k__BackingField",           "SteamId");
        Map(dump, "DxrpPlayer", "<Spread>k__BackingField",            "Spread");
        Map(dump, "DxrpPlayer", "<WalletBalance>k__BackingField",     "WalletBalance");
        Map(dump, "DxrpPlayer", "<BankBalance>k__BackingField",       "BankBalance");
        Map(dump, "DxrpPlayer", "<Level>k__BackingField",             "Level");
        Map(dump, "DxrpPlayer", "<Kills>k__BackingField",             "Kills");
        Map(dump, "DxrpPlayer", "<Deaths>k__BackingField",            "Deaths");
        Map(dump, "DxrpPlayer", "<PlayTime>k__BackingField",          "PlayTime");
        Map(dump, "DxrpPlayer", "<RespawnState>k__BackingField",      "RespawnState");
        Map(dump, "DxrpPlayer", "<IsTyping>k__BackingField",          "IsTyping");
        Map(dump, "DxrpPlayer", "<IsThirdPersonPreferred>k__BackingField", "IsThirdPerson");
        Map(dump, "DxrpPlayer", "<Restricted>k__BackingField",        "Restricted");
        Map(dump, "DxrpPlayer", "<AimRay>k__BackingField",            "AimRay");
        Map(dump, "DxrpPlayer", "<DamageTakenPosition>k__BackingField", "DamageTakenPosition");
        Map(dump, "DxrpPlayer", "<DamageTakenForce>k__BackingField",  "DamageTakenForce");
        Map(dump, "DxrpPlayer", "<FactionId>k__BackingField",         "FactionId");
        Map(dump, "DxrpPlayer", "<FactionRoleId>k__BackingField",     "FactionRoleId");
        Map(dump, "DxrpPlayer", "<Sit>k__BackingField",               "Sit");

        // HealthComponent
        Map(dump, "HealthComponent", "<Health>k__BackingField",    "Health");
        Map(dump, "HealthComponent", "<MaxHealth>k__BackingField", "MaxHealth");
        Map(dump, "HealthComponent", "<IsDead>k__BackingField",    "IsDead");
        Map(dump, "HealthComponent", "<LifeState>k__BackingField", "LifeState");
        Map(dump, "HealthComponent", "<IsGodMode>k__BackingField", "IsGodMode");

        // ArmorComponent
        Map(dump, "ArmorComponent", "<Armor>k__BackingField",     "Armor");
        Map(dump, "ArmorComponent", "<MaxArmor>k__BackingField",  "MaxArmor");
        Map(dump, "ArmorComponent", "<HasHelmet>k__BackingField", "HasHelmet");

        // JobResource
        Map(dump, "JobResource", "<Name>k__BackingField",        "Name");
        Map(dump, "JobResource", "<Title>k__BackingField",       "Title");
        Map(dump, "JobResource", "<Description>k__BackingField", "Description");
        Map(dump, "JobResource", "<Salary>k__BackingField",      "Salary");
        Map(dump, "JobResource", "<MaxSlots>k__BackingField",    "MaxSlots");
        Map(dump, "JobResource", "<Category>k__BackingField",    "Category");

        // PrinterEntity
        Map(dump, "PrinterEntity", "<Owner>k__BackingField",          "Owner");
        Map(dump, "PrinterEntity", "<CurrentBalance>k__BackingField", "CurrentBalance");
        Map(dump, "PrinterEntity", "<MaxBalance>k__BackingField",     "MaxBalance");
        Map(dump, "PrinterEntity", "<PrintRate>k__BackingField",      "PrintRate");

        // Prop
        Map(dump, "Prop", "<GameObject>k__BackingField",      "GameObject");
        Map(dump, "Prop", "<FadeDuration>k__BackingField",    "FadeDuration");
        Map(dump, "Prop", "<FadingDoor>k__BackingField",      "FadingDoor");
        Map(dump, "Prop", "<Fade>k__BackingField",            "Fade");
        Map(dump, "Prop", "<IsFadingBreached>k__BackingField", "IsFadingBreached");
        Map(dump, "Prop", "<IsReversed>k__BackingField",      "IsReversed");

        // Dresser
        Map(dump, "Dresser", "<ManualHeight>k__BackingField", "ManualHeight");
        Map(dump, "Dresser", "<ManualTint>k__BackingField",   "ManualTint");
        Map(dump, "Dresser", "<ManualAge>k__BackingField",    "ManualAge");

        // DxrpPlayer — additional fields for auto-updater
        Map(dump, "DxrpPlayer", "<IsPulsing>k__BackingField",         "IsPulsing");
        Map(dump, "DxrpPlayer", "<IsDebugPlayer>k__BackingField",     "IsDebugPlayer");
        Map(dump, "DxrpPlayer", "<DisconnectedSince>k__BackingField", "DisconnectedSince");

        // Print summary
        foreach (var (cat, entries) in dump.DmaOffsets.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"  [{cat}]");
            foreach (var (name, off) in entries.OrderBy(kv => kv.Value.Offset))
                Console.WriteLine($"    {name,-30} +0x{off.Offset:X3}  ({off.Type}, {off.Size}b)");
        }
    }

    static void Map(DumpResult dump, string category, string fieldName, string cleanName)
    {
        if (!dump.Offsets.TryGetValue(category, out var table)) return;
        var field = table.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field == null) return;

        if (!dump.DmaOffsets.ContainsKey(category))
            dump.DmaOffsets[category] = new();

        dump.DmaOffsets[category][cleanName] = new DmaOffset
        {
            Offset = field.Offset,
            OffsetHex = field.OffsetHex,
            Type = field.Type,
            Size = field.Size,
        };
    }
}
