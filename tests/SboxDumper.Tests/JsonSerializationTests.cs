using System.Text.Json;
using System.Text.Json.Serialization;
using SboxDumper.Models;
using Xunit;

namespace SboxDumper.Tests;

public class JsonSerializationTests
{
    // Mirrors the serializer options used in Program.Main.
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void DumpResult_SerializesSnakeCaseAndOmitsNulls()
    {
        var dump = new DumpResult
        {
            Process = "sbox",
            Pid = 1234,
            DumpedAt = "2026-08-01T00:00:00.0000000Z",
            ClrVersion = "9.0.0",
        };
        dump.Offsets["DxrpPlayer"] = new OffsetTableEntry
        {
            TypeName = "Dxura.RP.Game.Player",
            MethodTable = "0x7ff8a1234560",
            BaseSize = 1440,
            Fields =
            [
                new OffsetField
                {
                    Name = "<SteamId>k__BackingField",
                    CleanName = "SteamId",
                    Offset = 1128,
                    OffsetHex = "0x468",
                    Type = "System.Int64",
                    Size = 8,
                },
            ],
        };

        var json = JsonSerializer.Serialize(dump, Options);

        Assert.Contains("\"type_name\": \"Dxura.RP.Game.Player\"", json);
        Assert.Contains("\"method_table\": \"0x7ff8a1234560\"", json);
        Assert.Contains("\"clean_name\": \"SteamId\"", json);
        Assert.Contains("\"offset_hex\": \"0x468\"", json);
        // Nullable properties that are null (Parent, Job, Health, ...) must be omitted.
        Assert.DoesNotContain("\"parent\"", json);
    }

    [Fact]
    public void OffsetTable_SerializesOffsetHexAndSize()
    {
        var entry = new OffsetTableEntry
        {
            TypeName = "Dxura.RP.Game.Player",
            Fields =
            [
                new OffsetField { Name = "x", Offset = 1128, OffsetHex = "0x468", Type = "System.Int64", Size = 8 },
            ],
        };

        var json = JsonSerializer.Serialize(entry, Options);

        Assert.Contains("\"offset_hex\": \"0x468\"", json);
        Assert.Contains("\"size\": 8", json);
    }
}
