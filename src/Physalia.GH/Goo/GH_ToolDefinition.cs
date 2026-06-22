// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using GH_IO.Serialization;
using Physalia.Core.Common;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapping a <see cref="ToolDefinition"/> — a tool a tool node advertises to the
/// model. Tool nodes output it; the Reasoner collects the wired definitions and sends them to the
/// provider so the model can call the tool.
/// </summary>
public class GH_ToolDefinition : PhyGoo<GH_ToolDefinition, ToolDefinition>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ToolDefinition"/> class with no value.
    /// </summary>
    public GH_ToolDefinition()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ToolDefinition"/> class wrapping the given definition.
    /// </summary>
    /// <param name="definition">The tool definition to wrap.</param>
    public GH_ToolDefinition(ToolDefinition definition)
        : base(definition)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "Tool Definition";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "A tool advertised to the model: its name, a description of when to use it, and a JSON Schema for its arguments.";

    /// <inheritdoc/>
    public override string ToString() =>
        Value is null ? "(empty tool definition)" : $"Tool: {Value.Name}";

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: a tool node re-emits its definition on every solve, so the goo stores nothing.</remarks>
    public override bool Write(GH_IWriter writer) => true;

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: a tool node re-emits its definition on every solve, so the goo stores nothing.</remarks>
    public override bool Read(GH_IReader reader) => true;
}
