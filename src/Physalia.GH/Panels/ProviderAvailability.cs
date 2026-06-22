// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.Config;
using Physalia.Core.Models.Named;
using Physalia.Core.Providers.ClaudeCode;
using Physalia.Core.Tokens;

namespace Physalia.GH.Panels;

/// <summary>
/// Detects which usable LLM providers the user has configured, so the chat window can show its
/// first-run setup state when none is and list the ready ones otherwise. A provider counts as
/// available when an API key is resolvable from <c>API_KEY_CONFIG.YAML</c> (or its env vars), the
/// Claude Code CLI is installed on PATH, or a local llama-server answers at its default endpoint.
/// The llama-server probe is a network call, so the whole check is async and runs off the UI thread.
/// </summary>
internal static class ProviderAvailability
{
    // Maps an API-key provider name (as reported by Api.GetKeys) to the setup-screen provider id the
    // chat UI knows (providers.ts). openai_compatible keys report their leaf name (openai/deepseek/
    // openrouter); anthropic and gemini report their section name. Keeps the OpenAI-compatible
    // providers identifiable by brand rather than collapsing them into one "OpenAI compatible" entry.
    private static readonly Dictionary<string, string> KeyProviderToSetupId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic"] = "anthropic",
            ["gemini"] = "google",
            ["openai"] = "openai",
            ["deepseek"] = "deepseek",
            ["openrouter"] = "openrouter",
        };

    /// <summary>
    /// Returns the setup-screen ids of every configured provider — empty when none is, which is
    /// the signal for the window to show first-run setup. Key providers are read from the config
    /// first (cheap), then the Claude CLI, then the llama-server network probe last. Order follows
    /// that discovery order; the UI re-sorts into its own canonical order for display.
    /// </summary>
    /// <param name="apiKeyConfigPath">Absolute path to <c>API_KEY_CONFIG.YAML</c>.</param>
    /// <param name="client">Shared HTTP client for the llama-server probe.</param>
    /// <param name="ct">Cancellation token bounding the network probe.</param>
    /// <returns>The configured providers' setup ids; empty when the window should show setup.</returns>
    public static async Task<IReadOnlyList<string>> ConfiguredProviderIdsAsync(
        string apiKeyConfigPath,
        HttpClient client,
        CancellationToken ct)
    {
        var ids = new List<string>();

        foreach (string id in ConfiguredKeyProviderIds(apiKeyConfigPath))
        {
            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        if (ClaudeCodeProvider.IsCliAvailable())
        {
            ids.Add("claude-code");
        }

        if (await HasLlamaServerAsync(client, ct).ConfigureAwait(false))
        {
            ids.Add("local-llm");
        }

        return ids;
    }

    // The setup ids of every provider whose key resolves from the config file or its env vars.
    private static IReadOnlyList<string> ConfiguredKeyProviderIds(string apiKeyConfigPath)
    {
        var ids = new List<string>();

        IReadOnlyList<ApiKey> keys;
        try
        {
            keys = Api.GetKeys(apiKeyConfigPath);
        }
        catch
        {
            return ids;
        }

        foreach (ApiKey key in keys)
        {
            if (KeyProviderToSetupId.TryGetValue(key.Provider, out string? id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    // True when a llama-server answers a props query at the default local endpoint.
    private static async Task<bool> HasLlamaServerAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            Result<LlamaCppServerProps, LlmError> result =
                await LlamaCppServerQuery.GetPropsAsync(new LlamaCppConfig(), client, ct).ConfigureAwait(false);
            return result is Result<LlamaCppServerProps, LlmError>.Ok;
        }
        catch
        {
            return false;
        }
    }
}
