// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Prompts;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Providers;

/// <summary>
/// Google Gemini-specific implementation of <see cref="LlmProvider"/> that communicates
/// with the Gemini generative language API to generate text and retrieve available models.
/// </summary>
internal class GeminiProvider : LlmProvider
{
    // see: https://ai.google.dev/api/generate-content
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the Gemini API.</param>
    public GeminiProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Per-model output token limits populated during <see cref="GetModelsAsync"/>.
    /// </summary>
    private Dictionary<string, int> _modelMaxTokens = new ();

    /// <summary>
    /// Gets the display name of the Google Gemini provider.
    /// </summary>
    public override string ProviderName => "Google";

    /// <summary>
    /// Sets the current model and updates <see cref="LlmProvider.MaxTokens"/> from the
    /// per-model limit fetched during <see cref="GetModelsAsync"/>.
    /// </summary>
    public override string CurrentModel
    {
        get => base.CurrentModel;
        set
        {
            base.CurrentModel = value;
            if (_modelMaxTokens.TryGetValue(value, out var tokens))
            {
                MaxTokens = tokens;
            }
        }
    }

    /// <summary>
    /// Fetches available Gemini models that support generateContent and populates <see cref="_models"/>
    /// and per-model output token limits. Model names are stripped of their "models/" prefix before being stored.
    /// </summary>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    public override async Task GetModelsAsync()
    {
        // see: https://ai.google.dev/api/models
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Add("x-goog-api-key", _apiKey);

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini models error {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<ModelsResponse>(body);

        var eligible = parsed?.Models
            .Where(m => m.SupportedGenerationMethods.Contains("generateContent"))
            .ToList() ?? new List<ModelEntry>();

        _models = eligible
            .Select(m => m.Name.StartsWith("models/") ? m.Name["models/".Length..] : m.Name)
            .ToList();

        _modelMaxTokens = eligible
            .ToDictionary(
                m => m.Name.StartsWith("models/") ? m.Name["models/".Length..] : m.Name,
                m => m.OutputTokenLimit);
    }

    /// <summary>
    /// Sends a multi-turn conversation to the Gemini generateContent API and returns the raw response text.
    /// </summary>
    /// <param name="systemPrompt">The system prompt that defines the model's behavior and context.</param>
    /// <param name="history">The ordered list of conversation messages to send.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The concatenated text from all parts across all response candidates.</returns>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the response cannot be deserialized.</exception>
    protected override async Task<string> SendConversationCoreAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken)
    {
        // Gemini uses "model" for the assistant role rather than "assistant"
        var requestBody = new GeminiRequest
        {
            SystemInstruction = new GeminiContent
            {
                Parts = new[] { new GeminiPart { Text = systemPrompt } },
            },
            Contents = history.Select(m => new GeminiMessage
            {
                Role = m.Role == "assistant" ? "model" : m.Role,
                Parts = new[] { new GeminiPart { Text = m.Content } },
            }).ToArray(),
            GenerationConfig = new GenerationConfig
            {
                MaxOutputTokens = MaxTokens,
            },
        };

        var requestJson = JsonSerializer.Serialize(requestBody);

        var url = $"{BaseUrl}/models/{CurrentModel}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("x-goog-api-key", _apiKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini API error {(int)response.StatusCode}: {responseBody}");
        }

        var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseBody) ?? throw new InvalidOperationException("Failed to deserialize Gemini response.");

        // Join all text parts across all candidates
        var text = string.Join(string.Empty, geminiResponse.Candidates
            .SelectMany(c => c.Content.Parts)
            .Select(p => p.Text));

        return text;
    }

    /// <summary>
    /// Envelope returned by the Gemini models list API.
    /// </summary>
    private class ModelsResponse
    {
        [JsonPropertyName("models")]
        public List<ModelEntry> Models { get; set; } = new ();
    }

    /// <summary>
    /// A single entry in the Gemini models list response.
    /// </summary>
    private class ModelEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("supportedGenerationMethods")]
        public List<string> SupportedGenerationMethods { get; set; } = new ();

        [JsonPropertyName("outputTokenLimit")]
        public int OutputTokenLimit { get; set; }
    }

    /// <summary>
    /// Top-level request body for the Gemini generateContent endpoint.
    /// </summary>
    private class GeminiRequest
    {
        [JsonPropertyName("system_instruction")]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public GeminiMessage[] Contents { get; set; } = Array.Empty<GeminiMessage>();

        [JsonPropertyName("generationConfig")]
        public GenerationConfig? GenerationConfig { get; set; }
    }

    /// <summary>
    /// A content block containing an array of parts, used for both system instructions and response content.
    /// </summary>
    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    /// <summary>
    /// A single message in the conversation, with a role and an array of parts.
    /// </summary>
    private class GeminiMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    /// <summary>
    /// A single text part within a content block or message.
    /// </summary>
    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generation configuration options sent with each request.
    /// </summary>
    private class GenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }
    }

    /// <summary>
    /// Top-level response body from the Gemini generateContent endpoint.
    /// </summary>
    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate> Candidates { get; set; } = new ();
    }

    /// <summary>
    /// A single generation candidate in the Gemini response.
    /// </summary>
    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent Content { get; set; } = new ();
    }
}
