using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Diagnostics.Runtime;

namespace SboxDumper;

static class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════╗");
        Console.WriteLine("║  DXRP / s&box Offset Dumper v2.0     ║");
        Console.WriteLine("╚══════════════════════════════════════╝\n");

        var processName = args.Length > 0 ? args[0] : "sbox";

        var procs = Process.GetProcessesByName(processName);
        if (procs.Length == 0)
        {
            Console.Error.WriteLine($"[!] '{processName}' not found.");
            return 1;
        }

        var proc = procs[0];
        Console.WriteLine($"[+] Attached: {proc.ProcessName}.exe (PID {proc.Id})\n");

        try
        {
            using var dt = DataTarget.AttachToProcess(proc.Id, suspend: false);
            var clrInfo = dt.ClrVersions.FirstOrDefault();
            if (clrInfo is null)
            {
                Console.Error.WriteLine("[!] No CLR runtime found.");
                return 1;
            }

            using var runtime = clrInfo.CreateRuntime();
            Console.WriteLine($"[+] CLR Version: {runtime.ClrInfo.Version}\n");

            var dump = new DumpResult
            {
                Process = proc.ProcessName,
                Pid = proc.Id,
                DumpedAt = DateTime.UtcNow.ToString("o"),
                ClrVersion = runtime.ClrInfo.Version.ToString(),
            };

            DumpModules(dt, dump);
            DumpOffsetTable(runtime, dump);
            DumpPlayers(runtime, dump);

            var jsonOpts = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };

            Directory.CreateDirectory("output");

            var json = JsonSerializer.Serialize(dump, jsonOpts);
            File.WriteAllText("output/sbox_dump.json", json);

            var offsetJson = JsonSerializer.Serialize(dump.Offsets, jsonOpts);
            File.WriteAllText("output/offsets.json", offsetJson);

            Console.WriteLine($"\n[+] output/sbox_dump.json    ({json.Length:N0} bytes)");
            Console.WriteLine($"[+] output/offsets.json      ({offsetJson.Length:N0} bytes)");
            Console.WriteLine("[+] Done.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] {ex.GetType().Name}: {ex.Message}");
            if (ex.Message.Contains("Access") || ex.Message.Contains("denied"))
                Console.Error.WriteLine("    Try running as Administrator.");
            return 1;
        }
    }

    // ─── Module dump ────────────────────────────────────────────────
    static void DumpModules(DataTarget dt, DumpResult dump)
    {
        Console.WriteLine("── Modules ──────────────────────────────────────────");

        foreach (var m in dt.EnumerateModules())
        {
            var name = Path.GetFileName(m.FileName ?? "unknown");
            var lo = name.ToLowerInvariant();

            bool keep = lo.Contains("engine2") || lo.Contains("sandbox") ||
                        lo.Contains("sbox") || lo.Contains("coreclr") ||
                        lo.Contains("tier0") || lo.Contains("networksystem") ||
                        lo.Contains("scenesystem") || lo.Contains("dxura");

            if (!keep) continue;

            dump.Modules[name] = new ModuleEntry
            {
                Base = $"0x{m.ImageBase:X}",
                Size = (uint)m.ImageSize,
            };
            Console.WriteLine($"  {name,-45} 0x{m.ImageBase:X16}  {m.ImageSize,10:N0}b");
        }
        Console.WriteLine();
    }

    // ─── Build offset table for DMA ─────────────────────────────────
    static void DumpOffsetTable(ClrRuntime runtime, DumpResult dump)
    {
        Console.WriteLine("── Offset Table ─────────────────────────────────────");

        var heap = runtime.Heap;
        var typeCache = new Dictionary<string, ClrType>();

        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type;
            if (t?.Name is null) continue;
            typeCache.TryAdd(t.Name, t);
        }

        var targets = new (string TypeName, string Category, string[]? FieldFilter)[]
        {
            ("Sandbox.GameObject", "GameObject", null),
            ("Sandbox.GameTransform", "GameTransform", null),
            ("Sandbox.PlayerController", "PlayerController", null),
            ("Sandbox.CharacterController", "CharacterController", null),
            ("Sandbox.Dresser", "Dresser", null),
            ("Dxura.RP.Game.Player", "DxrpPlayer", null),
            ("Dxura.RP.Game.HealthComponent", "HealthComponent", null),
            ("Dxura.RP.Game.ArmorComponent", "ArmorComponent", null),
            ("Dxura.RP.Game.PlayerVoiceComponent", "PlayerVoiceComponent", null),
            ("Dxura.RP.Game.Equipment", "Equipment", null),
            ("Dxura.RP.Game.JobResource", "JobResource", null),
            ("Dxura.RP.Game.AnimationHelper", "AnimationHelper", null),
            ("Dxura.RP.Game.TagBinder", "TagBinder", null),
            ("Dxura.RP.Game.Door", "Door", null),
            ("Dxura.RP.Game.Entities.PrinterEntity", "PrinterEntity", null),
        };

        foreach (var (typeName, category, _) in targets)
        {
            if (!typeCache.TryGetValue(typeName, out var type)) continue;

            var offsets = new OffsetTableEntry
            {
                TypeName = typeName,
                MethodTable = $"0x{type.MethodTable:X}",
                BaseSize = type.StaticSize,
                Parent = type.BaseType?.Name,
            };

            // Collect fields from this type and its bases (stop at Component/Object)
            var fields = CollectAllFields(type);

            foreach (var f in fields)
            {
                var name = f.Name ?? "?";
                var cleanName = CleanFieldName(name);

                offsets.Fields.Add(new OffsetField
                {
                    Name = name,
                    CleanName = cleanName,
                    Offset = f.Offset,
                    OffsetHex = $"0x{f.Offset:X}",
                    Type = f.Type?.Name ?? "?",
                    Size = f.Size,
                    IsValueType = f.IsValueType,
                    IsSync = name.Contains("_SyncAttribute__"),
                });
            }

            dump.Offsets[category] = offsets;
            Console.WriteLine($"  {category,-25} ({typeName}) — {offsets.Fields.Count} fields");
        }

        // Build the condensed DMA offset map
        BuildDmaOffsetMap(dump);

        Console.WriteLine();
    }

    static void BuildDmaOffsetMap(DumpResult dump)
    {
        Console.WriteLine("\n── DMA Offset Map ───────────────────────────────────");

        // GameObject
        AddDmaOffset(dump, "GameObject", "_name", "Name");
        AddDmaOffset(dump, "GameObject", "_gameTransform", "GameTransform");
        AddDmaOffset(dump, "GameObject", "_enabled", "Enabled");
        AddDmaOffset(dump, "GameObject", "_components", "Components");
        AddDmaOffset(dump, "GameObject", "<Network>k__BackingField", "Network");
        AddDmaOffset(dump, "GameObject", "<Scene>k__BackingField", "Scene");
        AddDmaOffset(dump, "GameObject", "_parent", "Parent");

        // GameTransform -> Position/Rotation
        AddDmaOffset(dump, "GameTransform", "_targetLocal", "TargetLocal");
        AddDmaOffset(dump, "GameTransform", "_interpolatedLocal", "InterpolatedLocal");

        // PlayerController
        AddDmaOffset(dump, "PlayerController", "<WishVelocity>k__BackingField", "WishVelocity");

        // CharacterController
        AddDmaOffset(dump, "CharacterController", "<Velocity>k__BackingField", "Velocity");
        AddDmaOffset(dump, "CharacterController", "<IsOnGround>k__BackingField", "IsOnGround");

        // DXRP Player
        AddDmaOffset(dump, "DxrpPlayer", "<Job>k__BackingField", "Job");
        AddDmaOffset(dump, "DxrpPlayer", "<CustomJob>k__BackingField", "CustomJob");
        AddDmaOffset(dump, "DxrpPlayer", "<SteamId>k__BackingField", "SteamId");
        AddDmaOffset(dump, "DxrpPlayer", "<SteamName>k__BackingField", "SteamName");
        AddDmaOffset(dump, "DxrpPlayer", "<RpName>k__BackingField", "RpName");
        AddDmaOffset(dump, "DxrpPlayer", "<PreferredTitle>k__BackingField", "PreferredTitle");
        AddDmaOffset(dump, "DxrpPlayer", "<WalletBalance>k__BackingField", "WalletBalance");
        AddDmaOffset(dump, "DxrpPlayer", "<BankBalance>k__BackingField", "BankBalance");
        AddDmaOffset(dump, "DxrpPlayer", "<Level>k__BackingField", "Level");
        AddDmaOffset(dump, "DxrpPlayer", "<Kills>k__BackingField", "Kills");
        AddDmaOffset(dump, "DxrpPlayer", "<Deaths>k__BackingField", "Deaths");
        AddDmaOffset(dump, "DxrpPlayer", "<PlayTime>k__BackingField", "PlayTime");
        AddDmaOffset(dump, "DxrpPlayer", "<Restricted>k__BackingField", "Restricted");
        AddDmaOffset(dump, "DxrpPlayer", "<IsTyping>k__BackingField", "IsTyping");
        AddDmaOffset(dump, "DxrpPlayer", "<IsThirdPersonPreferred>k__BackingField", "IsThirdPerson");
        AddDmaOffset(dump, "DxrpPlayer", "<AimRay>k__BackingField", "AimRay");
        AddDmaOffset(dump, "DxrpPlayer", "<Controller>k__BackingField", "Controller");
        AddDmaOffset(dump, "DxrpPlayer", "<HealthComponent>k__BackingField", "HealthComponent");
        AddDmaOffset(dump, "DxrpPlayer", "<ArmorComponent>k__BackingField", "ArmorComponent");
        AddDmaOffset(dump, "DxrpPlayer", "<Renderer>k__BackingField", "Renderer");
        AddDmaOffset(dump, "DxrpPlayer", "<Rigidbody>k__BackingField", "Rigidbody");
        AddDmaOffset(dump, "DxrpPlayer", "<CurrentEquipment>k__BackingField", "CurrentEquipment");
        AddDmaOffset(dump, "DxrpPlayer", "<WeaponGameObject>k__BackingField", "WeaponGameObject");
        AddDmaOffset(dump, "DxrpPlayer", "<Voice>k__BackingField", "Voice");
        AddDmaOffset(dump, "DxrpPlayer", "<FactionId>k__BackingField", "FactionId");
        AddDmaOffset(dump, "DxrpPlayer", "<FactionRoleId>k__BackingField", "FactionRoleId");
        AddDmaOffset(dump, "DxrpPlayer", "<Sit>k__BackingField", "Sit");
        AddDmaOffset(dump, "DxrpPlayer", "<RespawnState>k__BackingField", "RespawnState");
        AddDmaOffset(dump, "DxrpPlayer", "<Spread>k__BackingField", "Spread");
        AddDmaOffset(dump, "DxrpPlayer", "<DamageTakenPosition>k__BackingField", "DamageTakenPosition");
        AddDmaOffset(dump, "DxrpPlayer", "<DamageTakenForce>k__BackingField", "DamageTakenForce");
        AddDmaOffset(dump, "DxrpPlayer", "<AnimationHelper>k__BackingField", "AnimationHelper");
        AddDmaOffset(dump, "DxrpPlayer", "<Statuses>k__BackingField", "Statuses");
        AddDmaOffset(dump, "DxrpPlayer", "<GameObject>k__BackingField", "GameObject");

        // HealthComponent
        AddDmaOffset(dump, "HealthComponent", "<Health>k__BackingField", "Health");
        AddDmaOffset(dump, "HealthComponent", "<MaxHealth>k__BackingField", "MaxHealth");
        AddDmaOffset(dump, "HealthComponent", "<IsDead>k__BackingField", "IsDead");

        // ArmorComponent
        AddDmaOffset(dump, "ArmorComponent", "<Armor>k__BackingField", "Armor");
        AddDmaOffset(dump, "ArmorComponent", "<MaxArmor>k__BackingField", "MaxArmor");
        AddDmaOffset(dump, "ArmorComponent", "<HasHelmet>k__BackingField", "HasHelmet");

        // JobResource
        AddDmaOffset(dump, "JobResource", "<Name>k__BackingField", "Name");
        AddDmaOffset(dump, "JobResource", "<Title>k__BackingField", "Title");
        AddDmaOffset(dump, "JobResource", "<Description>k__BackingField", "Description");
        AddDmaOffset(dump, "JobResource", "<Salary>k__BackingField", "Salary");
        AddDmaOffset(dump, "JobResource", "<MaxSlots>k__BackingField", "MaxSlots");
        AddDmaOffset(dump, "JobResource", "<Category>k__BackingField", "Category");

        // Dresser
        AddDmaOffset(dump, "Dresser", "<ManualHeight>k__BackingField", "ManualHeight");
        AddDmaOffset(dump, "Dresser", "<ManualTint>k__BackingField", "ManualTint");
        AddDmaOffset(dump, "Dresser", "<ManualAge>k__BackingField", "ManualAge");

        foreach (var (cat, entries) in dump.DmaOffsets.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"  [{cat}]");
            foreach (var (name, off) in entries.OrderBy(kv => kv.Value.Offset))
                Console.WriteLine($"    {name,-30} +0x{off.Offset:X3}  ({off.Type}, {off.Size}b)");
        }
    }

    static void AddDmaOffset(DumpResult dump, string category, string fieldName, string cleanName)
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

    // ─── Player extraction ──────────────────────────────────────────
    static void DumpPlayers(ClrRuntime runtime, DumpResult dump)
    {
        Console.WriteLine("\n── Players ──────────────────────────────────────────");

        var heap = runtime.Heap;

        foreach (var obj in heap.EnumerateObjects())
        {
            if (obj.Type?.Name != "Dxura.RP.Game.Player") continue;

            // Skip prefab-cache instances (the "player" template GO, not actual players)
            TryReadField(obj, "<GameObject>k__BackingField", out var goObj);
            if (!goObj.IsValid) continue;

            var goName = TryReadString(goObj, "_name");
            bool startCalled = TryReadBool(obj, "_startCalled");

            // Only dump real spawned players (startCalled=true, has SteamId)
            var steamId = TryReadInt64(obj, "<SteamId>k__BackingField");
            if (steamId == 0 && !startCalled) continue;

            Console.WriteLine($"\n  ── Player: \"{goName}\" ──");

            var player = new PlayerDump
            {
                Address = $"0x{obj.Address:X}",
                GameObjectAddress = $"0x{goObj.Address:X}",
                GameObjectName = goName,
            };

            // Identity
            player.SteamId = steamId;
            player.SteamName = TryReadString(obj, "<SteamName>k__BackingField");
            player.RpName = TryReadString(obj, "<RpName>k__BackingField");
            player.PreferredTitle = TryReadString(obj, "<PreferredTitle>k__BackingField");
            player.CustomJob = TryReadString(obj, "<CustomJob>k__BackingField");

            Console.WriteLine($"    SteamId:    {player.SteamId}");
            Console.WriteLine($"    SteamName:  {player.SteamName}");
            Console.WriteLine($"    RpName:     {player.RpName}");

            // Job — follow JobResource reference
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

            // Health + Armor via component references
            TryReadField(obj, "<HealthComponent>k__BackingField", out var hcObj);
            if (hcObj.IsValid)
            {
                player.Health = ReadHealthComponent(hcObj);
                Console.WriteLine($"    Health:     {player.Health.Current}/{player.Health.Max} (Dead={player.Health.IsDead})");
            }

            TryReadField(obj, "<ArmorComponent>k__BackingField", out var acObj);
            if (acObj.IsValid)
            {
                player.Armor = ReadArmorComponent(acObj);
                Console.WriteLine($"    Armor:      {player.Armor.Current}/{player.Armor.Max} (Helmet={player.Armor.HasHelmet})");
            }

            // Transform — read actual float values
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

            // Controller reference
            TryReadField(obj, "<Controller>k__BackingField", out var ctrlObj);
            if (ctrlObj.IsValid)
                player.ControllerAddress = $"0x{ctrlObj.Address:X}";

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

            // All attached components on this player's GameObject
            player.Components = FindComponentsOnGameObject(runtime, goObj.Address);
            Console.WriteLine($"    Components: {player.Components.Count}");

            dump.Players.Add(player);
        }

        if (dump.Players.Count == 0)
            Console.WriteLine("  [!] No DXRP players found. Make sure you're in a game session.");

        Console.WriteLine();
    }

    // ─── Component readers ──────────────────────────────────────────

    static JobDump ReadJobResource(ClrObject jobObj)
    {
        var job = new JobDump { Address = $"0x{jobObj.Address:X}" };

        // JobResource inherits from GameResource, fields might be named differently
        // Try common patterns for resource name/title
        foreach (var f in jobObj.Type?.Fields ?? [])
        {
            var name = f.Name ?? "";
            if (f.Type?.Name == "System.String" && !name.Contains("SyncAttribute"))
            {
                var val = TryReadString(jobObj, name);
                if (val == null) continue;

                if (name.Contains("Name") || name.Contains("name"))
                    job.Name ??= val;
                else if (name.Contains("Title") || name.Contains("title"))
                    job.Title ??= val;
                else if (name.Contains("Description") || name.Contains("description"))
                    job.Description ??= val;
                else if (name.Contains("Category") || name.Contains("category"))
                    job.Category ??= val;
            }
            else if (name.Contains("Salary") || name.Contains("salary"))
            {
                job.Salary = TryReadInt32Field(jobObj, f);
            }
            else if (name.Contains("MaxSlots") || name.Contains("maxSlots"))
            {
                job.MaxSlots = TryReadInt32Field(jobObj, f);
            }
        }

        // Also try base type (GameResource) fields
        var baseType = jobObj.Type?.BaseType;
        while (baseType != null && baseType.Name != "System.Object")
        {
            foreach (var f in baseType.Fields)
            {
                var name = f.Name ?? "";
                if (f.Type?.Name == "System.String")
                {
                    var val = TryReadStringField(jobObj, f);
                    if (val == null) continue;

                    if (name.Contains("ResourceName") || name.Contains("resourceName"))
                        job.ResourceName ??= val;
                    else if (name.Contains("ResourcePath") || name.Contains("resourcePath"))
                        job.ResourcePath ??= val;
                    else if (job.Name == null && name.Contains("Name"))
                        job.Name = val;
                }
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
            var typeName = f.Type?.Name ?? "";

            if (typeName is "System.Single")
            {
                var val = TryReadFloatField(obj, f);
                if (name.Contains("MaxHealth") || name.Contains("maxHealth"))
                    h.Max = val;
                else if (name.Contains("Health") || name.Contains("health"))
                    h.Current = val;
            }
            else if (typeName is "System.Boolean")
            {
                if (name.Contains("IsDead") || name.Contains("isDead"))
                    h.IsDead = TryReadBoolField(obj, f);
            }
        }

        return h;
    }

    static ArmorDump ReadArmorComponent(ClrObject obj)
    {
        var a = new ArmorDump();

        foreach (var f in CollectAllFields(obj.Type!))
        {
            var name = f.Name ?? "";
            var typeName = f.Type?.Name ?? "";

            if (typeName is "System.Single")
            {
                var val = TryReadFloatField(obj, f);
                if (name.Contains("MaxArmor") || name.Contains("maxArmor"))
                    a.Max = val;
                else if (name.Contains("Armor") || name.Contains("armor"))
                    a.Current = val;
            }
            else if (typeName is "System.Boolean")
            {
                if (name.Contains("HasHelmet") || name.Contains("Helmet") || name.Contains("helmet"))
                    a.HasHelmet = TryReadBoolField(obj, f);
            }
        }

        return a;
    }

    static TransformDump? ReadTransformData(ClrRuntime runtime, ClrObject gtObj)
    {
        // GameTransform._targetLocal is a value-type Transform struct at a known offset
        // From previous dumps: _targetLocal at +0xCC (contains Position+Scale+Rotation = 40 bytes)
        // Layout: Position(Vector3:12b) + Scale(Vector3:12b) + Rotation(Quaternion:16b)

        var targetField = gtObj.Type?.GetFieldByName("_targetLocal");
        if (targetField == null) return null;

        int baseOffset = targetField.Offset;

        // The object header is 8 bytes (MethodTable ptr) on x64
        // For reading fields on the managed heap, the address includes the object header
        var addr = gtObj.Address;

        try
        {
            // Read raw bytes: Position(12) + Scale(12) + Rotation(16) = 40 bytes
            // ClrMD field offsets are relative to the object data start (after MT pointer)
            var rawBytes = new byte[40];
            runtime.DataTarget.DataReader.Read(addr + (ulong)baseOffset + 8, rawBytes);

            var pos = new Vec3
            {
                X = BitConverter.ToSingle(rawBytes, 0),
                Y = BitConverter.ToSingle(rawBytes, 4),
                Z = BitConverter.ToSingle(rawBytes, 8),
            };

            var scale = new Vec3
            {
                X = BitConverter.ToSingle(rawBytes, 12),
                Y = BitConverter.ToSingle(rawBytes, 16),
                Z = BitConverter.ToSingle(rawBytes, 20),
            };

            var rot = new Vec4
            {
                X = BitConverter.ToSingle(rawBytes, 24),
                Y = BitConverter.ToSingle(rawBytes, 28),
                Z = BitConverter.ToSingle(rawBytes, 32),
                W = BitConverter.ToSingle(rawBytes, 36),
            };

            // Sanity check — if all zeros or NaN, try +0 offset (no MT adjustment)
            if (float.IsNaN(pos.X) || (pos.X == 0 && pos.Y == 0 && pos.Z == 0 && scale.X == 0))
            {
                runtime.DataTarget.DataReader.Read(addr + (ulong)baseOffset, rawBytes);

                pos = new Vec3
                {
                    X = BitConverter.ToSingle(rawBytes, 0),
                    Y = BitConverter.ToSingle(rawBytes, 4),
                    Z = BitConverter.ToSingle(rawBytes, 8),
                };
                scale = new Vec3
                {
                    X = BitConverter.ToSingle(rawBytes, 12),
                    Y = BitConverter.ToSingle(rawBytes, 16),
                    Z = BitConverter.ToSingle(rawBytes, 20),
                };
                rot = new Vec4
                {
                    X = BitConverter.ToSingle(rawBytes, 24),
                    Y = BitConverter.ToSingle(rawBytes, 28),
                    Z = BitConverter.ToSingle(rawBytes, 32),
                    W = BitConverter.ToSingle(rawBytes, 36),
                };
            }

            return new TransformDump
            {
                FieldOffset = baseOffset,
                Position = pos,
                Scale = scale,
                Rotation = rot,
            };
        }
        catch
        {
            return null;
        }
    }

    static List<ComponentRef> FindComponentsOnGameObject(ClrRuntime runtime, ulong goAddress)
    {
        var result = new List<ComponentRef>();

        foreach (var obj in runtime.Heap.EnumerateObjects())
        {
            var t = obj.Type;
            if (t?.Name is null) continue;
            if (!IsComponentSubclass(t)) continue;

            TryReadField(obj, "<GameObject>k__BackingField", out var goRef);
            if (!goRef.IsValid || goRef.Address != goAddress) continue;

            result.Add(new ComponentRef
            {
                Address = $"0x{obj.Address:X}",
                Type = t.Name,
            });
        }

        return result;
    }

    // ─── Field helpers ──────────────────────────────────────────────

    static List<ClrInstanceField> CollectAllFields(ClrType type)
    {
        var seen = new HashSet<(string, int)>();
        var fields = new List<ClrInstanceField>();
        var cur = type;

        while (cur != null)
        {
            foreach (var f in cur.Fields)
            {
                var key = (f.Name ?? "", f.Offset);
                if (seen.Add(key))
                    fields.Add(f);
            }

            if (cur.Name is "Sandbox.Component" or "Sandbox.GameResource" or "System.Object")
                break;

            cur = cur.BaseType;
        }

        return fields.OrderBy(f => f.Offset).ToList();
    }

    static string CleanFieldName(string name)
    {
        if (name.StartsWith('<') && name.Contains('>'))
        {
            var end = name.IndexOf('>');
            return name[1..end];
        }
        if (name.StartsWith('_'))
            return name[1..];
        return name;
    }

    static bool IsComponentSubclass(ClrType? t)
    {
        var cur = t?.BaseType;
        int depth = 0;
        while (cur != null && depth < 10)
        {
            if (cur.Name == "Sandbox.Component") return true;
            cur = cur.BaseType;
            depth++;
        }
        return false;
    }

    static bool TryReadField(ClrObject obj, string fieldName, out ClrObject result)
    {
        result = default;
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null || field.IsValueType) return false;
            result = field.ReadObject(obj.Address, interior: false);
            return result.IsValid;
        }
        catch { return false; }
    }

    static string? TryReadString(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) return null;
            var strObj = field.ReadObject(obj.Address, interior: false);
            if (!strObj.IsValid || strObj.Type?.Name != "System.String") return null;
            return strObj.AsString();
        }
        catch { return null; }
    }

    static string? TryReadStringField(ClrObject obj, ClrInstanceField f)
    {
        try
        {
            var strObj = f.ReadObject(obj.Address, interior: false);
            if (!strObj.IsValid || strObj.Type?.Name != "System.String") return null;
            return strObj.AsString();
        }
        catch { return null; }
    }

    static bool TryReadBool(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) return false;
            return field.Read<bool>(obj.Address, interior: false);
        }
        catch { return false; }
    }

    static bool TryReadBoolField(ClrObject obj, ClrInstanceField f)
    {
        try { return f.Read<bool>(obj.Address, interior: false); }
        catch { return false; }
    }

    static int TryReadInt32(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) return 0;
            return field.Read<int>(obj.Address, interior: false);
        }
        catch { return 0; }
    }

    static int TryReadInt32Field(ClrObject obj, ClrInstanceField f)
    {
        try { return f.Read<int>(obj.Address, interior: false); }
        catch { return 0; }
    }

    static uint TryReadUInt32(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) return 0;
            return field.Read<uint>(obj.Address, interior: false);
        }
        catch { return 0; }
    }

    static long TryReadInt64(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) return 0;
            return field.Read<long>(obj.Address, interior: false);
        }
        catch { return 0; }
    }

    static float TryReadFloat(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) return 0;
            return field.Read<float>(obj.Address, interior: false);
        }
        catch { return 0; }
    }

    static float TryReadFloatField(ClrObject obj, ClrInstanceField f)
    {
        try { return f.Read<float>(obj.Address, interior: false); }
        catch { return 0; }
    }

    static string? TryReadGuid(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) return null;

            // Nullable<Guid> is 20 bytes: 1 byte hasValue + padding + 16 byte Guid
            // Read as raw bytes
            var bytes = new byte[20];
            var addr = obj.Address + (ulong)field.Offset + 8; // +8 for object header
            var dt = field.Type?.Heap?.Runtime?.DataTarget;
            if (dt == null) return null;

            dt.DataReader.Read(addr, bytes);

            // First byte is hasValue for Nullable<T>
            if (bytes[0] == 0) return null;

            // Guid starts at offset 4 (after hasValue + 3 padding bytes)
            var guid = new Guid(bytes.AsSpan(4, 16));
            return guid.ToString();
        }
        catch { return null; }
    }
}

