namespace SboxDumper.Services;

internal sealed class CliOptions
{
    public string ProcessName { get; private set; } = "sbox";
    public int? Pid { get; private set; }
    public string DmaPath { get; private set; } = "";
    public bool Suspend { get; private set; } = true;
    public bool Help { get; private set; }

    public static CliOptions Parse(string[] args, string? environmentPath)
    {
        var options = new CliOptions
        {
            DmaPath = string.IsNullOrWhiteSpace(environmentPath)
                ? Path.Combine("..", "dma_offsets.json") : environmentPath,
        };
        bool hasProcess = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid":
                    if (++i >= args.Length || !int.TryParse(args[i], out int pid) || pid <= 0)
                        throw new ArgumentException("--pid requires a positive integer.");
                    options.Pid = pid;
                    break;
                case "--dma-path":
                    if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith('-'))
                        throw new ArgumentException("--dma-path requires a path argument.");
                    options.DmaPath = args[i];
                    break;
                case "--no-suspend": options.Suspend = false; break;
                case "--help":
                case "-h": options.Help = true; break;
                default:
                    if (string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith('-') || hasProcess)
                        throw new ArgumentException($"Unexpected argument: {args[i]}");
                    options.ProcessName = args[i];
                    hasProcess = true;
                    break;
            }
        }
        return options;
    }
}
