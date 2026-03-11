// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Providers;

namespace Physalia.Core.Config;

/// <summary>
/// Creates <see cref="LlmProvider"/> instances for a given provider name and API key.
/// </summary>
public static class LlmProviderFactory
{
    /// <summary>
    /// Instantiates the correct <see cref="LlmProvider"/> subclass for the named provider.
    /// </summary>
    /// <param name="providerName">The canonical provider name, e.g. "anthropic".</param>
    /// <param name="apiKey">The API key used to authenticate requests to the provider.</param>
    /// <returns>A concrete <see cref="LlmProvider"/> instance configured with the supplied key.</returns>
    /// <exception cref="ArgumentException">Thrown when the provider name is not recognized.</exception>
    public static LlmProvider Create(string providerName, string apiKey) =>
        providerName switch
        {
            "anthropic" => new AnthropicProvider(apiKey),
            //"openai" => new OpenAiProvider(apiKey),
            _ => throw new ArgumentException($"Unknown provider: {providerName}", nameof(providerName))
        };
}
