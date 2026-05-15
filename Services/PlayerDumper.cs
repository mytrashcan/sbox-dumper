using Microsoft.Diagnostics.Runtime;
using SboxDumper.Models;
using static SboxDumper.Readers.FieldReaders;

namespace SboxDumper.Services;

/// Extracts live player data from pre-collected Player objects.
/// No heap walk — uses HeapSnapshot.PlayerObjects + ComponentMap.
static class PlayerDumper
{
    public static void Dump(ClrRuntime runtime, HeapSnapshot snap, DumpResult dump)
    {
        Console.WriteLine("\n── Players ──────────────────────────────────────────");

        foreach (var obj in snap.PlayerObjects)
        {
            if (obj.Type?.Name != "Dxura.RP.Game.Player") continue;

            TryReadField(obj, "<GameObject>k__BackingField", out var goObj);
            if (!goObj.IsValid) continue;

            var goName = TryReadString(goObj, "_name");
            bool startCalled = TryReadBool(obj, "_startCalled");
            var steamId = TryReadInt64(obj, "<SteamId>k__BackingField");
            if (steamId == 0 && !startCalled) continue;

            Console.WriteLine($"\n  ── Player: \"{goName}\" ──");

            var player = new PlayerDump
            {
                Address = $"0x{obj.Address:X}",
                GameObjectAddress = $"0x{goObj.Address:X}",
                GameObjectName = goName,
                SteamId = steamId,
                SteamName = TryReadString(obj, "<SteamName>k__BackingField"),
                RpName = TryReadString(obj, "<RpName>k__BackingField"),
                PreferredTitle = TryReadString(obj, "<PreferredTitle>k__BackingField"),
                CustomJob = TryReadString(obj, "<CustomJob>k__BackingField"),
            };

            Console.WriteLine($"    SteamId:    {player.SteamId}");
            Console.WriteLine($"    SteamName:  {player.SteamName}");
            Console.WriteLine($"    RpName:     {player.RpName}");

            // Job
            TryReadField(obj, "<Job>k__BackingField", out var jobObj);
            if (jobObj.IsValid)
            {
                player.Job = ReadJobResource(jobObj);
                Console.WriteLine($"    Job:        {player.Job.Name} ({player.Job.Title})");
            }

            // Economy
            player.WalletBalance = TryReadUInt32(obj, "<WalletBalance>k__BackingField");
            player.BankBalance = TryReadUInt32(obj, "<BankBalance>k__BackingField");
            player.Level = TryReadInt32(obj, "<Level>k__BackingField");
            Console.WriteLine($"    Wallet:     ${player.WalletBalance}  Bank: ${player.BankBalance}  Level: {player.Level}");

            // Stats
            player.Kills = TryReadInt32(obj, "<Kills>k__BackingField");
            player.Deaths = TryReadInt32(obj, "<Deaths>k__BackingField");
            player.PlayTime = TryReadInt32(obj, "<PlayTime>k__BackingField");

            // State
            player.IsTyping = TryReadBool(obj, "<IsTyping>k__BackingField");
            player.Restricted = TryReadBool(obj, "<Restricted>k__BackingField");
            player.IsThirdPerson = TryReadBool(obj, "<IsThirdPersonPreferred>k__BackingField");
            player.IsDebugPlayer = TryReadBool(obj, "<IsDebugPlayer>k__BackingField");
            player.Spread = TryReadFloat(obj, "<Spread>k__BackingField");

            // Health
            TryReadField(obj, "<HealthComponent>k__BackingField", out var hcObj);
            if (hcObj.IsValid)
            {
                player.Health = ReadHealthComponent(hcObj);
                Console.WriteLine($"    Health:     {player.Health.Current}/{player.Health.Max} (Dead={player.Health.IsDead})");
            }

            // Armor
            TryReadField(obj, "<ArmorComponent>k__BackingField", out var acObj);
            if (acObj.IsValid)
            {
                player.Armor = ReadArmorComponent(acObj);
                Console.WriteLine($"    Armor:      {player.Armor.Current}/{player.Armor.Max} (Helmet={player.Armor.HasHelmet})");
            }

            // Transform
            TryReadField(goObj, "_gameTransform", out var gtObj);
            if (gtObj.IsValid)
            {
                player.TransformAddress = $"0x{gtObj.Address:X}";
                player.Transform = ReadTransformData(runtime, gtObj);
                if (player.Transform != null)
                    Console.WriteLine($"    Position:   ({player.Transform.Position.X:F1}, {player.Transform.Position.Y:F1}, {player.Transform.Position.Z:F1})");
            }

            // Faction
            player.FactionId = TryReadGuid(obj, "<FactionId>k__BackingField");
            player.FactionRoleId = TryReadGuid(obj, "<FactionRoleId>k__BackingField");

            // Controller
            TryReadField(obj, "<Controller>k__BackingField", out var ctrlObj);
            if (ctrlObj.IsValid) player.ControllerAddress = $"0x{ctrlObj.Address:X}";

            // Equipment
            TryReadField(obj, "<CurrentEquipment>k__BackingField", out var eqObj);
            if (eqObj.IsValid)
            {
                player.EquipmentAddress = $"0x{eqObj.Address:X}";
                player.EquipmentType = eqObj.Type?.Name;
            }

            TryReadField(obj, "<WeaponGameObject>k__BackingField", out var wpnObj);
            if (wpnObj.IsValid)
            {
                player.WeaponGameObjectAddress = $"0x{wpnObj.Address:X}";
                player.WeaponName = TryReadString(wpnObj, "_name");
                Console.WriteLine($"    Weapon:     {player.WeaponName}");
            }

            // Components — O(1) lookup from pre-built map instead of heap walk
            if (snap.ComponentMap.TryGetValue(goObj.Address, out var comps))
                player.Components = comps;
            Console.WriteLine($"    Components: {player.Components.Count}");

            dump.Players.Add(player);
        }

        if (dump.Players.Count == 0)
            Console.WriteLine("  [!] No DXRP players found. Make sure you're in a game session.");

        Console.WriteLine();
    }

