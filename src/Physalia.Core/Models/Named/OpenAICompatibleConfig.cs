// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Models.Named;

/// <summary>
/// Configuration for any provider that uses the OpenAI Chat Completions wire format.
/// Covers OpenAI, OpenRouter, DeepSeek, Groq, Ollama, and llama.cpp.
/// Set <see cref="OpenAIProtocolConfig.BaseUrl"/> to the provider's endpoint.
/// </summary>
/// <param name="ModelId">The provider's model identifier string.</param>
/// <param name="ApiKey">The API key for authentication. Leave empty for local servers.</param>
/// <param name="MaxTokens">Maximum number of tokens to generate.</param>
/// <param name="BaseUrl">
/// The base URL for the provider's API endpoint.
/// Defaults to <c>https://api.openai.com/v1</c>.
/// </param>
/// <param name="Temperature">Sampling temperature in the range 0.0–2.0.</param>
/// <param name="TopP">Nucleus sampling threshold in the range 0.0–1.0.</param>
public record OpenAICompatibleConfig(
    string ModelId = "gpt-4o",
    string ApiKey = "",
    int MaxTokens = 4096,
    string BaseUrl = "https://api.openai.com/v1",
    float Temperature = 1.0f,
    float TopP = 1.0f)
    : OpenAIProtocolConfig(ModelId, ApiKey, MaxTokens, BaseUrl, Temperature, TopP);
