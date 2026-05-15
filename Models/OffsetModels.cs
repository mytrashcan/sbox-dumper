namespace SboxDumper.Models;

class OffsetTableEntry
{
    public string TypeName { get; set; } = "";
    public string MethodTable { get; set; } = "";
    public int BaseSize { get; set; }
    public string? Parent { get; set; }
    public List<OffsetField> Fields { get; set; } = [];
}

class OffsetField
{
    public string Name { get; set; } = "";
    public string? CleanName { get; set; }
    public int Offset { get; set; }
    public string OffsetHex { get; set; } = "";
    public string Type { get; set; } = "";
    public int Size { get; set; }
    public bool IsValueType { get; set; }
    public bool IsSync { get; set; }
}

class DmaOffset
{
    public int Offset { get; set; }
    public string OffsetHex { get; set; } = "";
    public string Type { get; set; } = "";
    public int Size { get; set; }
}
