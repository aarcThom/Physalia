// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_LlmToolDefinition"/> values from tool
/// nodes into the LLM Call's Tools input.
/// </summary>
public class Param_LlmToolDefinition : PhyParam<GH_LlmToolDefinition>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_LlmToolDefinition"/> class.
    /// </summary>
    public Param_LlmToolDefinition()
        : base("Tool Definition", "Tool", "A tool offered to the model: what it is called, what it does, and the arguments it takes.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A2D4F6B8-1C3E-4A5D-9B7F-0E2C4A6D8B13");
}
