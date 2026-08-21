// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Composes an ordered list of <see cref="ConversationMessage"/> values into a <see cref="Conversation"/>.
/// </summary>
public class ConversationCompositor : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationCompositor"/> class.
    /// </summary>
    public ConversationCompositor()
        : base("Conversation Compositor", "CConv", "Builds a conversation out of separate turns, for assembling context by hand instead of letting a Conversation Log gather it.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("0946674C-7711-4843-A7A3-D6E0BCECD550");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ConversationMessage(), "Messages", "M", "The turns in order. They have to alternate between User and Assistant — two of the same in a row is rejected.", GH_ParamAccess.list);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Conversation(), "Conversation", "C", "The turns assembled into one conversation.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var messageGoos = new List<GH_ConversationMessage>();
        if (!DA.GetDataList(0, messageGoos)) return;

        var conversation = Conversation.Empty;

        foreach (var goo in messageGoos)
        {
            if (goo?.Value is not ConversationMessage message)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Skipping null or invalid message.");
                continue;
            }

            try
            {
                conversation = conversation.Append(message);
            }
            catch (InvalidOperationException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }
        }

        DA.SetData(0, new GH_Conversation(conversation));
    }
}
