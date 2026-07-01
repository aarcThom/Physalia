// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="DocumentUnitsGrounding"/> describing the active Rhino document's unit system,
/// so the model produces numeric values and geometry consistent with the document's units. Wire its
/// output into the Recorder's Grounding input; the chat window then shows the current units and lets
/// the user override the value handed to the model (the override never changes the document). Has no
/// inputs — it reads the active document's units on every solve.
/// </summary>
public class DocumentUnitsGrounder : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentUnitsGrounder"/> class.
    /// </summary>
    public DocumentUnitsGrounder()
        : base("Document Units Grounding", "UnGnd", "Grounds the model with the active Rhino document's unit system.", "Resources")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6F1B3E82-9C4A-4D57-8E2B-1A7D5C0F9B34");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs: the unit system is read from the active Rhino document.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "Document-units grounding for the Recorder.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        DA.SetData(0, new GH_Grounding(new DocumentUnitsGrounding(ReadDocumentUnits())));
    }

    // Reads the active document's unit-system display name. None/Unset map to an empty string, which
    // makes the grounding contribute nothing (the assembler drops empty sections).
    private static string ReadDocumentUnits()
    {
        Rhino.UnitSystem units = Rhino.RhinoDoc.ActiveDoc?.ModelUnitSystem ?? Rhino.UnitSystem.None;
        return units is Rhino.UnitSystem.None or Rhino.UnitSystem.Unset
            ? string.Empty
            : units.ToString();
    }
}
