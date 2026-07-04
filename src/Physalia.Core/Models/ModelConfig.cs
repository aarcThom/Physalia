// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Models;

/// <summary>
/// Base configuration record for any LLM provider.
/// Carries the parameters shared across all providers and protocols.
/// </summary>
/// <param name="ModelId">The provider's model identifier string, e.g. "claude-sonnet-4-6".</param>
/// <param name="ApiKey">The resolved API key for authentication.</param>
/// <param name="MaxTokens">Maximum number of tokens to generate.</param>
public abstract record ModelConfig(string ModelId, string ApiKey, int MaxTokens)
{
    /// <summary>
    /// Gets an optional caller-supplied identity used by stateful providers to pool a
    /// long-lived session across calls. The Claude Code warm-process provider keys its
    /// persistent CLI process on this (the LLM Call stamps its <c>InstanceGuid</c>).
    /// Null for stateless providers, which ignore it.
    /// </summary>
    public Guid? SessionKey { get; init; }
}
