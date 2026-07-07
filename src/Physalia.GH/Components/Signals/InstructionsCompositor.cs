// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Composes a <see cref="Conversation"/> and a system prompt string into an <see cref="Instructions"/> value.
/// </summary>
public class InstructionsCompositor : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InstructionsCompositor"/> class.
    /// </summary>
    public InstructionsCompositor()
        : base("Instructions Compositor", "IComp", "Composes a Conversation and a system prompt into an Instructions value.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("9D5E5E22-1C58-4771-BC2E-AAFDF1387505");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Conversation(), "Conversation", "C", "The conversation history.", GH_ParamAccess.item);
        pManager.AddTextParameter("System Prompt", "S", "The system prompt string.", GH_ParamAccess.item, string.Empty);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "The assembled Instructions for inference.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var conversationGoo = new GH_Conversation();
        string systemPrompt = string.Empty;

        if (!DA.GetData(0, ref conversationGoo)) return;
        DA.GetData(1, ref systemPrompt);

        if (conversationGoo.Value is not Conversation conversation)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid Conversation input.");
            return;
        }

        DA.SetData(0, new GH_Instructions(new Instructions(systemPrompt, conversation)));
    }
}
