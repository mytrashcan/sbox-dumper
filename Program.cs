using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Diagnostics.Runtime;
using SboxDumper.Models;
using SboxDumper.Services;

namespace SboxDumper;

static class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════╗");
        Console.WriteLine("║  DXRP / s&box Offset Dumper v3.0     ║");
        Console.WriteLine("╚══════════════════════════════════════╝\n");

        var processName = args.Length > 0 ? args[0] : "sbox";

        // ── Find process ────────────────────────────────
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
            // ── Attach ClrMD ────────────────────────────
            using var dt = DataTarget.AttachToProcess(proc.Id, suspend: false);
            var clrInfo = dt.ClrVersions.FirstOrDefault();
            if (clrInfo is null)
            {
                Console.Error.WriteLine("[!] No CLR runtime found. Is the game fully loaded?");
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

            // ── Modules ─────────────────────────────────
            DumpModules(dt, dump);

            // ── Single-pass heap walk ───────────────────
            Console.WriteLine("── Heap Walk (single pass) ──────────────────────────");
            var sw = Stopwatch.StartNew();
            var snapshot = HeapWalker.Walk(runtime.Heap);
            sw.Stop();
            Console.WriteLine($"  Completed in {sw.ElapsedMilliseconds}ms\n");

            // ── Offsets (from type cache, no heap walk) ─
            OffsetDumper.Dump(snapshot, dump);

            // ── Players (from collected objects, no heap walk) ─
            PlayerDumper.Dump(runtime, snapshot, dump);

            // ── Write JSON ──────────────────────────────
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

}
