using System.Net;
using Flight.Core;
using Flight.Core.Models;
using Xunit;

namespace Flight.Tests.Models;

public sealed class HttpModelStoreTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly FlightOptions _options;

    public HttpModelStoreTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "flight-tests-" + Guid.NewGuid().ToString("N"));
        _options = new FlightOptions
        {
            DataDirectory = _dataDirectory,
            SttModel = new ModelSpec("stt.bin", "https://models.test/stt.bin"),
            LlmModel = new ModelSpec("llm.gguf", "https://models.test/llm.gguf"),
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDirectory, recursive: true); } catch (IOException) { }
    }

    /// <summary>In-process fake HTTP handler — no real network involved.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Responses { get; } = [];
        public List<string> RequestedUrls { get; } = [];
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);
            if (StatusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(StatusCode));
            }
            var content = new ByteArrayContent(Responses[url]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private static HttpModelStore CreateStore(FlightOptions options, FakeHandler handler) =>
        new(options, new HttpClient(handler));

    // Criterion: first run with no models downloads them and reports progress
    [Fact]
    public async Task EnsureModels_Reports_Progress_When_Models_Missing()
    {
        var handler = new FakeHandler();
        handler.Responses["https://models.test/stt.bin"] = new byte[100];
        handler.Responses["https://models.test/llm.gguf"] = new byte[200];
        var store = CreateStore(_options, handler);
        Assert.False(store.ModelsArePresent);

        var reports = new List<ModelDownloadProgress>();
        var progress = new Progress<ModelDownloadProgress>(reports.Add);
        await store.EnsureModelsAsync(progress);
        // Progress<T> posts asynchronously; give queued reports a moment to land
        await Task.Delay(100);

        Assert.True(store.ModelsArePresent);
        Assert.Equal(100, new FileInfo(store.SttModelPath).Length);
        Assert.Equal(200, new FileInfo(store.LlmModelPath).Length);
        Assert.Contains(reports, r => r.FileName == "stt.bin");
        Assert.Contains(reports, r => r.FileName == "llm.gguf");
    }

    [Fact]
    public async Task EnsureModels_Skips_Download_When_Models_Present()
    {
        var handler = new FakeHandler();
        handler.Responses["https://models.test/stt.bin"] = new byte[10];
        handler.Responses["https://models.test/llm.gguf"] = new byte[10];
        var store = CreateStore(_options, handler);
        await store.EnsureModelsAsync();
        handler.RequestedUrls.Clear();

        await store.EnsureModelsAsync();

        Assert.Empty(handler.RequestedUrls);
    }

    // Criterion: download failure is an explicit error, and no partial model
    // file is left behind masquerading as a valid model
    [Fact]
    public async Task Failed_Download_Throws_And_Leaves_No_Partial_Model_File()
    {
        var handler = new FakeHandler { StatusCode = HttpStatusCode.InternalServerError };
        var store = CreateStore(_options, handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.EnsureModelsAsync());

        Assert.False(store.ModelsArePresent);
        Assert.False(File.Exists(store.SttModelPath));
        Assert.False(File.Exists(store.SttModelPath + ".download"));
    }
}
