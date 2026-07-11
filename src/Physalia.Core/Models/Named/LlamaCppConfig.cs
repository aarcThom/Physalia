// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Models.Named;

/// <summary>
/// Configuration for a local llama.cpp server (<c>llama-server</c>).
/// Start the server with: <c>llama-server -m model.gguf --port 8080</c>
/// </summary>
/// <remarks>
/// <para>
/// The server loads a single model at startup. <see cref="Physalia.Core.Models.ModelConfig.ModelId"/>
/// is sent in the request body as required by the OpenAI wire format but is ignored by
/// llama-server — the loaded model is always used regardless of what is specified here.
/// </para>
/// <para>
/// API key authentication is disabled by default. If the server was started with
/// <c>--api-key</c>, supply the matching key; otherwise leave <see cref="Physalia.Core.Models.ModelConfig.ApiKey"/>
/// as an empty string.
/// </para>
/// </remarks>
/// <param name="ModelId">
/// Identifier sent in the request body. Ignored by llama-server but required by the wire format.
/// Defaults to "local" as a neutral placeholder.
/// </param>
/// <param name="ApiKey">
/// API key. Leave empty unless the server was started with <c>--api-key</c>.
/// </param>
/// <param name="MaxTokens">Maximum number of tokens to generate.</param>
/// <param name="BaseUrl">
/// Base URL of the llama-server instance. Defaults to <c>http://127.0.0.1:8080/v1</c>.
/// </param>
/// <param name="Temperature">Sampling temperature. llama.cpp default is 0.8.</param>
/// <param name="TopP">Nucleus sampling threshold.</param>
/// <param name="ReasoningEffort">
/// Reasoning effort passthrough. Null (the default) omits the field — llama-server
/// ignores or rejects it depending on the loaded model.
/// </param>
public record LlamaCppConfig(
    string ModelId = "local",
    string ApiKey = "",
    int MaxTokens = 2048,
    string BaseUrl = "http://127.0.0.1:8080/v1",
    float Temperature = 0.8f,
    float TopP = 1.0f,
    string? ReasoningEffort = null)
    : OpenAIProtocolConfig(ModelId, ApiKey, MaxTokens, BaseUrl, Temperature, TopP, ReasoningEffort);
