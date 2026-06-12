// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Providers.OpenAiProtocol;

namespace Physalia.Core.Providers.Named;

/// <summary>
/// Provider for any endpoint that speaks the OpenAI Chat Completions wire format.
/// Covers OpenAI, OpenRouter, DeepSeek, Groq, Ollama, and llama.cpp.
/// Use with <see cref="Physalia.Core.Models.Named.OpenAICompatibleConfig"/>.
/// </summary>
public sealed class OpenAICompatibleProvider : OpenAIProtocolProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAICompatibleProvider"/> class.
    /// </summary>
    public OpenAICompatibleProvider()
    {
    }
}
