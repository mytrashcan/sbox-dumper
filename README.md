# sbox-dumper

> DXRP / s&box runtime offset & player dumper built with ClrMD

`sbox-dumper` is a .NET 10 tool that attaches to a running `sbox` process and extracts runtime field offsets, player data, and component references from the managed heap. Designed for DMA development — run it after every game update to regenerate offsets.

---

## Features

- **Single-pass heap walk** — collects type cache, player objects, and component mappings in one enumeration
- Managed object field offsets for 20 target types
- Condensed DMA offset map with clean field names
- Live DXRP player extraction (identity, economy, stats, health, armor, transform)
- Equipment & component references per player
- Engine/runtime module addresses and sizes
- Clean JSON output

---

## Architecture

```
sbox.exe (live process)
    │
    ├── DataTarget.AttachToProcess (ClrMD, suspended by default)
    │
    ▼
┌─────────────────────────────────────────────┐
│  HeapWalker.Walk()  ←  SINGLE PASS          │
│  ├── TypeCache      (typeName → ClrType)    │
│  ├── PlayerObjects  (Dxura.RP.Game.Player)  │
│  └── ComponentMap   (goAddr → components)   │
└──────────────┬──────────────────────────────┘
               │
       ┌───────┴───────┐
       ▼               ▼
  OffsetDumper    PlayerDumper
  (type cache)    (player objs + component map)
       │               │
       ▼               ▼
  offsets.json    sbox_dump.json
```

Previous versions walked the heap multiple times (offset table + player search + per-player component lookup). v3.0 consolidates everything into a single `heap.EnumerateObjects()` call.

---

## Project Structure

```
sbox-dumper/
├── Program.cs              CLI entry point + orchestration
├── Services/
│   ├── HeapWalker.cs       Single-pass heap enumeration
│   ├── OffsetDumper.cs     Offset table + DMA map generation
│   └── PlayerDumper.cs     Player data extraction
├── Readers/
│   └── FieldReaders.cs     ClrMD field read helpers
└── Models/
    ├── DumpResult.cs       Top-level dump + module models
    ├── OffsetModels.cs     Offset table / DMA offset DTOs
    └── PlayerModels.cs     Player / Job / Health / Armor / Transform DTOs
```

---

## Requirements

- Windows
- .NET 10 Runtime / SDK
- Administrator privileges (for process memory access)
- Running `sbox` game instance (fully loaded into a server)

---

## Installation

```bash
git clone https://github.com/mytrashcan/sbox-dumper.git
cd sbox-dumper
dotnet restore
dotnet build -c Release
```

---

## Usage

Default target (`sbox`):

```bash
dotnet run
```

Custom process name:

```bash
dotnet run -- someprocess
```

Or run the compiled executable directly:

```bash
./bin/Release/net10.0/sbox-dumper.exe
```

### Options

| Option | Description |
|---|---|
| `--pid <id>` | Attach to a specific PID. Required when multiple `sbox` instances are running. |
| `--no-suspend` | Read a running target without suspending it; heap changes can produce inconsistent reads. |
| `--dma-path <path>` | Where to write `dma_offsets.json` for the auto-updater (default: `../dma_offsets.json`, or `$SBOX_DUMPER_DMA_PATH`). |
| `-h`, `--help` | Show usage. |

```bash
sbox-dumper --pid 12345
sbox-dumper sbox --dma-path ../shared/dma_offsets.json
```

---

## Output

The target is suspended by default while managed memory is read, then resumed
before JSON serialization and file I/O. `--no-suspend` opts into a live,
potentially inconsistent read. Unknown options, nonpositive PIDs, and extra
process names are rejected with exit code 2.

The full dump uses `schema_version: 2` and includes `target_suspended`,
`read_warnings`, `missing_offsets`, `missing_required_offsets`, and
`offsets_complete`. Failed primitive reads are omitted instead of being emitted
as zero or false; consumers must tolerate missing numeric and Boolean properties.
Actual zero and false values remain present. Non-finite floats and incomplete
Transform reads are omitted and reported in `read_warnings`. Transform decoding
supports the existing 40-byte position/scale/rotation layout and does not guess
alternate field addresses.

