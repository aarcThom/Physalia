// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Providers;

/// <summary>
/// Abstract base class for providers that expose an OpenAI-compatible HTTP API.
/// Implements the standard <c>/models</c> list and <c>/chat/completions</c> endpoints
/// with <c>Authorization: Bearer</c> authentication.
/// </summary>
/// <remarks>
/// Subclasses only need to supply <see cref="ProviderName"/>, <see cref="MaxTokens"/>,
/// and <see cref="BaseUrl"/>. All HTTP logic is handled here.
/// </remarks>
internal abstract class OpenAiCompatibleProvider : LlmProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiCompatibleProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used for Bearer token authentication.</param>
    public OpenAiCompatibleProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the base URL of the provider's API, without a trailing slash.
    /// </summary>
    /// <remarks>
    /// The models list endpoint is resolved as <c>{BaseUrl}/models</c> and the chat
    /// completions endpoint as <c>{BaseUrl}/chat/completions</c>.
    /// </remarks>
    protected abstract string BaseUrl { get; }

    /// <summary>
    /// Fetches available models from <c>{BaseUrl}/models</c> and populates <see cref="_models"/>.
    /// </summary>
    /// <returns>A task that completes when the model list has been populated.</returns>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    public override async Task GetModelsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{ProviderName} models error {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<ModelsResponse>(body);
        _models = parsed?.Data.Select(m => m.Id).ToList() ?? new List<string>();
    }

    /// <summary>
    /// Sends a multi-turn conversation to <c>{BaseUrl}/chat/completions</c> and returns the response text.
    /// The system prompt is prepended as a <c>system</c> role message before the history.
    /// </summary>
    /// <param name="systemPrompt">The system prompt sent as the first message with role "system".</param>
    /// <param name="history">The ordered list of conversation messages to send.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The content of the first choice's assistant message.</returns>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the response cannot be deserialized or contains no choices.</exception>
    protected override async Task<string> SendConversationCoreAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken)
    {
        var messages = new[] { new ChatMessage { Role = "system", Content = systemPrompt } }
            .Concat(history.Select(m => new ChatMessage { Role = m.Role, Content = m.Content }))
            .ToArray();

        var requestBody = new ChatRequest
        {
            Model = CurrentModel,
            MaxTokens = MaxTokens,
            Messages = messages,
        };

        var requestJson = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{ProviderName} API error {(int)response.StatusCode}: {responseBody}");
        }

        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody)
            ?? throw new InvalidOperationException($"Failed to deserialize {ProviderName} response.");

        if (chatResponse.Choices.Count == 0)
        {
            throw new InvalidOperationException($"{ProviderName} response contained no choices.");
        }

        return chatResponse.Choices[0].Message.Content;
    }

    /// <summary>
    /// Envelope returned by the OpenAI-compatible models list endpoint.
    /// </summary>
    private class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelEntry> Data { get; set; } = new ();
    }

    /// <summary>
    /// A single model entry in the models list response.
    /// </summary>
    private class ModelEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request body DTO for the OpenAI-compatible chat completions endpoint.
    /// </summary>
    private class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
    }

    /// <summary>
    /// A single message in a chat completions request or response.
    /// </summary>
    private class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Top-level response body from the OpenAI-compatible chat completions endpoint.
    /// </summary>
    private class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice> Choices { get; set; } = new ();
    }

    /// <summary>
    /// A single completion choice in the chat completions response.
    /// </summary>
    private class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage Message { get; set; } = new ();
    }
}
