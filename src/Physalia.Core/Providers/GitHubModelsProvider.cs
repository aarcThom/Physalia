// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Providers;

/// <summary>
/// GitHub Models implementation of <see cref="OpenAiCompatibleProvider"/>.
/// Provides free-tier access to models from OpenAI, Meta, Mistral and others
/// via GitHub's inference endpoint, authenticated with a GitHub Personal Access Token.
/// See: https://docs.github.com/github-models/prototyping-with-ai-models.
/// </summary>
internal class GitHubModelsProvider : OpenAiCompatibleProvider
{
    private const string CatalogUrl = "https://models.github.ai/catalog/models";

    /// <summary>
    /// Per-model output token limits populated during <see cref="GetModelsAsync"/>.
    /// </summary>
    private Dictionary<string, int> _modelMaxTokens = new ();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubModelsProvider"/> class.
    /// </summary>
    /// <param name="apiKey">A GitHub Personal Access Token (PAT) used to authenticate requests.</param>
    public GitHubModelsProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the GitHub Models provider.
    /// </summary>
    public override string ProviderName => "github models";

    /// <summary>
    /// Gets the GitHub Models inference API base URL.
    /// </summary>
    protected override string BaseUrl => "https://models.github.ai/inference";

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
    /// Fetches available models from the GitHub Models catalog endpoint and populates
    /// <see cref="_models"/> and per-model output token limits. The catalog returns a
    /// <c>limits</c> object per model containing <c>max_output_tokens</c>.
    /// </summary>
    /// <exception cref="HttpRequestException">Thrown when the API returns a non-success status code.</exception>
    public override async Task GetModelsAsync()
    {
        // The inference /models endpoint does not include token limits.
        // The catalog endpoint does, so we use it as the source of truth.
        // see: https://docs.github.com/en/rest/models/catalog
        using var request = new HttpRequestMessage(HttpMethod.Get, CatalogUrl);
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{ProviderName} models error {(int)response.StatusCode}: {body}");
        }

        var entries = JsonSerializer.Deserialize<List<CatalogModelEntry>>(body) ?? new List<CatalogModelEntry>();
        _models = entries.Select(m => m.Id).ToList();
        _modelMaxTokens = entries
            .Where(m => m.Limits?.MaxOutputTokens is > 0)
            .ToDictionary(m => m.Id, m => m.Limits!.MaxOutputTokens);
    }

    private class CatalogModelEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("limits")]
        public ModelLimits? Limits { get; set; }
    }

    private class ModelLimits
    {
        [JsonPropertyName("max_output_tokens")]
        public int MaxOutputTokens { get; set; }
    }
}
