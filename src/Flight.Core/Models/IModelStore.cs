namespace Flight.Core.Models;

public record ModelDownloadProgress(string FileName, long BytesDownloaded, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0 ? (double)BytesDownloaded / TotalBytes.Value * 100 : null;
}

/// <summary>
/// Ensures the local STT and LLM model files exist, downloading them on first
/// run. Models are never bundled with the installer (per PRD tech constraints).
/// </summary>
public interface IModelStore
{
    bool ModelsArePresent { get; }

    string SttModelPath { get; }
    string LlmModelPath { get; }

    Task EnsureModelsAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}
