// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models;
using Physalia.Core.Models.Named;
using Physalia.Core.Providers;
using Physalia.Core.Providers.Named;

namespace Physalia.Core.Config;

/// <summary>
/// Returns the singleton provider instance that corresponds to a given <see cref="ModelConfig"/> type.
/// One <see cref="System.Net.Http.HttpClient"/> is shared across all calls of the same provider type.
/// </summary>
public static class LlmProviderFactory
{
    private static readonly AnthropicProvider _anthropic = new();
    private static readonly LlamaCppProvider _llamaCpp = new();

    /// <summary>
    /// Returns the provider for the given config, or null if the config type is not recognised.
    /// </summary>
    /// <param name="config">The model configuration to resolve a provider for.</param>
    /// <returns>A shared provider instance, or null.</returns>
    public static ILlmProvider? GetProvider(ModelConfig config) => config switch
    {
        AnthropicConfig => _anthropic,
        LlamaCppConfig  => _llamaCpp,
        _               => null,
    };
}
