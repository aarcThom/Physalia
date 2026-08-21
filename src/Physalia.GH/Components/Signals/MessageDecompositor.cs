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
        : base("Message Decompositor", "MDecomp", "Splits one conversation turn back into who spoke and what they said.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("03967E63-69B2-40A7-90A9-C15C6EAA40DA");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ConversationMessage(), "Message", "M", "The turn to open up.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Role", "R", "Who spoke: User or Assistant.", GH_ParamAccess.item);
        pManager.AddTextParameter("Content", "C", "What was said, flattened to plain text. Images and tool traffic are described rather than shown.", GH_ParamAccess.item);
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
