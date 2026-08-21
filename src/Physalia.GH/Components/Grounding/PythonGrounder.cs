// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Scaffold producer: emits a <see cref="PythonFunctionGrounding"/> describing a python function
/// so the System Prompt can ground the model with it. Wire its output into the System Prompt's Grounding
/// input.
/// </summary>
/// <remarks>
/// WIP: today the function is described by a hand-written signature and docstring. TODO: parse
/// the real signature and docstring from the function source.
/// </remarks>
public class PythonGrounder : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PythonGrounder"/> class.
    /// </summary>
    public PythonGrounder()
        : base("Python Grounding", "PyGnd", "Tells the model about a Python function it is allowed to call. Unfinished — a scaffold.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B8F3E0D5-2A47-4C9B-A1F6-3D0E5A9B4C72");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Signature", "S", "How the function is called, e.g. def foo(a, b) -> float.", GH_ParamAccess.item, string.Empty);
        int docIndex = pManager.AddTextParameter("Docstring", "D", "What the function is for, in enough detail that the model knows when to reach for it.", GH_ParamAccess.item, string.Empty);
        pManager[docIndex].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "The function described for the model. Wire into a Conversation Log's Grounding input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string signature = string.Empty;
        string docstring = string.Empty;
        DA.GetData(0, ref signature);
        DA.GetData(1, ref docstring);

        if (string.IsNullOrWhiteSpace(signature))
        {
            return;
        }

        DA.SetData(0, new GH_Grounding(new PythonFunctionGrounding(signature, docstring)));
    }
}
