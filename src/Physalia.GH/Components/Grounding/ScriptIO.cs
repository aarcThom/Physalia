// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Attributes;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Grounds the model with the exact input/output interface of the script component a linked
/// transmitter drives — every input's name, type hint, and item/list/tree access, and every
/// output's name and access — and declares that interface locked. While this component is linked
/// (and not disabled), the transmitter enforces the contract: it pushes code only, never restructures
/// the target's parameters (so existing wires survive every push), and rejects a submission that
/// departs from the locked set, routing corrective feedback back to the model. Drag the bottom grip
/// onto any script transmitter — <see cref="PyTransmitter"/> or <see cref="CsTransmitter"/> — and
/// wire the Grounding output into a Conversation Log's Grounding input so the model knows the
/// contract before it generates.
///
/// <para>The lock itself is language-neutral: a parameter set is a parameter set. What the model is
/// TOLD about it is not, so the wording comes from the linked transmitter's
/// <see cref="ScriptTransmitterBase.Dialect"/> rather than from anything here.</para>
/// </summary>
public class ScriptIO : PhyBase, IGuidLinked
{
    private const int OutGrounding = 0;

    private Guid _linkedGuid = Guid.Empty;
    private string _lastSignature = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptIO"/> class.
    /// </summary>
    public ScriptIO()
        : base(
            "Script I/O",
            "Script I/O",
            "Grounds the model with the linked transmitter's target inputs/outputs and locks them: the transmitter pushes code only and rejects submissions that change the interface. Drag the bottom grip to a Py or C# Transmitter; wire the output into a Conversation Log's Grounding input.",
            "Grounding")
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Pinned across the 2026-08-11 rename from "Interface Lock" — Grasshopper serializes by
    /// ComponentGuid, so changing it would orphan the component in every saved file.
    /// </remarks>
    public override Guid ComponentGuid => new Guid("B7D2F4A9-6C1E-4E8B-9A3D-2F5C8E7B0A46");

