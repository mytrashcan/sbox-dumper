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

            // Raw read for Nullable<Guid>: hasValue(1) + pad(3) + Guid(16).
            // ClrMD field offsets are relative to the object DATA start, so the
            // +8 is the object's MethodTable pointer. (ClrMD has no typed
            // Nullable<T> read, so we validate the layout explicitly below
            // instead of silently emitting garbage GUIDs.)
            var bytes = new byte[20];
            var addr = obj.Address + (ulong)field.Offset + 8;
            var dt = field.Type?.Heap?.Runtime?.DataTarget;
            if (dt == null) return null;

            int read = dt.DataReader.Read(addr, bytes);
            if (read < bytes.Length)
                return WarnOnce("guid-short-read", $"[!] {fieldName}: short read ({read}/{bytes.Length} bytes); skipping GUID.");

            byte hasValue = bytes[0];
            if (hasValue == 0) return null;
            if (hasValue != 1)
                return WarnOnce("guid-bad-hasvalue", $"[!] {fieldName}: Nullable<Guid>.hasValue = 0x{hasValue:X2} (expected 0/1); memory likely torn or offset stale; skipping GUID.");

            var guidBytes = bytes.AsSpan(4, 16);
            bool allZero = true, allFF = true;
            foreach (var b in guidBytes)
            {
                if (b != 0x00) allZero = false;
                if (b != 0xFF) allFF = false;
            }
            if (allZero || allFF)
                return WarnOnce("guid-degenerate", $"[!] {fieldName}: GUID bytes are all-zero/all-FF with hasValue=1; memory likely torn or offset stale; treating as unset.");

            return new Guid(guidBytes).ToString();
        }
        catch (Exception ex)
        {
            WarnOnce("guid-exception", $"[!] TryReadGuid({fieldName}): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    static readonly HashSet<string> _warned = [];

    /// Logs a warning once per failure class so repeated per-player errors don't spam output.
    internal static string? WarnOnce(string failureClass, string message)
    {
        if (_warned.Add(failureClass))
            Console.Error.WriteLine(message);
        return null;
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
