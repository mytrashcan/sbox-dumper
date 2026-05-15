# sbox-dumper

> DXRP / s&box runtime offset & player dumper built with ClrMD

`sbox-dumper` is a .NET 8 tool that attaches to a running `sbox` process and extracts:

- Runtime module information
- CLR heap object metadata
- DMA-friendly offset tables
- DXRP player data
- Health / armor / transform data
- Equipment & component references

Built using:
- .NET 8
- Microsoft.Diagnostics.Runtime (ClrMD)

---

# Features

- Attach to live `sbox.exe` process
- Enumerate important engine/runtime modules
- Dump managed object field offsets
- Generate condensed DMA offset maps
- Extract DXRP player information
- Read transform/position data directly from memory
- Export clean JSON output

---

# Example Output

```json
{
  "process": "sbox",
  "pid": 1234,
  "clr_version": "8.0.0",
  "players": [
    {
      "steam_name": "player",
      "health": {
        "current": 100
      }
    }
  ]
}
```

Generated files:

- `output/sbox_dump.json`
- `output/offsets.json`

---

# Requirements

- Windows
- .NET 8 Runtime / SDK
- Administrator privileges (recommended)
- Running `sbox` game instance

---

# Installation

```bash
git clone https://github.com/YOURNAME/sbox-dumper.git
cd sbox-dumper
dotnet restore
dotnet build -c Release
```

---

# Usage

Default target process:

```bash
dotnet run
```

Custom process name:

```bash
dotnet run -- someprocess
```

Or run compiled executable:

```bash
./sbox-dumper.exe
```

---

# Output

The tool creates an `output/` directory containing:

| File | Description |
|---|---|
| `sbox_dump.json` | Full runtime/player dump |
| `offsets.json` | Condensed DMA offsets |

---

# Internals

The dumper uses ClrMD to:

- Attach to a live CLR process
- Enumerate managed heap objects
- Resolve field offsets
- Read object references
- Parse runtime transforms/components

Main targets include:

- `Sandbox.GameObject`
- `Sandbox.GameTransform`
- `Dxura.RP.Game.Player`
- `HealthComponent`
- `ArmorComponent`
- `JobResource`

---

# Tech Stack

- C#
- .NET 8
- ClrMD (`Microsoft.Diagnostics.Runtime`)

---

# Disclaimer

This project is for educational and research purposes only.

Use responsibly and comply with all applicable game/server rules.

---

# License

MIT License
