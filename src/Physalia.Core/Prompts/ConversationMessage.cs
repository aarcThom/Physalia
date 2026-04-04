// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Prompts;

/// <summary>
/// Represents a single message in a multi-turn LLM conversation.
/// </summary>
/// <param name="Role">The role of the message sender — either "user" or "assistant".</param>
/// <param name="Content">The text content of the message.</param>
/// <param name="StatusMessage">An optional short human-readable summary shown in the conversation history UI instead of the raw content.</param>
public record ConversationMessage(string Role, string Content, string? StatusMessage = null);
