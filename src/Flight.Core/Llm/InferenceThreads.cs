namespace Flight.Core.Llm;

/// <summary>
/// Thread counts for CPU inference. Matrix maths gains little from
/// hyper-threads, and oversubscribing slows everything down, so on machines
/// with many logical cores use about one thread per physical core.
/// </summary>
public static class InferenceThreads
{
    public const int Max = 8;

    public static int Recommended => For(Environment.ProcessorCount);

    internal static int For(int logicalCores) =>
        Math.Clamp(logicalCores >= 8 ? logicalCores / 2 : logicalCores, 1, Max);
}
