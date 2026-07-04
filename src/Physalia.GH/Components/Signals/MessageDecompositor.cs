// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Decomposes a <see cref="ConversationMessage"/> into its role and content strings.
/// </summary>
public class MessageDecompositor : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageDecompositor"/> class.
    /// </summary>
    public MessageDecompositor()
        : base("Message Decompositor", "MDecomp", "Breaks a ConversationMessage into its role and content strings.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.septenary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("03967E63-69B2-40A7-90A9-C15C6EAA40DA");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ConversationMessage(), "Message", "M", "The conversation message to decompose.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Role", "R", "The role of this turn: User or Assistant.", GH_ParamAccess.item);
        pManager.AddTextParameter("Content", "C", "The content of this turn rendered as a display string.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var goo = new GH_ConversationMessage();
        if (!DA.GetData(0, ref goo)) return;

        if (goo.Value is not ConversationMessage message)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid ConversationMessage input.");
            return;
        }

        DA.SetData(0, message.Role.ToString());
        DA.SetData(1, ConversationHelpers.ToContentString(message));
    }
}
