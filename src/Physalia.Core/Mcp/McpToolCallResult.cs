// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Mcp;

/// <summary>
/// What a server returned from <c>tools/call</c>, already reduced to the shape a Physalia tool node
/// hands back.
/// </summary>
/// <param name="Text">
/// The textual result body. MCP allows several content blocks; the text ones are joined here,
/// because a tool result is text on every LLM provider.
/// </param>
/// <param name="Attachments">
/// Non-text blocks the answer came with — images, in practice. These ride the same answering user
/// turn as sibling blocks, after every tool_result, exactly as Take Snapshot's picture does.
/// </param>
/// <param name="IsError">
/// True when the server reported the call as failed. This is a tool-level failure reported back to
/// the model, not a transport failure.
/// </param>
public sealed record McpToolCallResult(
    string Text,
    IReadOnlyList<MessageContent> Attachments,
    bool IsError);
