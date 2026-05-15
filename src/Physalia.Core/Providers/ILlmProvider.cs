// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;

namespace Physalia.Core.Providers;

/// <summary>
/// Common contract for all LLM providers.
/// Implemented by each protocol base class so the factory and Reasoner
/// can work against a single type regardless of wire format.
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Streams an inference call and yields response chunks as they arrive.
    /// </summary>
    /// <param name="conversation">The conversation history to send.</param>
    /// <param name="systemPrompt">The system prompt, passed at call time.</param>
    /// <param name="config">Provider configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of result chunks.</returns>
    IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
        Conversation conversation,
        string systemPrompt,
        ModelConfig config,
        CancellationToken ct);

    /// <summary>
    /// Returns the list of model IDs available for this provider.
    /// </summary>
    /// <param name="config">Provider configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of model ID strings, or an error.</returns>
    Task<Result<IReadOnlyList<string>, LlmError>> GetAvailableModelsAsync(
        ModelConfig config,
        CancellationToken ct);
}