    // ── Component readers ───────────────────────────────

    static JobDump ReadJobResource(ClrObject jobObj)
    {
        var job = new JobDump { Address = $"0x{jobObj.Address:X}" };

        foreach (var f in jobObj.Type?.Fields ?? [])
        {
            var name = f.Name ?? "";
            if (f.Type?.Name == "System.String" && !name.Contains("SyncAttribute"))
            {
                var val = TryReadStringField(jobObj, f);
                if (val == null) continue;

                if (name.Contains("Name", StringComparison.OrdinalIgnoreCase))
                    job.Name ??= val;
                else if (name.Contains("Title", StringComparison.OrdinalIgnoreCase))
                    job.Title ??= val;
                else if (name.Contains("Description", StringComparison.OrdinalIgnoreCase))
                    job.Description ??= val;
                else if (name.Contains("Category", StringComparison.OrdinalIgnoreCase))
                    job.Category ??= val;
            }
            else if (name.Contains("Salary", StringComparison.OrdinalIgnoreCase))
                job.Salary = TryReadInt32Field(jobObj, f);
            else if (name.Contains("MaxSlots", StringComparison.OrdinalIgnoreCase))
                job.MaxSlots = TryReadInt32Field(jobObj, f);
        }

        // Walk base types for ResourceName/Path
        var baseType = jobObj.Type?.BaseType;
        while (baseType != null && baseType.Name != "System.Object")
        {
            foreach (var f in baseType.Fields)
            {
                var name = f.Name ?? "";
                if (f.Type?.Name != "System.String") continue;
                var val = TryReadStringField(jobObj, f);
                if (val == null) continue;

                if (name.Contains("ResourceName")) job.ResourceName ??= val;
                else if (name.Contains("ResourcePath")) job.ResourcePath ??= val;
                else if (job.Name == null && name.Contains("Name")) job.Name = val;
            }
            baseType = baseType.BaseType;
        }

        return job;
    }

    static HealthDump ReadHealthComponent(ClrObject obj)
    {
        var h = new HealthDump();
        foreach (var f in CollectAllFields(obj.Type!))
        {
            var name = f.Name ?? "";
            var tn = f.Type?.Name ?? "";

            if (tn == "System.Single")
            {
                var val = TryReadFloatField(obj, f);
                if (name.Contains("MaxHealth", StringComparison.OrdinalIgnoreCase)) h.Max = val;
                else if (name.Contains("Health", StringComparison.OrdinalIgnoreCase)) h.Current = val;
            }
            else if (tn == "System.Boolean" && name.Contains("IsDead", StringComparison.OrdinalIgnoreCase))
                h.IsDead = TryReadBoolField(obj, f);
        }
        return h;
    }

    static ArmorDump ReadArmorComponent(ClrObject obj)
    {
        var a = new ArmorDump();
        foreach (var f in CollectAllFields(obj.Type!))
        {
            var name = f.Name ?? "";
            var tn = f.Type?.Name ?? "";

            if (tn == "System.Single")
            {
                var val = TryReadFloatField(obj, f);
                if (name.Contains("MaxArmor", StringComparison.OrdinalIgnoreCase)) a.Max = val;
                else if (name.Contains("Armor", StringComparison.OrdinalIgnoreCase)) a.Current = val;
            }
            else if (tn == "System.Boolean" && name.Contains("Helmet", StringComparison.OrdinalIgnoreCase))
                a.HasHelmet = TryReadBoolField(obj, f);
        }
        return a;
    }

    static TransformDump? ReadTransformData(ClrRuntime runtime, ClrObject gtObj)
    {
        var targetField = gtObj.Type?.GetFieldByName("_targetLocal");
        if (targetField == null) return null;

        int baseOffset = targetField.Offset;
        var addr = gtObj.Address;

        try
        {
            var rawBytes = new byte[40];
            runtime.DataTarget.DataReader.Read(addr + (ulong)baseOffset + 8, rawBytes);

            var pos   = ReadVec3(rawBytes, 0);
            var scale = ReadVec3(rawBytes, 12);
            var rot   = ReadVec4(rawBytes, 24);

            // Sanity: if all zeros or NaN, try without MT offset
            if (float.IsNaN(pos.X) || (pos.X == 0 && pos.Y == 0 && pos.Z == 0 && scale.X == 0))
            {
                runtime.DataTarget.DataReader.Read(addr + (ulong)baseOffset, rawBytes);
                pos   = ReadVec3(rawBytes, 0);
                scale = ReadVec3(rawBytes, 12);
                rot   = ReadVec4(rawBytes, 24);
            }

            return new TransformDump
            {
                FieldOffset = baseOffset,
                Position = pos,
                Scale = scale,
                Rotation = rot,
            };
        }
        catch { return null; }
    }

    static Vec3 ReadVec3(byte[] buf, int off) => new()
    {
        X = BitConverter.ToSingle(buf, off),
        Y = BitConverter.ToSingle(buf, off + 4),
        Z = BitConverter.ToSingle(buf, off + 8),
    };

    static Vec4 ReadVec4(byte[] buf, int off) => new()
    {
        X = BitConverter.ToSingle(buf, off),
        Y = BitConverter.ToSingle(buf, off + 4),
        Z = BitConverter.ToSingle(buf, off + 8),
        W = BitConverter.ToSingle(buf, off + 12),
    };
}
