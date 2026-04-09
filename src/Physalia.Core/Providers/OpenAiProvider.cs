// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Providers;

/// <summary>
/// OpenAI implementation of <see cref="OpenAiCompatibleProvider"/>.
/// Provides access to GPT and o-series models via the OpenAI API.
/// See: https://platform.openai.com/docs/api-reference.
/// </summary>
internal class OpenAiProvider : OpenAiCompatibleProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the OpenAI API.</param>
    public OpenAiProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the OpenAI provider.
    /// </summary>
    public override string ProviderName => "openai";

    /// <summary>
    /// Gets the OpenAI API base URL.
    /// </summary>
    protected override string BaseUrl => "https://api.openai.com/v1";
}
