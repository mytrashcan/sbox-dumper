# sbox-dumper

> DXRP / s&box runtime offset & player dumper built with ClrMD

`sbox-dumper` is a .NET 8 tool that attaches to a running `sbox` process and extracts runtime field offsets, player data, and component references from the managed heap. Designed for DMA development — run it after every game update to regenerate offsets.

---

## Features

- **Single-pass heap walk** — collects type cache, player objects, and component mappings in one enumeration
- Managed object field offsets for 15 target types
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
    ├── DataTarget.AttachToProcess (ClrMD, no suspend)
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
- .NET 8 Runtime / SDK
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
./bin/Release/net8.0/sbox-dumper.exe
```

---

## Output

The tool creates an `output/` directory containing:

| File | Description |
|---|---|
| `sbox_dump.json` | Full dump: modules, offsets, DMA map, player data |
| `offsets.json` | Offset tables only (all 15 target types with fields) |

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

- C# / .NET 8
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
