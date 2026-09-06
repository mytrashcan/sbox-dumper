using Microsoft.Diagnostics.Runtime;

namespace SboxDumper.Readers;

/// Thin wrappers around ClrMD field reads.
/// Failed primitive reads are null and recorded in the dump diagnostics.
static class FieldReaders
{
    // ── Object reference fields ─────────────────────────

    public static bool TryReadField(ClrObject obj, string fieldName, out ClrObject result)
    {
        result = default;
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) throw new MissingFieldException(fieldName);
            if (field.IsValueType) throw new InvalidDataException($"{fieldName} is not an object reference.");
            result = field.ReadObject(obj.Address, interior: false);
            return result.IsValid;
        }
        catch (Exception ex)
        {
            WarnOnce(fieldName, $"[!] {fieldName}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // ── String ──────────────────────────────────────────

    public static string? TryReadString(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) throw new MissingFieldException(fieldName);
            var strObj = field.ReadObject(obj.Address, interior: false);
            if (!strObj.IsValid || strObj.Type?.Name != "System.String") return null;
            return strObj.AsString();
        }
        catch (Exception ex)
        {
            return WarnOnce(fieldName, $"[!] {fieldName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static string? TryReadStringField(ClrObject obj, ClrInstanceField f)
    {
        try
        {
            var strObj = f.ReadObject(obj.Address, interior: false);
            if (!strObj.IsValid || strObj.Type?.Name != "System.String") return null;
            return strObj.AsString();
        }
        catch (Exception ex)
        {
            return WarnOnce(f.Name ?? "string-read", $"[!] {f.Name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── Primitives ──────────────────────────────────────

    // Unknown values stay null rather than masquerading as zero or false.
    internal static T? ReadPrimitive<T>(Func<T> read, string context) where T : unmanaged
    {
        try
        {
            T value = read();
            if (value is float number && !float.IsFinite(number))
                throw new InvalidDataException("Non-finite floating-point value.");
            return value;
        }
        catch (Exception ex)
        {
            WarnOnce(context, $"[!] {context}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    static T? ReadPrimitive<T>(ClrObject obj, string name) where T : unmanaged =>
        ReadPrimitive(() => ReadPrimitiveValue<T>(obj, obj.Type?.GetFieldByName(name), name), name);

    static T? ReadPrimitive<T>(ClrObject obj, ClrInstanceField? field, string name) where T : unmanaged =>
        ReadPrimitive(() => ReadPrimitiveValue<T>(obj, field, name), name);

    static T ReadPrimitiveValue<T>(ClrObject obj, ClrInstanceField? field, string name) where T : unmanaged
    {
        if (field is null) throw new MissingFieldException(name);
        if (field.Type?.Name != typeof(T).FullName)
            throw new InvalidDataException($"Expected {typeof(T).FullName}, got {field.Type?.Name}.");
        return field.Read<T>(obj.Address, interior: false);
    }

    public static bool? TryReadBool(ClrObject obj, string name) => ReadPrimitive<bool>(obj, name);
    public static bool? TryReadBoolField(ClrObject obj, ClrInstanceField f) => ReadPrimitive<bool>(obj, f, f.Name ?? "?");
    public static int? TryReadInt32(ClrObject obj, string name) => ReadPrimitive<int>(obj, name);
    public static int? TryReadInt32Field(ClrObject obj, ClrInstanceField f) => ReadPrimitive<int>(obj, f, f.Name ?? "?");
    public static uint? TryReadUInt32(ClrObject obj, string name) => ReadPrimitive<uint>(obj, name);
    public static long? TryReadInt64(ClrObject obj, string name) => ReadPrimitive<long>(obj, name);
    public static float? TryReadFloat(ClrObject obj, string name) => ReadPrimitive<float>(obj, name);
    public static float? TryReadFloatField(ClrObject obj, ClrInstanceField f) => ReadPrimitive<float>(obj, f, f.Name ?? "?");
    // ── Nullable<Guid> ──────────────────────────────────

    public static string? TryReadGuid(ClrObject obj, string fieldName)
    {
        try
        {
            var field = obj.Type?.GetFieldByName(fieldName);
            if (field is null) throw new MissingFieldException(fieldName);

            // Raw read for Nullable<Guid>: hasValue(1) + pad(3) + Guid(16).
            // Let ClrMD calculate the field address including the object header.
            if (!field.IsValueType || field.Size != 20 ||
                field.Type?.Name != "System.Nullable<System.Guid>")
                throw new InvalidDataException("Unsupported Nullable<Guid> layout.");
            var bytes = new byte[20];
            var addr = field.GetAddress(obj.Address, interior: false);
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
            return new Guid(guidBytes).ToString();
        }
        catch (Exception ex)
        {
            WarnOnce("guid-exception", $"[!] TryReadGuid({fieldName}): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    static readonly HashSet<string> _warned = [];
    static readonly List<string> _warnings = [];
    public static IReadOnlyList<string> Warnings => _warnings;
    public static void ResetWarnings()
    {
        _warned.Clear();
        _warnings.Clear();
    }

    /// Logs a warning once per failure class so repeated per-player errors don't spam output.
    internal static string? WarnOnce(string failureClass, string message)
    {
        if (_warned.Add(failureClass))
        {
            _warnings.Add(message);
            Console.Error.WriteLine(message);
        }
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
