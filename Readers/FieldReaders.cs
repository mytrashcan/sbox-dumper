using Microsoft.Diagnostics.Runtime;

namespace SboxDumper.Readers;

/// Thin wrappers around ClrMD field reads.
/// Every method swallows exceptions — partial data beats a crash.
static class FieldReaders
{
    // ── Object reference fields ─────────────────────────

    public static bool TryReadField(ClrObject obj, string fieldName, out ClrObject result)
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

    // ── String ──────────────────────────────────────────

    public static string? TryReadString(ClrObject obj, string fieldName)
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

    public static string? TryReadStringField(ClrObject obj, ClrInstanceField f)
    {
        try
        {
            var strObj = f.ReadObject(obj.Address, interior: false);
            if (!strObj.IsValid || strObj.Type?.Name != "System.String") return null;
            return strObj.AsString();
        }
        catch { return null; }
    }

    // ── Primitives ──────────────────────────────────────

    public static bool TryReadBool(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            return field != null && field.Read<bool>(obj.Address, interior: false);
        }
        catch { return false; }
    }

    public static bool TryReadBoolField(ClrObject obj, ClrInstanceField f)
    {
        try { return f.Read<bool>(obj.Address, interior: false); }
        catch { return false; }
    }

    public static int TryReadInt32(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            return field?.Read<int>(obj.Address, interior: false) ?? 0;
        }
        catch { return 0; }
    }

    public static int TryReadInt32Field(ClrObject obj, ClrInstanceField f)
    {
        try { return f.Read<int>(obj.Address, interior: false); }
        catch { return 0; }
    }

    public static uint TryReadUInt32(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            return field?.Read<uint>(obj.Address, interior: false) ?? 0;
        }
        catch { return 0; }
    }

    public static long TryReadInt64(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            return field?.Read<long>(obj.Address, interior: false) ?? 0;
        }
        catch { return 0; }
    }

    public static float TryReadFloat(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            return field?.Read<float>(obj.Address, interior: false) ?? 0;
        }
        catch { return 0; }
    }

    public static float TryReadFloatField(ClrObject obj, ClrInstanceField f)
    {
        try { return f.Read<float>(obj.Address, interior: false); }
        catch { return 0; }
    }

    // ── Nullable<Guid> ──────────────────────────────────

    public static string? TryReadGuid(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) return null;

            var bytes = new byte[20];
            var addr = obj.Address + (ulong)field.Offset + 8;
            var dt = field.Type?.Heap?.Runtime?.DataTarget;
            if (dt == null) return null;

            dt.DataReader.Read(addr, bytes);
            if (bytes[0] == 0) return null;

            var guid = new Guid(bytes.AsSpan(4, 16));
            return guid.ToString();
        }
        catch { return null; }
    }

    // ── Type hierarchy helpers ──────────────────────────

    public static List<ClrInstanceField> CollectAllFields(ClrType type)
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

    public static string CleanFieldName(string name)
    {
        if (name.StartsWith('<') && name.Contains('>'))
        {
            var end = name.IndexOf('>');
            return name[1..end];
        }
        return name.StartsWith('_') ? name[1..] : name;
    }

    public static bool IsComponentSubclass(ClrType? t)
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
}
