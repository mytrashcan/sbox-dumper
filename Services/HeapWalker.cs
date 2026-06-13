using Microsoft.Diagnostics.Runtime;
using SboxDumper.Models;
using SboxDumper.Readers;

namespace SboxDumper.Services;

/// Single-pass heap walk result.
/// Everything we need is collected in ONE enumeration.
class HeapSnapshot
{
    /// ClrType cache: typeName → ClrType (for offset dumping)
    public Dictionary<string, ClrType> TypeCache { get; } = new();

    /// Player object addresses (Dxura.RP.Game.Player instances)
    public List<ClrObject> PlayerObjects { get; } = [];

    /// Component mapping: gameObjectAddress → list of (address, typeName)
    /// Pre-built so PlayerDumper doesn't need a second heap walk.
    public Dictionary<ulong, List<ComponentRef>> ComponentMap { get; } = new();
}

static class HeapWalker
{
    /// Walk the managed heap exactly ONCE and collect everything.
    public static HeapSnapshot Walk(ClrHeap heap)
    {
        var snap = new HeapSnapshot();
        int objCount = 0;

        // MethodTable → per-type classification, computed once per unique type.
        // Without this, the base-type chain walk and name comparisons run for
        // every object on the heap (millions) instead of every type (thousands).
        var classified = new Dictionary<ulong, (bool IsPlayer, bool IsComponent, string Name)>();

        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type;
            if (t is null) continue;

            if (!classified.TryGetValue(t.MethodTable, out var info))
            {
                var name = t.Name;
                if (name is null)
                {
                    classified[t.MethodTable] = (false, false, "");
                    continue;
                }

                snap.TypeCache.TryAdd(name, t);
                info = (name == "Dxura.RP.Game.Player", FieldReaders.IsComponentSubclass(t), name);
                classified[t.MethodTable] = info;
            }
            else if (info.Name.Length == 0)
            {
                continue;
            }

            objCount++;

            // ── Collect Player objects ──────────────────
            if (info.IsPlayer)
            {
                snap.PlayerObjects.Add(obj);
            }

            // ── Build Component→GameObject mapping ──────
            if (info.IsComponent)
            {
                if (FieldReaders.TryReadField(obj, "<GameObject>k__BackingField", out var goRef) && goRef.IsValid)
                {
                    if (!snap.ComponentMap.TryGetValue(goRef.Address, out var list))
                    {
                        list = [];
                        snap.ComponentMap[goRef.Address] = list;
                    }
                    list.Add(new ComponentRef
                    {
                        Address = $"0x{obj.Address:X}",
                        Type = info.Name,
                    });
                }
            }
        }

        Console.WriteLine($"  Heap: {objCount:N0} objects, {snap.TypeCache.Count} types, " +
                          $"{snap.PlayerObjects.Count} players, {snap.ComponentMap.Count} GameObjects with components");

        return snap;
    }
}
