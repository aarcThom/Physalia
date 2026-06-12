// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Providers.OpenAiProtocol;

namespace Physalia.Core.Providers.Named;

/// <summary>
/// Provider for a local <c>llama-server</c> (llama.cpp) instance.
/// Connects to a single-model HTTP server started with:
/// <c>llama-server -m model.gguf --port 8080</c>
/// </summary>
/// <remarks>
/// The full OpenAI Chat Completions wire format is handled by the base class.
/// No overrides are needed — llama-server accepts the standard request body as-is.
/// Use <see cref="Physalia.Core.Models.Named.LlamaCppConfig"/> to configure the endpoint.
/// </remarks>
public sealed class LlamaCppProvider : OpenAIProtocolProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaCppProvider"/> class.
    /// </summary>
    public LlamaCppProvider()
    {
    }
}
