// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="MemoryGrounding"/> that tells the model it has a persistent memory (the
/// <c>memory</c> tool) and nudges it to consult and maintain that memory. Wire its output into the
/// Recorder's Grounding input to turn the feature on: when this grounding is wired, the model is
/// informed of its memory and the chat input offers the <c>/m/global</c> and <c>/m/local</c>
/// references; when it is not wired, the model is told nothing about memory at all.
///
/// <para>Pair it with a <see cref="MemoryTool"/> node wired into a Router so the model can actually
/// call the memory operations the grounding advertises. Has no inputs.</para>
/// </summary>
public class MemoryGrounder : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryGrounder"/> class.
    /// </summary>
    public MemoryGrounder()
        : base("Memory Grounding", "MemGnd", "Grounds the model with its persistent memory (the memory tool). Wire into a Recorder's Grounding input.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("1D8F5A63-4B27-4E09-8C14-6A2F0B9D7E35");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs — this grounding is a switch: wired means the model knows about its memory.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "Memory grounding for the Recorder.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        DA.SetData(0, new GH_Grounding(new MemoryGrounding()));
    }
}
