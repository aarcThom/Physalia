// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_HumanTool"/> values from human-tool
/// components into the Conversation Log's Human Tools input.
/// </summary>
public class Param_HumanTool : PhyParam<GH_HumanTool>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_HumanTool"/> class.
    /// </summary>
    public Param_HumanTool()
        : base("Human Tool", "HT", "An affordance for the human in the chat window (geometry snapshot, image attachments).")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("8C5E2D71-4A9B-4F36-B1E8-6D0A3C7F9254");
}
