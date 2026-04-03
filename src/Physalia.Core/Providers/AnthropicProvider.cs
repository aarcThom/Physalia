// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Providers;

/// <summary>
/// Anthropic-specific implementation of <see cref="LlmProvider"/> that communicates
/// with the Anthropic messages API to generate text and retrieve available Claude models.
/// </summary>
public class AnthropicProvider : LlmProvider
{
    // see: https://platform.claude.com/docs/en/api/overview
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    /// <summary>
    /// The name of the Anthropic provider.
    /// </summary>
    public override string ProviderName => "Anthropic";

    /// <summary>
    /// Maximum number of tokens for Anthropic API requests.
    /// </summary>
    public override int MaxTokens => 16000; // claude-sonnet-4-6 supports up to 64K; 16K is a practical ceiling for GH scripts

    /// <summary>
    /// Initializes a new instance of the <see cref="AnthropicProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the Anthropic API.</param>
    public AnthropicProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Fetches available Claude models from the Anthropic API and populates <see cref="_models"/>.
    /// </summary>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    public override async Task GetModelsAsync()
    {
        // see: https://platform.claude.com/docs/en/api/csharp/beta/models/list (March 2026)

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Anthropic models error {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<ModelsResponse>(body);
        _models = parsed?.Data.Select(m => m.Id).ToList() ?? new List<string>();
    }

    /// <summary>
    /// Sends a multi-turn conversation to the Anthropic messages API and returns the raw response text.
    /// </summary>
    /// <param name="systemPrompt">The system prompt that defines the model's behavior and context.</param>
    /// <param name="history">The ordered list of conversation messages to send.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The concatenated text content from all text blocks in the Anthropic response.</returns>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the response cannot be deserialized.</exception>
    protected override async Task<string> SendConversationCoreAsync(string systemPrompt, IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken)
    {
        // Build the request body
        var requestBody = new AnthropicRequest
        {
            RequestModel = CurrentModel,
            RequestMaxTokens = MaxTokens,
            System = systemPrompt,
            Messages = history.Select(m => new AnthropicMessage { Role = m.Role, Content = m.Content }).ToArray(),
        };

        var requestJson = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("x-api-key", _apiKey);
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
        var anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody) ?? throw new InvalidOperationException("Failed to deserialize Anthropic response.");

        // Claude returns an array of content blocks — join all text blocks
        var text = string.Join(string.Empty, anthropicResponse.Content
            .Where(c => c.Type == "text")
            .Select(c => c.Text));

        return text;
    }

    // see: https://platform.claude.com/docs/en/api/csharp/messages/create (march 2026)

    /// <summary>
    /// The envelope object returned by the Anthropic models API containing the list of available models.
    /// </summary>
    private class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelEntry> Data { get; set; } = new ();
    }

    /// <summary>
    /// Represents a single model entry in the Anthropic models API response.
    /// </summary>
    private class ModelEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request body DTO matching the Anthropic messages API shape.
    /// See: https://docs.anthropic.com/en/api/messages
    /// </summary>
    private class AnthropicRequest
    {
        [JsonPropertyName("model")]
        public string RequestModel { get; set; } = string.Empty;

        [JsonPropertyName("max_tokens")]
        public int RequestMaxTokens { get; set; }

        [JsonPropertyName("system")]
        public string System { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public AnthropicMessage[] Messages { get; set; } = Array.Empty<AnthropicMessage>();
    }

    /// <summary>
    /// Represents a single message in the Anthropic messages API request.
    /// </summary>
    private class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response body DTO matching the Anthropic messages API shape.
    /// </summary>
    private class AnthropicResponse
    {
        [JsonPropertyName("content")]
        public List<ContentBlock> Content { get; set; } = new ();
    }

    /// <summary>
    /// Represents a single content block in the Anthropic messages API response.
    /// </summary>
    private class ContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }
}