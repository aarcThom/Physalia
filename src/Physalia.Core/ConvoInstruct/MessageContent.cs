// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// A single content block within a conversation message.
/// A message may carry multiple blocks (e.g. text + image together).
/// </summary>
public abstract record MessageContent;

/// <summary>
/// A plain-text content block.
/// </summary>
/// <param name="Text">The text of the content block.</param>
public record TextContent(string Text) : MessageContent;

/// <summary>
/// An image content block.
/// </summary>
/// <param name="Source">Describes where the image bytes come from.</param>
public record ImageContent(ImageSource Source) : MessageContent;

/// <summary>
/// A tool-call request block, produced by the assistant when it wants to invoke a tool.
/// Maps to Anthropic <c>tool_use</c> content blocks and OpenAI <c>tool_calls</c> entries.
/// </summary>
/// <param name="Id">Provider-assigned call identifier, echoed back in the matching <see cref="ToolResultContent"/>.</param>
/// <param name="Name">The name of the tool to invoke — matches the name declared in the system prompt.</param>
/// <param name="InputJson">The tool's input arguments as a JSON string.</param>
public record ToolCallContent(string Id, string Name, string InputJson) : MessageContent;

/// <summary>
/// A tool result block, produced by Physalia after executing the requested tool.
/// Must appear in a User-role message whose <see cref="ToolCallContent.Id"/> it echoes.
/// </summary>
/// <param name="ToolCallId">The <see cref="ToolCallContent.Id"/> this result corresponds to.</param>
/// <param name="Content">The tool output, typically a JSON string of the cluster's output values.</param>
/// <param name="IsError">True when the tool execution failed; the LLM uses this to self-correct.</param>
public record ToolResultContent(string ToolCallId, string Content, bool IsError = false) : MessageContent;
