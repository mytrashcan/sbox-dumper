using System.Runtime.InteropServices;

namespace SboxDumper.Fixture;

internal static class Program
{
    static void Main()
    {
        var sample = new Sample();
        Console.WriteLine("ready");
        Console.ReadLine();
        GC.KeepAlive(sample);
    }
}

internal class SampleBase
{
    public int Inherited = 17;
}

internal sealed class Sample : SampleBase
{
    public int Zero = 0;
    public bool False = false;
    public float Invalid = float.PositiveInfinity;
    public Guid? EmptyGuid = System.Guid.Empty;
    public Guid? Guid = new System.Guid("00112233-4455-6677-8899-aabbccddeeff");
    public Transform _targetLocal = new() { ScaleX = 2, ScaleY = 3, ScaleZ = 4, RotationW = 1 };
}

[StructLayout(LayoutKind.Sequential)]
internal struct Transform
{
    public float X, Y, Z;
    public float ScaleX, ScaleY, ScaleZ;
    public float RotationX, RotationY, RotationZ, RotationW;
}
