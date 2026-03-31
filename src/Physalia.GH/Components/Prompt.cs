// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel;
using Physalia.GH.Attributes;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Physalia.GH.Components;

/// <summary>
/// The prompt class.
/// </summary>
public class Prompt : PhyBase
{
    // FIELDS ==========================================================================================

    // PROPERTIES =======================================================================================

    /// <summary>
    /// The text to be displayed in the prompt input box when the user is not currently entering text.
    /// </summary>
    public string UserPromptText = "Double click to enter prompt...";

    /// <summary>
    /// Gets the unique ID for this component. Do not change this ID after release.
    /// </summary>
    public override Guid ComponentGuid
    {
        get { return new Guid("2A8562D9-9866-461C-8A2D-2F7E4F40026B"); }
    }

    // CONSTRUCTOR =======================================================================================

    /// <summary>
    /// Initializes a new instance of the <see cref="Prompt"/> class.
    /// </summary>
    public Prompt()
        : base("Prompt", "Prmpt", "Prompt the LLM", "Core")
    {
    }

    // GH COMPONENT OVERRIDES ============================================================================================

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Registers all the output parameters for this component.
    /// </summary>
    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("prompt", "prmpt", "The prompt to be fed into Crest.", GH_ParamAccess.item);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
    }

    /// <summary>
    /// Assigns the custom <see cref="PromptAttrib"/> attribute class to this component.
    /// </summary>
    public override void CreateAttributes() => m_attributes = new PromptAttrib(this);
}