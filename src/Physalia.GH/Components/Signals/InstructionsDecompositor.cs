// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Decomposes an <see cref="Instructions"/> value into its constituent system prompt and message list.
/// </summary>
public class InstructionsDecompositor : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InstructionsDecompositor"/> class.
    /// </summary>
    public InstructionsDecompositor()
        : base("Instructions Decompositor", "IDecomp", "Breaks Instructions into a system prompt string and a list of ConversationMessages.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("1BB95700-FBC4-4025-9242-6C7FE22DB9D8");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "Instructions to decompose.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("System Prompt", "S", "The system prompt string.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_ConversationMessage(), "Messages", "M", "The ordered list of conversation turns.", GH_ParamAccess.list);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var goo = new GH_Instructions();
        if (!DA.GetData(0, ref goo)) return;

        if (goo.Value is not Instructions instructions)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid Instructions input.");
            return;
        }

        DA.SetData(0, instructions.SystemPrompt);
        DA.SetDataList(1, instructions.Conversation.Messages.Select(m => new GH_ConversationMessage(m)));
    }
}
