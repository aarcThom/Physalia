// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models;

namespace Physalia.Core.Models.Protocol;

/// <summary>
/// Configuration for any provider that speaks the OpenAI Chat Completions wire format.
/// Covers OpenAI, DeepSeek, Ollama, OpenRouter, and Groq.
/// </summary>
/// <param name="ModelId">The provider's model identifier string.</param>
/// <param name="ApiKey">The resolved API key for authentication.</param>
/// <param name="MaxTokens">Maximum number of tokens to generate.</param>
/// <param name="BaseUrl">
/// The base URL for the provider's API endpoint, e.g. "https://api.openai.com/v1".
/// </param>
/// <param name="Temperature">
/// Sampling temperature in the range 0.0–2.0.
/// </param>
/// <param name="TopP">
/// Nucleus sampling threshold in the range 0.0–1.0.
/// </param>
public abstract record OpenAIProtocolConfig(
    string ModelId,
    string ApiKey,
    int MaxTokens,
    string BaseUrl,
    float Temperature,
    float TopP)
    : ModelConfig(ModelId, ApiKey, MaxTokens);
