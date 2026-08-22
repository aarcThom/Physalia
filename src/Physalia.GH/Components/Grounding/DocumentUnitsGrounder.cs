// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="DocumentUnitsGrounding"/> describing the active Rhino document's unit system,
/// so the model produces numeric values and geometry consistent with the document's units. Wire its
/// output into the Conversation Log's Grounding input; the chat window then shows the current units and lets
/// the user override the value handed to the model (the override never changes the document). Has no
/// inputs — it reads the active document's units on every solve.
/// </summary>
public class DocumentUnitsGrounder : PhyBase
{
    // The unit text handed to the model instead of the document's own. Null = use the live document
    // units (the default). The document is NEVER changed either way — this only rewrites what the
    // model is told, which is how you make a model reason in metres about a millimetre file. Edited
    // from the chat window's units pill, which reaches it through the Conversation Log; it lives HERE
    // so it travels with the component and ships inside a preset.
    private string? _override;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentUnitsGrounder"/> class.
    /// </summary>
    public DocumentUnitsGrounder()
        : base("Document Units Grounding", "UnGnd", "Tells the model what one unit means in this document, so a wall 3000 long does not come out 3000 metres tall.", "Grounding")
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
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "This document's unit system, spelled out for the model. Wire into a Conversation Log's Grounding input.", GH_ParamAccess.item);
    }

    /// <summary>
    /// Gets the unit text handed to the model in place of the document's own, or null when the live
    /// document units are used.
    /// </summary>
    public string? Override => _override;

    /// <summary>
    /// Sets the unit text handed to the model, or clears it back to the document's own units. Called
    /// from the chat window (through the Conversation Log) on the UI thread; does not re-solve,
    /// because the grounding on the wire still carries the document's real units — the substitution
    /// happens where the prompt is assembled, on the Conversation Log's own next solve.
    /// </summary>
    /// <param name="units">The override unit text, or null (or blank) to use the live document units.</param>
    public void SetOverride(string? units) =>
        _override = string.IsNullOrWhiteSpace(units) ? null : units;

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        SettingArchive.WriteOptionalString(writer, "UnitsOverride", _override);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _override = SettingArchive.ReadOptionalString(reader, "UnitsOverride");
        return base.Read(reader);
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
