namespace SboxDumper.Models;

class DumpResult
{
    public string Process { get; set; } = "";
    public int Pid { get; set; }
    public string DumpedAt { get; set; } = "";
    public string ClrVersion { get; set; } = "";
    public int SchemaVersion { get; set; } = 2;
    public bool TargetSuspended { get; set; }
    public List<string> MissingOffsets { get; set; } = [];
    public List<string> MissingRequiredOffsets { get; set; } = [];
    public bool OffsetsComplete => MissingRequiredOffsets.Count == 0 &&
        new[] { "GameObject", "GameTransform", "DxrpPlayer" }.All(category =>
            DmaOffsets.TryGetValue(category, out var fields) && fields.Count > 0);
    public List<string> ReadWarnings { get; set; } = [];

    public Dictionary<string, ModuleEntry> Modules { get; set; } = new();
    public Dictionary<string, OffsetTableEntry> Offsets { get; set; } = new();
    public Dictionary<string, Dictionary<string, DmaOffset>> DmaOffsets { get; set; } = new();
    public List<PlayerDump> Players { get; set; } = [];
}

class ModuleEntry
{
    public string Base { get; set; } = "";
    public uint Size { get; set; }
}
