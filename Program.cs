using System.Diagnostics;
using System.Reflection;
using Microsoft.Diagnostics.Runtime;
using SboxDumper.Models;
using SboxDumper.Services;

namespace SboxDumper;

static class Program
{
    static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    static int Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args, Environment.GetEnvironmentVariable("SBOX_DUMPER_DMA_PATH"));
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[!] {ex.Message}");
            return 2;
        }
        if (options.Help)
        {
            PrintUsage();
            return 0;
        }
        string processName = options.ProcessName;
        int? pid = options.Pid;
        var title = $"DXRP / s&box Offset Dumper v{Version}";
        var pad = new string(' ', Math.Max(0, 34 - title.Length));
        Console.WriteLine("╔══════════════════════════════════════╗");
        Console.WriteLine($"║  {title}{pad}  ║");
        Console.WriteLine("╚══════════════════════════════════════╝\n");

        // ── Find process ────────────────────────────────
        Process? proc = null;
        Process[]? procs = null;
        try
        {
            if (pid is int targetPid)
            {
                try
                {
                    proc = Process.GetProcessById(targetPid);
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine($"[!] No process with PID {targetPid}.");
                    return 1;
                }
            }
            else
            {
                procs = Process.GetProcessesByName(processName);
                if (procs.Length == 0)
                {
                    Console.Error.WriteLine($"[!] '{processName}' not found.");
                    return 1;
                }

                if (procs.Length > 1)
                {
                    Console.Error.WriteLine($"[!] Multiple '{processName}' processes found ({procs.Length}). Re-run with --pid:");
                    foreach (var pr in procs)
                        Console.Error.WriteLine($"    PID {pr.Id}  (started {pr.StartTime:HH:mm:ss})");
                    return 2;
                }

                proc = procs[0];
            }

            Console.WriteLine($"[+] Target: {proc.ProcessName}.exe (PID {proc.Id})\n");

            return Run(options.DmaPath, options.Suspend, proc);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            // Dispose every handle we opened, on all paths.
            foreach (var p in procs ?? [])
                p.Dispose();
            if (procs is null) proc?.Dispose();
        }
    }

    static int Run(string dmaPath, bool suspend, Process proc)
    {
        try
        {
            SboxDumper.Readers.FieldReaders.ResetWarnings();
            DumpResult dump;
            // ── Attach ClrMD ────────────────────────────
            using (var dt = DataTarget.AttachToProcess(proc.Id, suspend))
            {
                var clrInfo = dt.ClrVersions.FirstOrDefault();
                if (clrInfo is null)
                {
                    Console.Error.WriteLine("[!] No CLR runtime found. Is the game fully loaded?");
                    return 1;
                }

                using var runtime = clrInfo.CreateRuntime();
                if (!runtime.Heap.CanWalkHeap) throw new InvalidOperationException("CLR heap cannot be walked.");
                Console.WriteLine($"[+] CLR Version: {runtime.ClrInfo.Version}\n");

                dump = new DumpResult
                {
                    Process = proc.ProcessName,
                    Pid = proc.Id,
                    TargetSuspended = suspend,
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

                dump.ReadWarnings = SboxDumper.Readers.FieldReaders.Warnings.ToList();
            } // Resume the target before serialization or disk I/O.

            return DumpWriter.Write(dump, "output", dmaPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] {ex.GetType().Name}: {ex.Message}");
            if (ex.Message.Contains("Access") || ex.Message.Contains("denied"))
                Console.Error.WriteLine("    Try running as Administrator.");
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage: sbox-dumper [processName] [options]");
        Console.WriteLine();
        Console.WriteLine("  processName        Process to attach to (default: sbox)");
        Console.WriteLine("  --pid <id>         Attach to a specific PID (use when multiple instances are running)");
        Console.WriteLine("  --no-suspend       Do NOT suspend the target during the dump (default: suspended,");
        Console.WriteLine("                     which avoids torn reads on a live heap)");
        Console.WriteLine("  --dma-path <path>  Where to write dma_offsets.json for the auto-updater");
        Console.WriteLine("                     (default: ../dma_offsets.json, or $SBOX_DUMPER_DMA_PATH)");
        Console.WriteLine("  -h, --help         Show this help");
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
