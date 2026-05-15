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

        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type;
            if (t?.Name is null) continue;
            objCount++;

            // ── 1. Cache unique types ───────────────────
            snap.TypeCache.TryAdd(t.Name, t);

            // ── 2. Collect Player objects ────────────────
            if (t.Name == "Dxura.RP.Game.Player")
            {
                snap.PlayerObjects.Add(obj);
            }

            // ── 3. Build Component→GameObject mapping ───
            if (FieldReaders.IsComponentSubclass(t))
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
                        Type = t.Name,
                    });
                }
            }
        }

        Console.WriteLine($"  Heap: {objCount:N0} objects, {snap.TypeCache.Count} types, " +
                          $"{snap.PlayerObjects.Count} players, {snap.ComponentMap.Count} GameObjects with components");

        return snap;
    }
}
