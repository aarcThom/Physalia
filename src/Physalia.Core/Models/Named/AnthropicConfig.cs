// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Models.Named;

/// <summary>
/// Configuration for the Anthropic API.
/// </summary>
/// <param name="ModelId">The model identifier, e.g. "claude-sonnet-4-6".</param>
/// <param name="ApiKey">The Anthropic API key.</param>
/// <param name="MaxTokens">
/// Maximum tokens to generate. Thinking and answer share this budget, so the default is a
/// generous 32768 — 8192 truncated real responses mid-document once adaptive thinking ran.
/// </param>
/// <param name="BaseUrl">API base URL. Defaults to "https://api.anthropic.com/v1".</param>
/// <param name="Temperature">Sampling temperature in range 0.0–1.0. Defaults to 1.0.</param>
/// <param name="TopP">Nucleus sampling threshold. Defaults to 1.0.</param>
/// <param name="TopK">Top-K sampling. Set to 0 to use provider default.</param>
/// <param name="ThinkingBudget">
/// Extended thinking control. Null (the default) applies the model's known behaviour
/// from <see cref="Physalia.Core.Models.Defaults.AnthropicModelDefaults"/>; 0 explicitly
/// disables thinking; -1 requests adaptive thinking with summarized display; positive
/// values set a manual budget (raised to the 1024 API minimum).
/// </param>
public record AnthropicConfig(
    string ModelId,
    string ApiKey,
    int MaxTokens = 32768,
    string BaseUrl = "https://api.anthropic.com/v1",
    float Temperature = 1.0f,
    float TopP = 1.0f,
    int TopK = 0,
    int? ThinkingBudget = null)
    : AnthropicProtocolConfig(ModelId, ApiKey, MaxTokens, BaseUrl, Temperature, TopP, TopK, ThinkingBudget);
