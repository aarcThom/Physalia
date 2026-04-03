// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;

namespace Physalia.GH.ParamTypes;

/// <summary>
/// Grasshopper parameter type that carries a <see cref="ConversationGoo"/> between components on the canvas.
/// </summary>
public class ConversationGhParam : GH_Param<ConversationGoo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationGhParam"/> class.
    /// </summary>
    public ConversationGhParam()
        : base("Conversation", "Conv", "A multi-turn LLM conversation history",
               "Physalia", "Core", GH_ParamAccess.item)
    { }

    /// <summary>
    /// Gets the unique identifier for this parameter type.
    /// </summary>
    public override Guid ComponentGuid => new("36D10652-B361-4FEF-91D2-38825E782884");

    /// <summary>
    /// Gets the icon bitmap for this parameter type.
    /// </summary>
    protected override System.Drawing.Bitmap Icon => null;

    /// <summary>
    /// Gets the exposure level that controls where this parameter appears in the GH component palette.
    /// </summary>
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}
