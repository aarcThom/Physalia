// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a single <see cref="LlmToolCall"/> requested by the model.
/// </summary>
public class GH_LlmToolCall : PhyGoo<GH_LlmToolCall, LlmToolCall>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_LlmToolCall"/> class with a null value.
    /// </summary>
    public GH_LlmToolCall()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_LlmToolCall"/> class wrapping the given tool call.
    /// </summary>
    /// <param name="toolCall">The tool call to wrap.</param>
    public GH_LlmToolCall(LlmToolCall toolCall)
        : base(toolCall)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "LlmToolCall";

    /// <inheritdoc/>
    public override string TypeDescription => "A tool call requested by the LLM.";

    /// <inheritdoc/>
    public override string ToString() =>
        Value == null ? "null" : $"[Tool: {Value.Name} (id:{Value.Id})]";
}
