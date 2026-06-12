// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// A tool call requested by the model in a single inference response.
/// Kept in <c>Common</c> so <see cref="LlmResponseChunk"/> can reference it
/// without creating a dependency on the ConvoInstruct layer.
/// </summary>
/// <param name="Id">Provider-assigned call identifier. Must be echoed in the matching tool result.</param>
/// <param name="Name">The name of the tool the model wants to invoke.</param>
/// <param name="InputJson">The tool's input arguments as a JSON string.</param>
public record LlmToolCall(string Id, string Name, string InputJson);
