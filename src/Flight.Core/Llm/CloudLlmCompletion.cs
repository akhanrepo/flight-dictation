using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flight.Core.Security;

namespace Flight.Core.Llm;

/// <summary>
/// Cloud cleanup over the OpenAI-compatible <c>/chat/completions</c> shape.
/// Deliberately budget-tier (see <see cref="FlightOptions.CloudLlmModel"/>),
/// not a frontier model.
/// </summary>
/// <remarks>
/// The PRD leaves the provider open. This speaks the chat-completions shape
/// that OpenAI, DeepSeek, Groq, Together and Gemini's compatibility endpoint
/// all accept, and takes both the base URL and the model name from options —
/// so changing provider is configuration, not code. That keeps an open pricing
/// question from becoming an architectural one.
/// </remarks>
public class CloudLlmCompletion : ILlmCompletion
{
    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secrets;
    private readonly FlightOptions _options;

    public CloudLlmCompletion(HttpClient httpClient, ISecretStore secrets, FlightOptions options)
    {
        _httpClient = httpClient;
        _secrets = secrets;
        _options = options;
    }

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = _secrets.Get(SecretNames.CloudLlmApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new CloudLlmException(
                "Cloud mode is on but no API key is saved. Add one in Settings, or switch back to local mode.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.CloudLlmTimeout);

        using var httpRequest = BuildRequest(request, apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CloudLlmException(
                $"The cloud model didn't respond within {_options.CloudLlmTimeout.TotalSeconds:F0} seconds. " +
                "Try again, or switch to local mode in Settings.");
        }
        catch (HttpRequestException ex)
        {
            throw new CloudLlmException(
                $"Couldn't reach the cloud model: {ex.Message}. " +
                "Check your connection, or switch to local mode in Settings.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new CloudLlmException(await DescribeFailureAsync(response, timeout.Token));
            }

            ChatCompletionResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(timeout.Token);
            }
            catch (Exception ex)
            {
                throw new CloudLlmException(
                    "The cloud model returned a response Flight couldn't read. " +
                    "Try again, or switch to local mode in Settings.", ex);
            }

            var content = body?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                // An empty result is a failure, not a valid cleanup. Returning
                // it would silently wipe the user's dictation.
                throw new CloudLlmException(
                    "The cloud model returned an empty result. " +
                    "Try again, or switch to local mode in Settings.");
            }

            return content.Trim();
        }
    }

    private HttpRequestMessage BuildRequest(LlmRequest request, string apiKey)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.CloudLlmEndpoint)
        {
            Content = JsonContent.Create(new ChatCompletionRequest
            {
                Model = _options.CloudLlmModel,
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
                Messages =
                [
                    new ChatMessage { Role = "system", Content = request.SystemPrompt },
                    new ChatMessage { Role = "user", Content = request.UserPrompt },
                ],
            }),
        };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return httpRequest;
    }

    private static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var suffix = "Switch to local mode in Settings to keep dictating.";

        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "The cloud provider rejected the saved API key.",
            HttpStatusCode.TooManyRequests =>
                "The cloud provider is rate-limiting this key.",
            >= HttpStatusCode.InternalServerError =>
                "The cloud provider is having problems right now.",
            _ => null,
        };

        if (reason is not null)
        {
            return $"{reason} {suffix}";
        }

        string detail;
        try
        {
            detail = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        }
        catch
        {
            detail = string.Empty;
        }

        detail = detail.Length > 200 ? detail[..200] : detail;
        return $"The cloud model returned {(int)response.StatusCode} {response.StatusCode}. " +
               (detail.Length > 0 ? $"{detail} " : string.Empty) + suffix;
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}
