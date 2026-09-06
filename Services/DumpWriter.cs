using System.Text.Json;
using System.Text.Json.Serialization;
using SboxDumper.Models;

namespace SboxDumper.Services;

internal static class DumpWriter
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Each file is replaced atomically; the group of files is not a transaction.
    public static int Write(DumpResult dump, string outputDirectory, string dmaPath)
    {
        var dumpPath = Path.GetFullPath(Path.Combine(outputDirectory, "sbox_dump.json"));
        var offsetsPath = Path.GetFullPath(Path.Combine(outputDirectory, "offsets.json"));
        var localDmaPath = Path.GetFullPath(Path.Combine(outputDirectory, "dma_offsets.json"));
        var sharedPath = Path.GetFullPath(dmaPath);
        if (string.Equals(sharedPath, dumpPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sharedPath, offsetsPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--dma-path must not overwrite the full dump or offset table.");

        // Serialize before touching any existing files.
        string json = JsonSerializer.Serialize(dump, JsonOptions);
        string offsets = JsonSerializer.Serialize(dump.Offsets, JsonOptions);
        string dma = JsonSerializer.Serialize(dump.DmaOffsets, JsonOptions);
        AtomicWrite(dumpPath, json);
        if (!dump.OffsetsComplete)
        {
            Console.Error.WriteLine("[!] Incomplete offsets; diagnostic dump saved, existing offset files preserved.");
            return 3;
        }

        AtomicWrite(offsetsPath, offsets);
        AtomicWrite(localDmaPath, dma);
        if (!string.Equals(sharedPath, localDmaPath, StringComparison.OrdinalIgnoreCase))
            AtomicWrite(sharedPath, dma);
        Console.WriteLine("[+] Dump and offset files saved.");
        return dump.ReadWarnings.Count == 0 ? 0 : 3;
    }

    internal static void AtomicWrite(string path, string contents)
    {
        path = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
