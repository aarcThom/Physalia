// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Providers;

/// <summary>
/// Ollama implementation of <see cref="OpenAiCompatibleProvider"/>.
/// Provides access to locally running models via Ollama's OpenAI-compatible API.
/// No API key is required — authentication is not used against a local server.
/// See: https://ollama.com/blog/openai-compatibility.
/// </summary>
internal class OllamaProvider : OpenAiCompatibleProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaProvider"/> class.
    /// </summary>
    /// <param name="apiKey">Unused — Ollama runs locally and requires no authentication. Pass any non-null value.</param>
    public OllamaProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the Ollama provider.
    /// </summary>
    public override string ProviderName => "ollama";

    /// <summary>
    /// Gets the maximum number of output tokens for Ollama API requests.
    /// </summary>
    public override int MaxTokens => 4096;

    /// <summary>
    /// Gets the Ollama local API base URL.
    /// </summary>
    protected override string BaseUrl => "http://localhost:11434/v1";
}
