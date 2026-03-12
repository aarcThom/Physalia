// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Providers;

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
    /// Gets the maximum number of output tokens for OpenRouter API requests.
    /// </summary>
    public override int MaxTokens => 4096;

    /// <summary>
    /// Gets the OpenRouter API base URL.
    /// </summary>
    protected override string BaseUrl => "https://openrouter.ai/api/v1";
}
