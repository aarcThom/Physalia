// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

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
/// Detects whether the user has any usable LLM provider configured, so the chat window can
/// show its first-run setup state when none is. A provider counts as available when any of the
/// following holds: an API key is resolvable from <c>API_KEY_CONFIG.YAML</c> (or its env vars),
/// the Claude Code CLI is installed on PATH, or a local llama-server answers at its default
/// endpoint. The llama-server probe is a network call, so the whole check is async and runs off
/// the UI thread.
/// </summary>
internal static class ProviderAvailability
{
    /// <summary>
    /// Determines whether at least one LLM provider is configured. Cheap checks (API keys, then
    /// the Claude CLI) are evaluated first and short-circuit before the network probe, so the
    /// llama-server endpoint is only contacted when nothing else is set up.
    /// </summary>
    /// <param name="apiKeyConfigPath">Absolute path to <c>API_KEY_CONFIG.YAML</c>.</param>
    /// <param name="client">Shared HTTP client for the llama-server probe.</param>
    /// <param name="ct">Cancellation token bounding the network probe.</param>
    /// <returns>True when any provider is available; false when the window should show setup.</returns>
    public static async Task<bool> AnyConfiguredAsync(
        string apiKeyConfigPath,
        HttpClient client,
        CancellationToken ct)
    {
        if (HasAnyApiKey(apiKeyConfigPath))
        {
            return true;
        }

        if (ClaudeCodeProvider.IsCliAvailable())
        {
            return true;
        }

        return await HasLlamaServerAsync(client, ct).ConfigureAwait(false);
    }

    // True when at least one provider key resolves from the config file or its env vars.
    private static bool HasAnyApiKey(string apiKeyConfigPath)
    {
        try
        {
            return Api.GetKeys(apiKeyConfigPath).Count > 0;
        }
        catch
        {
            return false;
        }
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
