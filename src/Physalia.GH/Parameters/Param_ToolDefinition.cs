// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_ToolDefinition"/> values from tool
/// nodes into the LLM Call's Tools input.
/// </summary>
public class Param_ToolDefinition : PhyParam<GH_ToolDefinition>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ToolDefinition"/> class.
    /// </summary>
    public Param_ToolDefinition()
        : base("Tool Definition", "Tool", "A tool advertised to the model (name, description, argument schema).")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A2D4F6B8-1C3E-4A5D-9B7F-0E2C4A6D8B13");
}
