// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Physalia.Core.Common;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Bundles a conversation history with its system prompt (and the tools advertised to the model)
/// for a single inference call. Providers are responsible for serializing each field into the wire
/// format their API expects.
/// </summary>
/// <param name="SystemPrompt">The system prompt passed at call time. May be empty.</param>
/// <param name="Conversation">The accumulated message history.</param>
public record Instructions(string SystemPrompt, Conversation Conversation)
{
    /// <summary>
    /// Gets the tool definitions advertised to the model for this call, or an empty list for plain
    /// inference. Rides on the Instructions so the full inference context is one object: the Conversation Log
    /// folds the tools wired in as grounding (via the Tools Present grounder) into this field, and a
    /// compaction component carries it forward unchanged, so the LLM Call reads its tools here rather
    /// than from a separate input.
    /// </summary>
    public IReadOnlyList<LlmToolDefinition> Tools { get; init; } = Array.Empty<LlmToolDefinition>();
}
