// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models;

namespace Physalia.Core.Models.Protocol;

/// <summary>
/// Configuration for any provider that speaks the Gemini GenerateContent wire format.
/// </summary>
/// <param name="ModelId">The Gemini model identifier, e.g. "gemini-2.0-flash".</param>
/// <param name="ApiKey">The Google API key passed as a query parameter.</param>
/// <param name="MaxTokens">Maximum number of tokens to generate.</param>
/// <param name="BaseUrl">
/// Base URL for the Gemini API endpoint.
/// Defaults to "https://generativelanguage.googleapis.com/v1beta".
/// </param>
/// <param name="Temperature">Sampling temperature in the range 0.0–2.0.</param>
/// <param name="TopP">Nucleus sampling threshold in the range 0.0–1.0.</param>
/// <param name="TopK">
/// Limits the sampling pool to the top-K tokens before nucleus sampling is applied.
/// Set to 0 to omit from the request (provider default applies).
/// </param>
public abstract record GeminiProtocolConfig(
    string ModelId,
    string ApiKey,
    int MaxTokens,
    string BaseUrl,
    float Temperature,
    float TopP,
    int TopK)
    : ModelConfig(ModelId, ApiKey, MaxTokens);