    /// <summary>
    /// Gets the InstanceGuid of the linked script transmitter, or <see cref="Guid.Empty"/> if unlinked.
    /// </summary>
    public Guid LinkedGuid => _linkedGuid;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new ScriptIOAttrib(this);
    }

    /// <summary>
    /// Links this lock to a script transmitter.
    /// Called by <see cref="ScriptIOAttrib"/> when the user drops the wire.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the transmitter to link.</param>
    public void LinkTo(Guid guid)
    {
        _linkedGuid = guid;
    }

    /// <summary>
    /// Removes the current link, releasing the transmitter from the interface contract.
    /// Called by <see cref="ScriptIOAttrib"/> when the user Ctrl+drops the wire.
    /// </summary>
    public void Unlink()
    {
        _linkedGuid = Guid.Empty;
    }

    /// <summary>
    /// Reports whether this lock is actively constraining the given transmitter: it is linked to
    /// it and not disabled. A disabled (locked-in-the-GH-sense) component keeps its link but stops
    /// enforcing, so the user can suspend the contract without re-dragging the wire.
    /// </summary>
    /// <param name="transmitterGuid">The InstanceGuid of the transmitter asking.</param>
    /// <returns>true when the transmitter must preserve its target's interface.</returns>
    public bool Constrains(Guid transmitterGuid)
        => !Locked && _linkedGuid != Guid.Empty && _linkedGuid == transmitterGuid;

    /// <summary>
    /// Maps push-shaped parameter specs to the locked-interface ports the grounding (and the
    /// transmitter's rejection feedback) renders, converting the access enum to its
    /// submission-JSON string.
    /// </summary>
    /// <param name="specs">The parameter specs read off the target script component.</param>
    /// <returns>The locked-interface ports, in interface order.</returns>
    public static IReadOnlyList<ScriptInterfacePort> ToPorts(IEnumerable<GhParamSpec> specs)
        => specs.Select(s => new ScriptInterfacePort(s.Name, s.TypeHint, AccessString(s.Access))).ToList();

    /// <inheritdoc/>
    /// <remarks>
    /// Watches the document so the grounding refreshes when the target's interface changes out
    /// from under it — the user edits the script component's parameters, or relinks the
    /// transmitter — since none of those re-solve this (source) component.
    /// </remarks>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        document.SolutionEnd += OnDocumentSolutionEnd;
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        document.SolutionEnd -= OnDocumentSolutionEnd;
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs — the target is designated by the grip link, not a wire.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "Locked-interface grounding for the linked transmitter's target. Wire into the Conversation Log's Grounding input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        _lastSignature = CurrentSignature();

        if (!TryResolveTargetScript(out IGH_DocumentObject? target, out ScriptTransmitterBase? transmitter, out string problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, problem);
            return;
        }

        var grounding = new ScriptInterfaceGrounding(
            target!.NickName,
            ToPorts(GhPythonBridge.GetInputSpecs(target)),
            ToPorts(GhPythonBridge.GetOutputSpecs(target)),
            transmitter!.Dialect);

        DA.SetData(OutGrounding, new GH_Grounding(grounding));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The linked transmitter is a peer in the same document, so this link is re-pointed when a
    /// document's ids are re-issued (loading a preset).
    /// </remarks>
    void IGuidLinked.RemapLinks(IReadOnlyDictionary<Guid, Guid> replacements)
    {
        if (replacements.TryGetValue(_linkedGuid, out Guid replacement))
        {
            _linkedGuid = replacement;
        }
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetGuid("LinkedGuid", _linkedGuid);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("LinkedGuid"))
            _linkedGuid = reader.GetGuid("LinkedGuid");
        return base.Read(reader);
    }

    /// <summary>
    /// Resolves the target script component through the linked transmitter, or explains why it
    /// cannot: no transmitter linked, the transmitter itself unlinked, or its target gone. The
    /// transmitter comes back too — it owns the dialect the grounding is worded in.
    /// </summary>
    /// <param name="target">The target script component, or null.</param>
    /// <param name="transmitter">The linked transmitter, or null.</param>
    /// <param name="problem">A human-readable reason when resolution fails; otherwise empty.</param>
    /// <returns>true when a valid target script component was found.</returns>
    private bool TryResolveTargetScript(
        out IGH_DocumentObject? target,
        out ScriptTransmitterBase? transmitter,
        out string problem)
    {
        target = null;
        transmitter = null;
        problem = string.Empty;

        if (_linkedGuid == Guid.Empty)
        {
            problem = "No transmitter linked. Drag from the bottom grip onto a Py or C# Transmitter.";
            return false;
        }

        if (OnPingDocument()?.FindObject(_linkedGuid, false) is not ScriptTransmitterBase linked)
        {
            problem = "Linked component not found or is not a script transmitter.";
            return false;
        }

        // The transmitter is a Physalia node and shares this document; its target script component
        // is an ordinary component on the user's canvas, so it is resolved against the host.
        IGH_DocumentObject? script = linked.LinkedGuid == Guid.Empty
            ? null
            : Harness.PhyDocuments.Host(this)?.FindObject(linked.LinkedGuid, false);
        if (script is null || !GhPythonBridge.IsScriptComponent(script))
        {
            problem = $"The linked {linked.NickName} has no target script component — link the transmitter to one first.";
            return false;
        }

        target = script;
        transmitter = linked;
        return true;
    }

    /// <summary>
    /// Builds a signature of everything the emitted grounding depends on — the link chain plus the
    /// target's nickname and full parameter set — so a change is detected without re-solving when
    /// nothing changed.
    /// </summary>
    /// <returns>A signature string.</returns>
    private string CurrentSignature()
    {
        if (!TryResolveTargetScript(out IGH_DocumentObject? target, out _, out _))
            return $"unresolved:{_linkedGuid:N}";

        string inputs = string.Join("|", GhPythonBridge.GetInputSpecs(target!).Select(SpecKey));
        string outputs = string.Join("|", GhPythonBridge.GetOutputSpecs(target!).Select(SpecKey));
        return $"{_linkedGuid:N}:{target!.InstanceGuid:N}:{target.NickName}:{inputs}>{outputs}";
    }

    private static string SpecKey(GhParamSpec spec) => $"{spec.Name}:{spec.TypeHint}:{spec.Access}";

    private static string AccessString(GhScriptParamAccess access) => access switch
    {
        GhScriptParamAccess.List => "list",
        GhScriptParamAccess.Tree => "tree",
        _ => "item",
    };

    private void OnDocumentSolutionEnd(object sender, GH_SolutionEventArgs e)
    {
        // A transmitter push or a manual param edit changes the target's interface without
        // re-solving this source component. Re-solve only when the emitted contract actually
        // changed, so the refreshed grounding reaches the Conversation Log and the comparison
        // breaks any solve loop once it converges.
        if (CurrentSignature() != _lastSignature)
        {
            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(false));
        }
    }
}
