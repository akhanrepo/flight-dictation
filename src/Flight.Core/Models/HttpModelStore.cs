namespace Flight.Core.Models;

public class HttpModelStore : IModelStore
{
    private readonly FlightOptions _options;
    private readonly HttpClient _httpClient;

    public HttpModelStore(FlightOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public string SttModelPath => Path.Combine(_options.ModelsDirectory, _options.SttModel.FileName);
    public string LlmModelPath => Path.Combine(_options.ModelsDirectory, _options.LlmModel.FileName);

    public bool ModelsArePresent => File.Exists(SttModelPath) && File.Exists(LlmModelPath);

    public async Task EnsureModelsAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.ModelsDirectory);
        await EnsureModelAsync(_options.SttModel, SttModelPath, progress, cancellationToken);
        await EnsureModelAsync(_options.LlmModel, LlmModelPath, progress, cancellationToken);
    }

    private async Task EnsureModelAsync(
        ModelSpec spec, string targetPath, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath))
        {
            return;
        }

        // Download to a temp file, then rename: a partial download never
        // appears as a valid model on disk.
        var tempPath = targetPath + ".download";
        try
        {
            using var response = await _httpClient.GetAsync(
                spec.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                progress?.Report(new ModelDownloadProgress(spec.FileName, 0, totalBytes));
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    progress?.Report(new ModelDownloadProgress(spec.FileName, downloaded, totalBytes));
                }
            }

            File.Move(tempPath, targetPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
