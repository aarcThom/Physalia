// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.Config;
using Physalia.Core.Models.Named;
using Physalia.Core.Providers.ClaudeCode;
using Physalia.Core.Providers.Codex;
using Physalia.Core.Tokens;
using Physalia.GH.Config;

namespace Physalia.GH.Panels;

/// <summary>
/// Detects which usable LLM providers the user has configured, so the chat window can show its
/// first-run setup state when none is and list the ready ones otherwise.
/// </summary>
/// <remarks>
/// <para>A provider is AVAILABLE when its credential resolves (environment variable or the encrypted
/// store), the Claude Code or Codex CLI is installed on PATH, or a local llama-server answers at its
/// default endpoint. It is CONFIGURED only when the user has also connected it — see
/// <see cref="ProviderActivation"/>. Conflating the two meant a machine with unrelated tooling
/// installed arrived pre-wired to providers nobody had chosen.</para>
/// <para><b>Availability of the CLI and local providers is PROBED, never stored.</b> The setup
/// page's Detect button forces this check rather than writing a flag, because a stored flag would
/// go on claiming a CLI exists after the user uninstalled it. The llama-server probe is a network
/// call, so the whole check is async and runs off the UI thread.</para>
/// <para>Provider ids come from <see cref="ProviderCatalog"/> — the same vocabulary the credential
/// store, the bridge verbs and the setup page's <c>providers.ts</c> use.</para>
/// </remarks>
internal static class ProviderAvailability
{
    /// <summary>
    /// Reports every provider's state: whether it is available, from where, and whether the user has
    /// connected it.
    /// </summary>
    /// <remarks>
    /// Credentialed providers come from the resolver (cheap, and cached); the CLI and local-server
    /// providers are probed here. Order is catalog order for the first group then the probed ones;
    /// the UI re-sorts into its own display order.
    /// </remarks>
    /// <param name="client">Shared HTTP client for the llama-server probe.</param>
    /// <param name="ct">Cancellation token bounding the network probe.</param>
    /// <returns>One status per known provider.</returns>
    public static async Task<IReadOnlyList<ProviderStatus>> StatusesAsync(
        HttpClient client,
        CancellationToken ct)
    {
        // Credentialed providers: the resolver already knows what is available and what is connected.
        var statuses = new List<ProviderStatus>(PhyCredentials.Resolver.Statuses());

        // Probed providers: availability is a live check, so it is composed here rather than in Core,
        // which has no idea what a PATH or a llama-server is.
        foreach (ProviderInfo info in ProviderCatalog.All)
        {
            if (info.Auth != ProviderAuth.Detected)
            {
                continue;
            }

            bool present = string.Equals(info.Id, "local-llm", StringComparison.OrdinalIgnoreCase)
                ? await HasLlamaServerAsync(client, ct).ConfigureAwait(false)
                : IsDetected(info.Id);

            statuses.Add(new ProviderStatus(
                info.Id,
                PhyCredentials.Activation.IsActivated(info.Id),
                present ? ProviderSource.Detected : ProviderSource.None,
                null));
        }

        return statuses;
    }

    /// <summary>
    /// Returns the ids of every provider that is both available and connected — the set the chat
    /// window treats as configured, and whose emptiness triggers first-run setup.
    /// </summary>
    /// <param name="client">Shared HTTP client for the llama-server probe.</param>
    /// <param name="ct">Cancellation token bounding the network probe.</param>
    /// <returns>The ready providers' ids.</returns>
    public static async Task<IReadOnlyList<string>> ConfiguredProviderIdsAsync(
        HttpClient client,
        CancellationToken ct)
    {
        IReadOnlyList<ProviderStatus> statuses = await StatusesAsync(client, ct).ConfigureAwait(false);
        return statuses.Where(s => s.Ready).Select(s => s.Id).ToList();
    }

    /// <summary>
    /// Runs the availability check for one probed provider — the Detect button's whole job.
    /// </summary>
    /// <param name="providerId">"claude-code", "codex" or "local-llm".</param>
    /// <param name="client">Shared HTTP client for the llama-server probe.</param>
    /// <param name="ct">Cancellation token bounding the network probe.</param>
    /// <returns>True when the provider answered.</returns>
    public static async Task<bool> DetectAsync(string providerId, HttpClient client, CancellationToken ct)
    {
        if (string.Equals(providerId, "local-llm", StringComparison.OrdinalIgnoreCase))
        {
            return await HasLlamaServerAsync(client, ct).ConfigureAwait(false);
        }

        return IsDetected(providerId);
    }

    /// <summary>
    /// The configured-provider set as far as it can be known WITHOUT any probing — synchronously,
    /// on the calling thread.
    /// </summary>
    /// <remarks>
    /// <para>Exists so the chat window opens on the right screen. <c>needsSetup</c> is derived from
    /// this set, and until it had an answer the window showed the not-yet-setup case as though
    /// everything were fine — for however long the async probe took, which includes two PATH scans
    /// and a socket connect that waits out its timeout when nothing is listening. On a machine with
    /// no providers that is seconds of the wrong screen.</para>
    /// <para>Credentialed providers are exact: the resolver reads local files only. A CONNECTED
    /// probe-based provider is assumed present until the probe says otherwise — the opposite guess
    /// would flash the setup screen at someone who has Claude Code set up perfectly well.</para>
    /// </remarks>
    /// <returns>The provider ids to treat as configured until the first probe lands.</returns>
    public static IReadOnlyList<string> ConfiguredProviderIdsNow()
    {
        var ids = new List<string>();

        try
        {
            ids.AddRange(PhyCredentials.Resolver.ConfiguredIds(ProviderKind.Llm));
            ids.AddRange(PhyCredentials.Resolver.ConfiguredIds(ProviderKind.Tool));

            foreach (ProviderInfo info in ProviderCatalog.All)
            {
                if (info.Auth == ProviderAuth.Detected && PhyCredentials.Activation.IsActivated(info.Id))
                {
                    ids.Add(info.Id);
                }
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return ids;
    }

    // True when the named CLI is on PATH. Wrapped so a provider id, not a class, is what callers name.
    private static bool IsDetected(string providerId) => providerId.ToLowerInvariant() switch
    {
        "claude-code" => ClaudeCodeProvider.IsCliAvailable(),
        "codex" => CodexProvider.IsCliAvailable(),
        _ => false,
    };

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
