using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Providers;

public class AnthropicProvider : ILlmProvider
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";
    private const string DefaultModel = "claude-haiku-4-5";

    private readonly HttpClient _http;

    public AnthropicProvider(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public async Task<string> SendPromptAsync(
        string systemPrompt,
        string userPrompt,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        // Build the request body
        var requestBody = new AnthropicRequest
        {
            Model = DefaultModel,
            MaxTokens = 4096,
            System = systemPrompt,
            Messages = new[]
            {
                new AnthropicMessage { Role = "user", Content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        // Send the request
        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        // If the API returned an error, throw with the details
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Anthropic API error {(int)response.StatusCode}: {responseBody}");
        }

        // Parse the Anthropic response to extract the text content
        var anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody);

        if (anthropicResponse is null)
            throw new Exception("Failed to deserialize Anthropic response.");

        // Claude returns an array of content blocks — join all text blocks
        var text = string.Join("", anthropicResponse.Content
            .Where(c => c.Type == "text")
            .Select(c => c.Text));

        return text;
    }

    // --- Internal DTOs that match the Anthropic API shape ---

    private class AnthropicRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("system")]
        public string System { get; set; } = "";

        [JsonPropertyName("messages")]
        public AnthropicMessage[] Messages { get; set; } = Array.Empty<AnthropicMessage>();
    }

    private class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private class AnthropicResponse
    {
        [JsonPropertyName("content")]
        public List<ContentBlock> Content { get; set; } = new();
    }

    private class ContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }
}