// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models;

namespace Physalia.Core.Models.Protocol;

/// <summary>
/// Configuration for any provider that speaks the Anthropic Messages wire format.
/// </summary>
/// <param name="ModelId">The provider's model identifier string.</param>
/// <param name="ApiKey">The resolved API key for authentication.</param>
/// <param name="MaxTokens">
/// Maximum number of tokens to generate. Required by the Anthropic API — a default
/// is always injected if this is zero.
/// </param>
/// <param name="BaseUrl">
/// The base URL for the provider's API endpoint, e.g. "https://api.anthropic.com/v1".
/// </param>
/// <param name="Temperature">
/// Sampling temperature. Anthropic range is 0.0–1.0. Values above 1.0 are clamped on intake.
/// </param>
/// <param name="TopP">Nucleus sampling threshold in the range 0.0–1.0.</param>
/// <param name="TopK">
/// Limits the sampling pool to the top-K tokens before nucleus sampling is applied.
/// Set to 0 to omit from the request (provider default applies).
/// </param>
public abstract record AnthropicProtocolConfig(
    string ModelId,
    string ApiKey,
    int MaxTokens,
    string BaseUrl,
    float Temperature,
    float TopP,
    int TopK)
    : ModelConfig(ModelId, ApiKey, MaxTokens);
