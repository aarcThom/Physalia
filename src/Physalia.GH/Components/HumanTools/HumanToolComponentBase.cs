// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel;
using Physalia.Core.HumanTools;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base class for human-tool components — the counterpart of <see cref="LlmToolComponentBase"/>.
/// A human tool is an affordance the human uses from the chat window (a geometry-snapshot button,
/// image attachments), enabled only while the component is wired into the Conversation Log's
/// Human Tools input. Every human tool is a passive emitter: no inputs, no signals, one
/// <see cref="Param_HumanTool"/> output re-emitting its <see cref="Tool"/> each solve. Human
/// tools never touch the system prompt and are never advertised to the model.
/// </summary>
public abstract class HumanToolComponentBase : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HumanToolComponentBase"/> class.
    /// </summary>
    /// <param name="name">The component display name.</param>
    /// <param name="nickname">The component nickname.</param>
    /// <param name="description">The component description.</param>
    protected HumanToolComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, "Human Tools")
    {
    }

    /// <summary>
    /// Gets the human tool this component enables in the chat window.
    /// </summary>
    protected abstract HumanTool Tool { get; }

    /// <inheritdoc/>
    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs: a human tool's presence on the Conversation Log's Human Tools input is the
        // whole contract; its behaviour lives in the chat window.
    }

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_HumanTool(), "Human Tool", "HT", "Human tool for the Conversation Log's Human Tools input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        DA.SetData(0, new GH_HumanTool(Tool));
    }
}
