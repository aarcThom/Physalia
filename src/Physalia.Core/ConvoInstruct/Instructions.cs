// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Bundles a conversation history with its system prompt for a single inference call.
/// Providers are responsible for serializing each field into the wire format their API expects.
/// </summary>
/// <param name="SystemPrompt">The system prompt passed at call time. May be empty.</param>
/// <param name="Conversation">The accumulated message history.</param>
public record Instructions(string SystemPrompt, Conversation Conversation);
