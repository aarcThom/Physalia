// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;
using Physalia.Core.Common;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a single <see cref="LlmToolCall"/> requested by the model.
/// </summary>
public class GH_LlmToolCall : GH_Goo<LlmToolCall>
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
    {
        Value = toolCall;
    }

    /// <inheritdoc/>
    public override bool IsValid => Value != null;

    /// <inheritdoc/>
    public override string TypeName => "LlmToolCall";

    /// <inheritdoc/>
    public override string TypeDescription => "A tool call requested by the LLM.";

    /// <inheritdoc/>
    public override IGH_Goo Duplicate() => new GH_LlmToolCall(Value);

    /// <inheritdoc/>
    public override string ToString() =>
        Value == null ? "null" : $"[Tool: {Value.Name} (id:{Value.Id})]";
}
