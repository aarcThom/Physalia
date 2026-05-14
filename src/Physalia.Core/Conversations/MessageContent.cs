// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Conversations;

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
