// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Models.Named;

/// <summary>
/// Configuration for the Anthropic API.
/// </summary>
/// <param name="ModelId">The model identifier, e.g. "claude-sonnet-4-6".</param>
/// <param name="ApiKey">The Anthropic API key.</param>
/// <param name="MaxTokens">Maximum tokens to generate. Defaults to 4096.</param>
/// <param name="BaseUrl">API base URL. Defaults to "https://api.anthropic.com/v1".</param>
/// <param name="Temperature">Sampling temperature in range 0.0–1.0. Defaults to 1.0.</param>
/// <param name="TopP">Nucleus sampling threshold. Defaults to 1.0.</param>
/// <param name="TopK">Top-K sampling. Set to 0 to use provider default.</param>
public record AnthropicConfig(
    string ModelId,
    string ApiKey,
    int MaxTokens = 4096,
    string BaseUrl = "https://api.anthropic.com/v1",
    float Temperature = 1.0f,
    float TopP = 1.0f,
    int TopK = 0)
    : AnthropicProtocolConfig(ModelId, ApiKey, MaxTokens, BaseUrl, Temperature, TopP, TopK);
