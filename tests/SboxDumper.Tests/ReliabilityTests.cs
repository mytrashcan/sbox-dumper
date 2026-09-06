using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using SboxDumper.Models;
using SboxDumper.Readers;
using SboxDumper.Services;
using Xunit;

namespace SboxDumper.Tests;

// FieldReaders diagnostics belong to one single-threaded dump run.
public class ReliabilityTests
{
    [Fact]
    public async Task ClrMdReadsRealManagedFieldsAndResumesFixture()
    {
        // Launch the apphost built with this test assembly, never an executable
        // selected by DOTNET_HOST_PATH or PATH from the calling environment.
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixture", "SboxDumper.Fixture.exe");
        var start = new ProcessStartInfo(fixture)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        // Use the runtime already hosting these tests, including private SDKs.
        string runtimeRoot = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        foreach (string key in new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)" })
            start.Environment[key] = runtimeRoot;
        using var process = Process.Start(start)!;
        try
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            FieldReaders.ResetWarnings();
            using (var target = DataTarget.AttachToProcess(process.Id, suspend: true))
            using (var runtime = target.ClrVersions.Single().CreateRuntime())
            {
                var sample = runtime.Heap.EnumerateObjects().First(obj => obj.Type?.Name == "SboxDumper.Fixture.Sample");
                Assert.Equal(0, FieldReaders.TryReadInt32(sample, "Zero"));
                Assert.Equal(false, FieldReaders.TryReadBool(sample, "False"));
                Assert.Equal(17, FieldReaders.TryReadInt32(sample, "Inherited"));
                Assert.Null(FieldReaders.TryReadFloat(sample, "Invalid"));
                Assert.Null(FieldReaders.TryReadInt32(sample, "Missing"));
                Assert.Null(FieldReaders.TryReadFloat(sample, "Zero"));
                Assert.Equal(System.Guid.Empty.ToString(), FieldReaders.TryReadGuid(sample, "EmptyGuid"));
                Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", FieldReaders.TryReadGuid(sample, "Guid"));
                var transform = Assert.IsType<TransformDump>(PlayerDumper.ReadTransformData(runtime, sample));
                Assert.Equal(0f, transform.Position.X);
                Assert.Equal(2f, transform.Scale.X);
                Assert.Equal(4f, transform.Scale.Z);
                Assert.Equal(1f, transform.Rotation.W);
            }
            await process.StandardInput.WriteLineAsync("exit");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [Theory]
    [InlineData("--wat")]
    [InlineData("--pid")]
    [InlineData("--pid", "0")]
    [InlineData("--pid", "-1")]
    [InlineData("--pid", "2147483648")]
    [InlineData("--dma-path")]
    [InlineData("--dma-path", "--no-suspend")]
    [InlineData("--dma-path", " ")]
    [InlineData("sbox", "extra")]
    public void CliRejectsInvalidArguments(params string[] args) =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(args, null));

    [Fact]
    public void CliPreservesDefaultsAndAllowsExplicitOverrides()
    {
        var defaults = CliOptions.Parse([], "shared.json");
        Assert.True(defaults.Suspend);
        Assert.Equal("sbox", defaults.ProcessName);
        Assert.Equal("shared.json", defaults.DmaPath);
        var options = CliOptions.Parse(["other", "--pid", "42", "--no-suspend", "--dma-path", "override.json"], "shared.json");
        Assert.False(options.Suspend);
        Assert.Equal(42, options.Pid);
        Assert.Equal("other", options.ProcessName);
        Assert.Equal("override.json", options.DmaPath);
        Assert.True(CliOptions.Parse(["--help"], null).Help);
    }

    [Fact]
    public void FailedReadsAreOmittedWhileRealZeroAndFalseSurvive()
    {
        FieldReaders.ResetWarnings();
        var player = new PlayerDump
        {
            Level = FieldReaders.ReadPrimitive(() => 0, "Level"),
            IsTyping = FieldReaders.ReadPrimitive(() => false, "IsTyping"),
            Kills = FieldReaders.ReadPrimitive<int>(() => throw new IOException("Short read"), "Kills"),
            Spread = FieldReaders.ReadPrimitive(() => float.NaN, "Spread"),
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(player, DumpWriter.JsonOptions));
        Assert.Equal(0, json.RootElement.GetProperty("level").GetInt32());
        Assert.False(json.RootElement.GetProperty("is_typing").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("kills", out _));
        Assert.False(json.RootElement.TryGetProperty("spread", out _));
        Assert.Equal(2, FieldReaders.Warnings.Count);
        Assert.Null(FieldReaders.TryReadInt32(default, "missing"));
        Assert.Contains(FieldReaders.Warnings, warning => warning.Contains("missing"));
    }

