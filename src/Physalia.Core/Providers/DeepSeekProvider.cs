// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Providers;

/// <summary>
/// DeepSeek implementation of <see cref="OpenAiCompatibleProvider"/>.
/// Provides access to DeepSeek's coding and reasoning models via its
/// OpenAI-compatible API.
/// See: https://platform.deepseek.com/api-docs/.
/// </summary>
internal class DeepSeekProvider : OpenAiCompatibleProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeepSeekProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the DeepSeek API.</param>
    public DeepSeekProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the DeepSeek provider.
    /// </summary>
    public override string ProviderName => "deepseek";

    /// <summary>
    /// Gets the maximum number of output tokens for DeepSeek API requests.
    /// </summary>
    public override int MaxTokens => 4096;

    /// <summary>
    /// Gets the DeepSeek API base URL.
    /// </summary>
    protected override string BaseUrl => "https://api.deepseek.com/v1";
}