// ─── JSON Models ────────────────────────────────────────────────────

class DumpResult
{
    public string Process { get; set; } = "";
    public int Pid { get; set; }
    public string DumpedAt { get; set; } = "";
    public string ClrVersion { get; set; } = "";

    public Dictionary<string, ModuleEntry> Modules { get; set; } = new();
    public Dictionary<string, OffsetTableEntry> Offsets { get; set; } = new();
    public Dictionary<string, Dictionary<string, DmaOffset>> DmaOffsets { get; set; } = new();
    public List<PlayerDump> Players { get; set; } = [];
}

class ModuleEntry
{
    public string Base { get; set; } = "";
    public uint Size { get; set; }
}

class OffsetTableEntry
{
    public string TypeName { get; set; } = "";
    public string MethodTable { get; set; } = "";
    public int BaseSize { get; set; }
    public string? Parent { get; set; }
    public List<OffsetField> Fields { get; set; } = [];
}

class OffsetField
{
    public string Name { get; set; } = "";
    public string? CleanName { get; set; }
    public int Offset { get; set; }
    public string OffsetHex { get; set; } = "";
    public string Type { get; set; } = "";
    public int Size { get; set; }
    public bool IsValueType { get; set; }
    public bool IsSync { get; set; }
}

class DmaOffset
{
    public int Offset { get; set; }
    public string OffsetHex { get; set; } = "";
    public string Type { get; set; } = "";
    public int Size { get; set; }
}

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
