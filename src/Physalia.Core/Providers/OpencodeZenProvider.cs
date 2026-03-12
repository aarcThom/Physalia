// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Providers;

/// <summary>
/// OpenCode Zen implementation of <see cref="LlmProvider"/> that communicates
/// with the OpenCode Zen unified API — an OpenAI-compatible proxy that provides
/// access to a curated set of models benchmarked for coding agent tasks.
/// </summary>
/// <remarks>
/// OpenCode Zen exposes an OpenAI-compatible chat completions endpoint.
/// Models are referenced using the format <c>opencode/&lt;model-id&gt;</c>.
/// See: https://opencode.ai/docs/zen/.
/// </remarks>
internal class OpencodeZenProvider : LlmProvider
{
    // see: https://opencode.ai/docs/zen/
    private const string BaseUrl = "https://opencode.ai/zen/v1";

    /// <summary>
    /// Initializes a new instance of the <see cref="OpencodeZenProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the OpenCode Zen API.</param>
    public OpencodeZenProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the OpenCode Zen provider.
    /// </summary>
    public override string ProviderName => "opencode zen";

    /// <summary>
    /// Gets the maximum number of output tokens for OpenCode Zen API requests.
    /// </summary>
    public override int MaxTokens => 4096;

    /// <summary>
    /// Fetches available models from the OpenCode Zen models endpoint and populates <see cref="_models"/>.
    /// </summary>
    /// <returns>A task that completes when the model list has been populated.</returns>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    public override async Task GetModelsAsync()
    {
        // see: https://opencode.ai/zen/v1/models
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenCode Zen models error {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<ModelsResponse>(body);
        _models = parsed?.Data.Select(m => m.Id).ToList() ?? new List<string>();
    }

    /// <summary>
    /// Sends a system and user prompt to the OpenCode Zen chat completions endpoint and returns the response text.
    /// </summary>
    /// <param name="systemPrompt">The system prompt that defines the model's behavior and context.</param>
    /// <param name="userPrompt">The user prompt containing the request to be processed.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The assistant message content from the first choice in the response.</returns>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the response cannot be deserialized or contains no choices.</exception>
    protected override async Task<string> SendPromptCoreAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var requestBody = new ZenRequest
        {
            Model = CurrentModel,
            MaxTokens = MaxTokens,
            Messages = new[]
            {
                new ZenMessage { Role = "system", Content = systemPrompt },
                new ZenMessage { Role = "user", Content = userPrompt },
            },
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
            throw new HttpRequestException($"OpenCode Zen API error {(int)response.StatusCode}: {responseBody}");
        }

        var zenResponse = JsonSerializer.Deserialize<ZenResponse>(responseBody)
            ?? throw new InvalidOperationException("Failed to deserialize OpenCode Zen response.");

        if (zenResponse.Choices.Count == 0)
        {
            throw new InvalidOperationException("OpenCode Zen response contained no choices.");
        }

        return zenResponse.Choices[0].Message.Content;
    }

    /// <summary>
    /// Envelope returned by the OpenCode Zen models list endpoint.
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
    /// Request body DTO for the OpenCode Zen chat completions endpoint.
    /// </summary>
    private class ZenRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("messages")]
        public ZenMessage[] Messages { get; set; } = Array.Empty<ZenMessage>();
    }

    /// <summary>
    /// A single message in a chat completions request.
    /// </summary>
    private class ZenMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Top-level response body from the OpenCode Zen chat completions endpoint.
    /// </summary>
    private class ZenResponse
    {
        [JsonPropertyName("choices")]
        public List<ZenChoice> Choices { get; set; } = new ();
    }

    /// <summary>
    /// A single completion choice in the OpenCode Zen response.
    /// </summary>
    private class ZenChoice
    {
        [JsonPropertyName("message")]
        public ZenMessage Message { get; set; } = new ();
    }
}
