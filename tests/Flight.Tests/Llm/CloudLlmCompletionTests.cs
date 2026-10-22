using System.Net;
using System.Text;
using System.Text.Json;
using Flight.Core;
using Flight.Core.Llm;
using Flight.Core.Security;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Llm;

/// <summary>
/// Feature #4: the cloud client's contract — budget-tier model, same
/// input/output as local, and explicit errors with no silent fallback.
/// </summary>
public class CloudLlmCompletionTests
{
    private readonly FlightOptions _options = new();

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { role = "assistant", content } } },
            }),
            Encoding.UTF8,
            "application/json"),
    };

    private CloudLlmCompletion CreateClient(
        StubHttpMessageHandler handler,
        string? apiKey = "test-key")
        => new(new HttpClient(handler), new InMemorySecretStore(apiKey), _options);

    // Criterion: cleanup routes the raw transcript to the cloud API through the
    // same input/output contract as local
    [Fact]
    public async Task Sends_The_Prompt_And_Returns_The_Cleaned_Text()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(Json("Hello, world.")));

        var result = await CreateClient(handler).CompleteAsync(
            new LlmRequest("system instruction", "um hello uh world"));

        Assert.Equal("Hello, world.", result);
        Assert.Contains("um hello uh world", handler.LastRequestBody);
        Assert.Contains("system instruction", handler.LastRequestBody);
    }

    // Criterion: the model used is budget-tier, not frontier
    [Fact]
    public async Task Uses_The_Configured_BudgetTier_Model()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(Json("ok")));

        await CreateClient(handler).CompleteAsync(new LlmRequest("s", "u"));

        Assert.Equal("gpt-4o-mini", _options.CloudLlmModel);
        Assert.Contains("gpt-4o-mini", handler.LastRequestBody);
    }

    // The provider is configuration, not code — the PRD leaves it open
    [Fact]
    public async Task Provider_Endpoint_And_Model_Are_Configurable()
    {
        _options.CloudLlmEndpoint = new Uri("https://api.deepseek.com/v1/chat/completions");
        _options.CloudLlmModel = "deepseek-chat";
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(Json("ok")));

        await CreateClient(handler).CompleteAsync(new LlmRequest("s", "u"));

        Assert.Equal("api.deepseek.com", handler.Requests[0].RequestUri!.Host);
        Assert.Contains("deepseek-chat", handler.LastRequestBody);
    }

    [Fact]
    public async Task Sends_The_Stored_ApiKey_As_A_Bearer_Token()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(Json("ok")));

        await CreateClient(handler, apiKey: "sk-secret").CompleteAsync(new LlmRequest("s", "u"));

        var auth = handler.Requests[0].Headers.Authorization;
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("sk-secret", auth.Parameter);
    }

    // Criterion: an explicit error state, never a silent failure — missing key
    [Fact]
    public async Task MissingApiKey_Fails_Explicitly_Without_Calling_The_Api()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(Json("ok")));

        var ex = await Assert.ThrowsAsync<CloudLlmException>(() =>
            CreateClient(handler, apiKey: null).CompleteAsync(new LlmRequest("s", "u")));

        Assert.Contains("no API key", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    // Criterion: an API failure is explicit, and names the way back to local
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the saved API key")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limiting")]
    [InlineData(HttpStatusCode.InternalServerError, "having problems")]
    public async Task ApiFailure_Throws_An_Explicit_Error_Naming_The_Way_Back(
        HttpStatusCode status, string expected)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") }));

        var ex = await Assert.ThrowsAsync<CloudLlmException>(() =>
            CreateClient(handler).CompleteAsync(new LlmRequest("s", "u")));

        Assert.Contains(expected, ex.Message);
        Assert.Contains("local mode", ex.Message);
    }

    // Criterion: a timeout is an explicit error, not a silent hang
    [Fact]
    public async Task Timeout_Throws_An_Explicit_Error()
    {
        _options.CloudLlmTimeout = TimeSpan.FromMilliseconds(50);
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json("never");
        });

        var ex = await Assert.ThrowsAsync<CloudLlmException>(() =>
            CreateClient(handler).CompleteAsync(new LlmRequest("s", "u")));

        Assert.Contains("didn't respond", ex.Message);
        Assert.Contains("local mode", ex.Message);
    }

    [Fact]
    public async Task NetworkFailure_Throws_An_Explicit_Error()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("no such host"));

        var ex = await Assert.ThrowsAsync<CloudLlmException>(() =>
            CreateClient(handler).CompleteAsync(new LlmRequest("s", "u")));

        Assert.Contains("Couldn't reach the cloud model", ex.Message);
    }

    // Criterion: never a silent empty result — that would wipe the dictation
    [Fact]
    public async Task EmptyResult_Is_An_Error_Not_An_Empty_Cleanup()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(Json("   ")));

        var ex = await Assert.ThrowsAsync<CloudLlmException>(() =>
            CreateClient(handler).CompleteAsync(new LlmRequest("s", "u")));

        Assert.Contains("empty result", ex.Message);
    }

    [Fact]
    public async Task UnreadableResponse_Is_An_Error()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<CloudLlmException>(() =>
            CreateClient(handler).CompleteAsync(new LlmRequest("s", "u")));

        Assert.Contains("couldn't read", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Criterion: the API key is kept in the secret store, never in options or
    // a plaintext config field
    [Fact]
    public void ApiKey_Comes_From_The_Secret_Store()
    {
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretNames.CloudLlmApiKey, "sk-secret");

        Assert.Equal("sk-secret", secrets.Get(SecretNames.CloudLlmApiKey));

        // Credentials must not have a home in plain options, which are plain
        // config.
        Assert.DoesNotContain(
            typeof(FlightOptions).GetProperties(),
            property => property.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }
}
