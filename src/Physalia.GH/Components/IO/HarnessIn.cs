// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Physalia.GH.Harness;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Carries data from the user's canvas INTO the harness. The inward half of the harness boundary, and
/// the mirror of <see cref="HarnessOut"/>: this one has no inputs, that one has no outputs, and
/// between them the whole exchange with the canvas happens at the proxy rather than in here.
///
/// <para><b>No inputs, one output.</b> Placing one inside a harness grows an input on the LEFT edge of
/// that harness's proxy; whatever is wired into it out there arrives here, tree structure intact, and
/// leaves on <b>Data</b>. Place several and the proxy grows several, stacked in the order these nodes
/// are laid out inside — move them and the inputs re-order with them.</para>
///
/// <para><b>One name, two ends.</b> This output and the harness input it is fed by share a nickname,
/// starting out "Data" on both. Rename either — the output in here, or the input on the proxy out
/// there — and the other follows, so the label a user reads on the harness always says what the wire
/// inside is called. The node's own nickname is left alone for saying what the node is.</para>
///
/// <para><b>Any data, not only geometry.</b> Geometry is what these are mostly for — a site boundary,
/// a target volume, an existing structure the model has to work around — and it rides through
/// untouched. But a goal condition is usually stated partly in numbers and text, so the parameter is
/// generic and takes whatever the canvas computed.</para>
///
/// <para><b>It is a value, not an event.</b> Data arriving here mints no signal and starts no round:
/// this node latches what it was handed and outputs it on every solve of the harness, so the value is
/// current whenever a signal-driven round happens to read it. That is what makes it safe to feed a
/// harness from a slider — and it is what keeps Harness Out writing to the canvas / the canvas feeding
/// a Harness In from closing into a loop, since nothing in the pipeline ACTS on inlet data by
/// itself.</para>
///
/// <para>Standing on the user's canvas rather than inside a harness, this node has no proxy to grow an
/// input on and can never be fed anything. It says so rather than sitting quietly empty.</para>
/// </summary>
public class HarnessIn : PhyBase, IHarnessInlet
{
    private const int OutData = 0;

    /// <summary>The name a fresh node's output — and so its harness input — starts out with.</summary>
    private const string DefaultName = "Data";

    // The latched tree, held because the harness pipeline solves on its own schedule: the host
    // solution that delivered this data is long over by the time a signal round reads it.
    private GH_Structure<IGH_Goo> _held = new();

    private string _key = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessIn"/> class.
    /// </summary>
    public HarnessIn()
        : base(
            "Harness In",
            "Harness In",
            "Brings data from your canvas into the harness. Putting one in grows an input on the left edge of the harness node; whatever you wire into it out there arrives here, branches and all. Rename this node's output to label that input.",
            "I/O")
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unchanged from when this component was called "Receiver": renaming it must not orphan the nodes
    /// in documents already holding one.
    /// </remarks>
    public override Guid ComponentGuid => new Guid("5F0A8C31-4D76-42B9-9E85-6C13A7F2D094");

    /// <inheritdoc/>
    /// <remarks>
    /// The OUTPUT parameter's nickname, not this node's. The name a harness input carries and the name
    /// on the wire leaving here are the same name, so there is one place it lives and this is it; the
    /// node's own nickname stays free to say what the node is. Falls back to <see cref="DefaultName"/>
    /// for a nickname cleared to nothing, so the input is never nameless.
    /// </remarks>
    public string InletName
    {
        get => Port?.NickName is { } nickname && !string.IsNullOrWhiteSpace(nickname)
            ? nickname
            : DefaultName;

        set
        {
            if (Port is { } port)
            {
                // A cleared name is taken as "back to the default" rather than obeyed: an unnamed grip
                // on the proxy tells the user nothing, and the two ends would then disagree, since the
                // getter has to answer with something.
                port.NickName = string.IsNullOrWhiteSpace(value) ? DefaultName : value;
            }
        }
    }

    /// <inheritdoc/>
    public string InletDescription =>
        $"Goes to the \"{InletName}\" Harness In inside this harness. Branch structure is carried through unchanged.";

    // This node's output, typed so its rename can be heard. Null only while the component is being
    // constructed or has had its parameters torn down.
    private Param_HarnessPort? Port =>
        Params.Output.Count > OutData ? Params.Output[OutData] as Param_HarnessPort : null;

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
    /// <remarks>
    /// Re-binds the output's rename callback. Registration already bound the parameter this component
    /// built, but a component read from an archive gets its parameters back from Grasshopper, and
    /// those arrive with nothing attached.
    /// </remarks>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        if (Port is { } port)
        {
            BindPort(port);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Nothing: the data arrives from the harness proxy, not from a wire on this node.</remarks>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // Nicknamed in full rather than initialled, because this name is shared with the harness input
        // it feeds and has to read as a label out there. Bound here as well as in AddedToDocument: this
        // runs during construction, so a rename is heard from the very first one.
        var port = new Param_HarnessPort();
        BindPort(port);

        pManager.AddParameter(
            port,
            DefaultName,
            DefaultName,
            "Exactly what is wired into the matching input on the harness node: same items, same branches. Rename it and that input takes the new name too.",
            GH_ParamAccess.tree);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (PhyDocuments.Harness(this) is null)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "A Harness In is fed through the input it grows on a harness proxy, so one outside a harness can never receive anything. Put it inside a harness, or wire the data straight into whatever needed it.");
        }

        // Re-emitted every solve, not only on the solve that received it: the pipeline downstream is
        // signal-driven and solves on its own schedule, and an output that emptied itself in between
        // would leave a round reading nothing.
        if (!_held.IsEmpty)
        {
            DA.SetDataTree(OutData, _held);
        }
    }

    // Points the output at this node, so renaming it carries out to the harness input. Nothing else
    // can see that rename: Grasshopper's NickName setter raises no event, and layout — which could
    // otherwise re-read the name — is performed on solution, not on paint.
    private void BindPort(Param_HarnessPort port)
    {
        port.Renamed = name =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                // Cleared in here rather than on the proxy. Put the default back, which re-enters this
                // callback with a real name and carries THAT outward — so both ends land on "Data"
                // instead of one going blank and the other keeping the old label.
                port.NickName = DefaultName;
                return;
            }

            PhyDocuments.Harness(this)?.OnInletRenamed(this);
        };
    }
}
