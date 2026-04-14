// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Providers.Concrete;

/// <summary>
/// OpenRouter implementation of <see cref="OpenAiCompatibleProvider"/>.
/// Provides access to hundreds of models from multiple providers through a
/// single OpenAI-compatible API endpoint, including a rotating selection of
/// free open-weight models.
/// See: https://openrouter.ai/docs/quickstart.
/// </summary>
internal class OpenRouterProvider : OpenAiCompatibleProvider
{
    /// <summary>
    /// Per-model output token limits populated during <see cref="GetModelsAsync"/>.
    /// </summary>
    private Dictionary<string, int> _modelMaxTokens = new ();

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenRouterProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the OpenRouter API.</param>
    public OpenRouterProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the OpenRouter provider.
    /// </summary>
    public override string ProviderName => "openrouter";

    /// <summary>
    /// Gets the OpenRouter API base URL.
    /// </summary>
    protected override string BaseUrl => "https://openrouter.ai/api/v1";

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
    /// Fetches available OpenRouter models and populates <see cref="_models"/> and per-model
    /// output token limits. The OpenRouter models endpoint returns a <c>top_provider</c> object
    /// per model containing <c>max_completion_tokens</c>.
    /// </summary>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    public override async Task GetModelsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{ProviderName} models error {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<ModelsResponse>(body);
        _models = parsed?.Data.Select(m => m.Id).ToList() ?? new List<string>();
        _modelMaxTokens = parsed?.Data
            .Where(m => m.TopProvider?.MaxCompletionTokens is > 0)
            .ToDictionary(m => m.Id, m => m.TopProvider!.MaxCompletionTokens!.Value)
            ?? new Dictionary<string, int>();
    }

    private class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelEntry> Data { get; set; } = new ();
    }

    private class ModelEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("top_provider")]
        public TopProvider? TopProvider { get; set; }
    }

    private class TopProvider
    {
        [JsonPropertyName("max_completion_tokens")]
        public int? MaxCompletionTokens { get; set; }
    }
}
