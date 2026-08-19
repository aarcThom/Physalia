// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Carries data from the user's canvas INTO the harness — the inverse of a transmitter, and the only
/// thing that crosses that boundary as a wire.
///
/// <para><b>No inputs, one output.</b> Placing one inside a harness grows an input on the LEFT edge of
/// that harness's proxy, named after this node's nickname; whatever is wired into it out there arrives
/// here, tree structure intact, and leaves on <b>Data</b>. Place several and the proxy grows several,
/// stacked in the order the Receivers are laid out inside — move them and the inputs re-order with
/// them. Rename one and its input on the proxy is renamed too.</para>
///
/// <para><b>Any data, not only geometry.</b> Geometry is what these are mostly for — a site boundary,
/// a target volume, an existing structure the model has to work around — and it rides through
/// untouched. But a goal condition is usually stated partly in numbers and text, so the parameter is
/// generic and takes whatever the canvas computed.</para>
///
/// <para><b>It is a value, not an event.</b> Data arriving here mints no signal and starts no round:
/// this node latches what it was handed and outputs it on every solve of the harness, so the value is
/// current whenever a signal-driven round happens to read it. That is what makes it safe to feed a
/// harness from a slider — and it is what keeps transmitter-writes-canvas / canvas-feeds-receiver from
/// closing into a loop, since nothing in the pipeline ACTS on inlet data by itself.</para>
///
/// <para>Standing on the user's canvas rather than inside a harness, a Receiver has no proxy to grow
/// an input on and can never be fed anything. It says so rather than sitting quietly empty.</para>
/// </summary>
public class Receiver : PhyBase, IHarnessInlet
{
    private const int OutData = 0;

    // The latched tree, held because the harness pipeline solves on its own schedule: the host
    // solution that delivered this data is long over by the time a signal round reads it.
    private GH_Structure<IGH_Goo> _held = new();

    private string _key = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="Receiver"/> class.
    /// </summary>
    public Receiver()
        : base(
            "Receiver",
            "Rx",
            "Passes data from the user's canvas into the harness. Placing one grows an input on the harness proxy, named after this node — wire geometry (or anything else) into that input and it arrives here, tree structure intact. Rename this node to label the input.",
            "Receivers")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("5F0A8C31-4D76-42B9-9E85-6C13A7F2D094");

    /// <inheritdoc/>
    /// <remarks>Falls back to the type name for a nickname cleared to nothing, so the proxy's input is never nameless.</remarks>
    public string InletName => string.IsNullOrWhiteSpace(NickName) ? Name : NickName;

    /// <inheritdoc/>
    /// <remarks>
    /// Renaming this node relabels its input on the harness proxy, which is the whole point of naming
    /// it — the label out there is how you tell one harness input from another, and this is the node
    /// that says what it is.
    ///
    /// <para>Overridden rather than watched, because there is nothing to watch. Grasshopper's
    /// <c>NickName</c> setter raises no event whatsoever (its body is a bare field assignment), and
    /// only the right-click name box announces a rename at all — so an F2 or properties-panel rename
    /// reaches no handler anywhere. Nor can the proxy simply re-read the name when it is next laid
    /// out: <c>PerformLayout</c> is called from a bare handful of places and the paint loop is not one
    /// of them, so an expired layout may never be performed. The setter is virtual, so overriding it
    /// is the one hook that cannot be missed.</para>
    /// </remarks>
    public override string NickName
    {
        get => base.NickName;

        set
        {
            if (string.Equals(base.NickName, value, StringComparison.Ordinal))
            {
                return;
            }

            base.NickName = value;

            // Null while this node is being read out of an archive or pasted, before it belongs to a
            // harness. The proxy's sync names its inputs on arrival, so nothing is lost.
            PhyDocuments.Harness(this)?.OnInletRenamed(this);
        }
    }

    /// <inheritdoc/>
    public string InletDescription =>
        $"Data for the \"{InletName}\" Receiver inside this harness. Tree structure is carried through unchanged.";

    /// <inheritdoc/>
    public bool Accept(GH_Structure<IGH_Goo> data)
    {
        string key = TreeIdentity.Of(data);
        if (key == _key)
        {
            return false;
        }

        _key = key;

        // Copied, because the structure handed out by the proxy's solve belongs to its input parameter
        // and is cleared the moment that parameter expires — long before the harness solves and reads
        // it. Shallow: the goo inside is not ours to duplicate, and it is the same goo the canvas holds.
        _held = data is null ? new GH_Structure<IGH_Goo>() : new GH_Structure<IGH_Goo>(data, false);
        return true;
    }

    /// <inheritdoc/>
    public void ClearInlet()
    {
        _held = new GH_Structure<IGH_Goo>();
        _key = string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>Nothing: the data arrives from the harness proxy, not from a wire on this node.</remarks>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter(
            "Data",
            "D",
            "Exactly what is wired into this Receiver's input on the harness proxy — the same items on the same paths.",
            GH_ParamAccess.tree);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (PhyDocuments.Harness(this) is null)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "A Receiver is fed through the input it grows on a harness proxy, so one outside a harness can never receive anything. Put it inside a harness, or wire the data straight into whatever needed it.");
        }

        // Re-emitted every solve, not only on the solve that received it: the pipeline downstream is
        // signal-driven and solves on its own schedule, and an output that emptied itself in between
        // would leave a round reading nothing.
        if (!_held.IsEmpty)
        {
            DA.SetDataTree(OutData, _held);
        }
    }
}
