// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Models.Named;

/// <summary>
/// Configuration for the Google Gemini API.
/// </summary>
/// <param name="ModelId">The model identifier, e.g. "gemini-2.0-flash".</param>
/// <param name="ApiKey">The Google API key.</param>
/// <param name="MaxTokens">Maximum tokens to generate. Defaults to 8192.</param>
/// <param name="BaseUrl">API base URL. Defaults to "https://generativelanguage.googleapis.com/v1beta".</param>
/// <param name="Temperature">Sampling temperature in range 0.0–2.0. Defaults to 1.0.</param>
/// <param name="TopP">Nucleus sampling threshold. Defaults to 0.95.</param>
/// <param name="TopK">Top-K sampling pool size. Set to 0 to use the provider default.</param>
/// <param name="ThinkingBudget">
/// Gemini <c>thinkingConfig.thinkingBudget</c>. Null (the default) applies the model's
/// known behaviour from <see cref="Physalia.Core.Models.Defaults.GeminiModelDefaults"/>;
/// 0 disables thinking where supported; -1 is dynamic; positive values cap thinking tokens.
/// </param>
public record GeminiConfig(
    string ModelId,
    string ApiKey,
    int MaxTokens = 8192,
    string BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
    float Temperature = 1.0f,
    float TopP = 0.95f,
    int TopK = 0,
    int? ThinkingBudget = null)
    : GeminiProtocolConfig(ModelId, ApiKey, MaxTokens, BaseUrl, Temperature, TopP, TopK, ThinkingBudget);
