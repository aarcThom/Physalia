// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Providers;

/// <summary>
/// Groq implementation of <see cref="OpenAiCompatibleProvider"/>.
/// Provides high-speed inference via Groq's LPU hardware using its
/// OpenAI-compatible API.
/// See: https://console.groq.com/docs/overview.
/// </summary>
internal class GroqProvider : OpenAiCompatibleProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GroqProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the Groq API.</param>
    public GroqProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the Groq provider.
    /// </summary>
    public override string ProviderName => "groq";

    /// <summary>
    /// Gets the maximum number of output tokens for Groq API requests.
    /// </summary>
    public override int MaxTokens => 4096;

    /// <summary>
    /// Gets the Groq API base URL.
    /// </summary>
    protected override string BaseUrl => "https://api.groq.com/openai/v1";
}
