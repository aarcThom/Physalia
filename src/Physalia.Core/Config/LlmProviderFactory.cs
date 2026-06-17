// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models;
using Physalia.Core.Models.Named;
using Physalia.Core.Providers;
using Physalia.Core.Providers.ClaudeCode;
using Physalia.Core.Providers.Named;
using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Config;

/// <summary>
/// Returns the singleton provider instance that corresponds to a given <see cref="ModelConfig"/> type.
/// One <see cref="System.Net.Http.HttpClient"/> is shared across all calls of the same provider type.
/// </summary>
public static class LlmProviderFactory
{
    private static readonly AnthropicProvider _anthropic = new();
    private static readonly OpenAICompatibleProvider _openAICompatible = new();
    private static readonly GeminiProvider _gemini = new();
    private static readonly ClaudeCodeProvider _claudeCode = new();

    /// <summary>
    /// Returns the provider for the given config, or null if the config type is not recognised.
    /// </summary>
    /// <param name="config">The model configuration to resolve a provider for.</param>
    /// <returns>A shared provider instance, or null.</returns>
    public static ILlmProvider? GetProvider(ModelConfig config) => config switch
    {
        AnthropicConfig             => _anthropic,
        GeminiProtocolConfig        => _gemini,
        OpenAICompatibleConfig      => _openAICompatible,
        LlamaCppConfig              => _openAICompatible,
        ClaudeCodeConfig            => _claudeCode,
        _                           => null,
    };
}