    [Fact]
    public void TransformRejectsShortReadsAndEveryNonFiniteComponent()
    {
        var bytes = new byte[40];
        for (int read = 0; read < 40; read++)
            Assert.Null(PlayerDumper.DecodeTransform(bytes, read, 12));
        Assert.Null(PlayerDumper.DecodeTransform(new byte[39], 40, 12));
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            for (int component = 0; component < 10; component++)
            {
                bytes = new byte[40];
                BitConverter.GetBytes(invalid).CopyTo(bytes, component * 4);
                Assert.Null(PlayerDumper.DecodeTransform(bytes, 40, 12));
            }
        }
    }

    [Fact]
    public void TransformPreservesOriginAndDecodesScaleAndRotation()
    {
        var bytes = new byte[40];
        BitConverter.GetBytes(2f).CopyTo(bytes, 12);
        BitConverter.GetBytes(3f).CopyTo(bytes, 16);
        BitConverter.GetBytes(4f).CopyTo(bytes, 20);
        BitConverter.GetBytes(1f).CopyTo(bytes, 36);
        var transform = Assert.IsType<TransformDump>(PlayerDumper.DecodeTransform(bytes, 40, 64));
        Assert.Equal(0f, transform.Position.X);
        Assert.Equal(2f, transform.Scale.X);
        Assert.Equal(3f, transform.Scale.Y);
        Assert.Equal(4f, transform.Scale.Z);
        Assert.Equal(1f, transform.Rotation.W);
        Assert.Equal(64, transform.FieldOffset);
    }

    [Fact]
    public void MissingRequiredOffsetsPreserveEveryExistingOffsetFile()
    {
        using var directory = new TestDirectory();
        var dump = CompleteDump();
        dump.MissingRequiredOffsets.Add("DxrpPlayer.SteamId");
        foreach (string name in new[] { "offsets.json", "dma_offsets.json", "shared.json" })
            File.WriteAllText(Path.Combine(directory.Path, name), "previous");
        Assert.Equal(3, DumpWriter.Write(dump, directory.Path, Path.Combine(directory.Path, "shared.json")));
        foreach (string name in new[] { "offsets.json", "dma_offsets.json", "shared.json" })
            Assert.Equal("previous", File.ReadAllText(Path.Combine(directory.Path, name)));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "sbox_dump.json")));
        Assert.False(json.RootElement.GetProperty("offsets_complete").GetBoolean());
    }

    [Fact]
    public void EmptyHeapReportsMissingTypesAndCannotPublishOffsets()
    {
        var dump = new DumpResult();
        OffsetDumper.Dump(new HeapSnapshot(), dump);
        Assert.False(dump.OffsetsComplete);
        Assert.Equal(20, dump.MissingOffsets.Count);
        Assert.Equal(3, dump.MissingRequiredOffsets.Count);
    }

    [Fact]
    public void CompleteDumpPublishesDespiteAbsentOptionalTypes()
    {
        using var directory = new TestDirectory();
        var dump = CompleteDump();
        dump.MissingOffsets.Add("Dxura.RP.Game.Door");
        var shared = Path.Combine(directory.Path, "nested", "shared.json");
        Assert.Equal(0, DumpWriter.Write(dump, directory.Path, shared));
        Assert.Equal(File.ReadAllText(shared), File.ReadAllText(Path.Combine(directory.Path, "dma_offsets.json")));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void ReadWarningsProducePartialExitStatus()
    {
        using var directory = new TestDirectory();
        var dump = CompleteDump();
        dump.ReadWarnings.Add("Could not read player field");
        Assert.Equal(3, DumpWriter.Write(dump, directory.Path, Path.Combine(directory.Path, "dma_offsets.json")));
    }

    [Fact]
    public void FailedReplacementPreservesOldFileAndCleansTemporaryFile()
    {
        using var directory = new TestDirectory();
        string destination = Path.Combine(directory.Path, "locked.json");
        File.WriteAllText(destination, "previous");
        using (var locked = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => DumpWriter.AtomicWrite(destination, "replacement"));
            Assert.True(error is IOException or UnauthorizedAccessException, $"Unexpected error: {error}");
        }
        Assert.Equal("previous", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void SharedWriteFailureIsReportedAfterLocalFilesAreSaved()
    {
        using var directory = new TestDirectory();
        string blocker = Path.Combine(directory.Path, "blocker");
        File.WriteAllText(blocker, "file instead of directory");
        Assert.ThrowsAny<IOException>(() => DumpWriter.Write(CompleteDump(), directory.Path, Path.Combine(blocker, "shared.json")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "dma_offsets.json")));
    }

    [Fact]
    public void SerializationFailureDoesNotTouchExistingDump()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "sbox_dump.json");
        File.WriteAllText(destination, "previous");
        var dump = CompleteDump();
        dump.Players.Add(new PlayerDump { Spread = float.NaN });
        Assert.Throws<ArgumentException>(() => DumpWriter.Write(dump, directory.Path, Path.Combine(directory.Path, "shared.json")));
        Assert.Equal("previous", File.ReadAllText(destination));
    }

    [Theory]
    [InlineData("sbox_dump.json")]
    [InlineData("offsets.json")]
    public void SharedPathCannotOverwriteOtherOutputFormats(string name)
    {
        using var directory = new TestDirectory();
        Assert.Throws<ArgumentException>(() => DumpWriter.Write(CompleteDump(), directory.Path, Path.Combine(directory.Path, name)));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    static DumpResult CompleteDump()
    {
        var dump = new DumpResult();
        foreach (string category in new[] { "GameObject", "GameTransform", "DxrpPlayer" })
            dump.DmaOffsets[category] = new() { ["test"] = new DmaOffset { Offset = 8, Size = 8 } };
        return dump;
    }

    sealed class TestDirectory : IDisposable
    {
        // Keep creation and recursive cleanup inside our own build output;
        // TMP/TEMP must not redirect filesystem operations elsewhere.
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-output", Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
