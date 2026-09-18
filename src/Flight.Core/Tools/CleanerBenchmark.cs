using System.Diagnostics;
using System.Text;
using Flight.Core.Llm;

namespace Flight.Core.Tools;

/// <summary>
/// Measures real cleanup latency on this machine, per the PRD's validation
/// plan (is a quantized local model fast enough for the dictation loop?).
/// Run via `Flight.App --benchmark`.
/// </summary>
public static class CleanerBenchmark
{
    public static readonly string[] SampleTranscripts =
    [
        "um so the uh the client wants the the security audit finished by friday and um we still need access to their staging environment",
        "okay so um three action items from the call uh first update the sow second um send the revised estimate and third uh schedule the follow up for next tuesday",
        "so the uh the migration plan is basically um we move the database first then uh the app servers and um dns cutover happens last probably over a weekend",
    ];

    public static async Task<string> RunAsync(ITextCleaner cleaner, CancellationToken cancellationToken = default)
    {
        var report = new StringBuilder();
        report.AppendLine($"Cleaner benchmark — {DateTimeOffset.Now:u}");
        report.AppendLine($"Machine: {Environment.MachineName}, {Environment.ProcessorCount} logical cores");
        report.AppendLine();

        var total = TimeSpan.Zero;
        for (var i = 0; i < SampleTranscripts.Length; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var cleaned = await cleaner.CleanAsync(SampleTranscripts[i], cancellationToken);
            stopwatch.Stop();
            total += stopwatch.Elapsed;

            report.AppendLine($"[{i + 1}] {stopwatch.Elapsed.TotalSeconds:F1}s (first run includes model load)");
            report.AppendLine($"    raw:     {SampleTranscripts[i]}");
            report.AppendLine($"    cleaned: {cleaned}");
            report.AppendLine();
        }

        report.AppendLine($"Total: {total.TotalSeconds:F1}s, average {total.TotalSeconds / SampleTranscripts.Length:F1}s per transcript");
        return report.ToString();
    }
}