All configured mappings in `GameObject`, `GameTransform`, and `DxrpPlayer` are
required for publishing offset files. Missing optional types or mappings remain
listed in `missing_offsets` but do not prevent publication. If required offsets
are missing, only the diagnostic `sbox_dump.json` is updated; existing
`offsets.json` and both DMA files are preserved. When no previous offset files
exist, none are created for an incomplete result.

Each output is written to a temporary file in the destination directory and
replaced by a rename. The files are individually atomic, not a multi-file
transaction. A shared-path write failure is an error even if local files were
saved. Parent directories are created automatically.

Exit codes: **0** = published without read warnings; **1** = capture or write
failure; **2** = invalid arguments or ambiguous process selection; **3** = missing
required offsets or read warnings (inspect the full dump). Read warnings alone
do not prevent publication of otherwise complete offsets.

The tool creates an `output/` directory containing:

| File | Description |
|---|---|
| `sbox_dump.json` | Full dump: modules, offsets, DMA map, player data |
| `offsets.json` | Offset tables only (all 20 target types with fields) |
| `dma_offsets.json` | Condensed DMA map for the external auto-updater (also copied to `../dma_offsets.json` by default) |

### offsets.json structure

```json
{
  "DxrpPlayer": {
    "type_name": "Dxura.RP.Game.Player",
    "method_table": "0x7FF8A1234560",
    "base_size": 1440,
    "fields": [
      {
        "name": "<SteamId>k__BackingField",
        "clean_name": "SteamId",
        "offset": 1128,
        "offset_hex": "0x468",
        "type": "System.Int64",
        "size": 8
      }
    ]
  }
}
```

### DMA offset map (inside sbox_dump.json)

```json
{
  "dma_offsets": {
    "DxrpPlayer": {
      "SteamId":    { "offset": 1128, "offset_hex": "0x468", "type": "System.Int64", "size": 8 },
      "Health":     { "offset": 504,  "offset_hex": "0x1F8", "type": "Dxura.RP.Game.HealthComponent", "size": 8 },
      "AimRay":     { "offset": 1264, "offset_hex": "0x4F0", "type": "Sandbox.Ray", "size": 24 }
    }
  }
}
```

---

## Game Update Workflow

When DXRP updates and offsets change:

1. Join a game server (need live player objects on the heap)
2. Run `sbox-dumper` → regenerates `offsets.json`
3. Compare new offsets with your `Offsets.cs` in the ESP project
4. Update changed values, rebuild ESP

Field offsets shift when the game adds/removes/reorders properties. The dumper discovers them at runtime via ClrMD regardless of game version.

---

## Target Types

| Category | Type | Purpose |
|---|---|---|
| GameObject | `Sandbox.GameObject` | Scene entity container |
| GameTransform | `Sandbox.GameTransform` | Position / rotation / scale |
| DxrpPlayer | `Dxura.RP.Game.Player` | Main player class (identity, stats, components) |
| HealthComponent | `Dxura.RP.Game.HealthComponent` | HP / max HP / death state |
| ArmorComponent | `Dxura.RP.Game.ArmorComponent` | Armor value / helmet |
| JobResource | `Dxura.RP.Game.JobResource` | Job name, salary, category |
| PlayerController | `Sandbox.PlayerController` | Movement (wish velocity) |
| Equipment | `Dxura.RP.Game.Equipment` | Held item |
| Dresser | `Sandbox.Dresser` | Player appearance |
| Door | `Dxura.RP.Game.Door` | Door entities |
| PrinterEntity | `Dxura.RP.Game.Entities.PrinterEntity` | Money printers |

---

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `'sbox' not found` | Game not running | Launch s&box first |
| `No CLR runtime found` | Game still loading | Wait until fully loaded into a server |
| `Access denied` | Insufficient privileges | Run as Administrator |
| `No DXRP players found` | Not in a game session | Join a DXRP server and wait for players to spawn |

---

## Tech Stack

- C# / .NET 10
- [ClrMD](https://github.com/microsoft/clrmd) (`Microsoft.Diagnostics.Runtime`) — managed heap inspection

---

## Related

- [sbox-external](https://github.com/mytrashcan/sbox-external) — ESP overlay that consumes the offsets from this dumper

---

## Disclaimer

This project is for educational and research purposes only.
Use responsibly and comply with all applicable game/server rules.

---

## License

MIT License
