// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.HumanTools;
using Physalia.GH.Attributes;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="TokenCountTool"/> that puts the live token count in the corner of the chat
/// window, reading it off the <see cref="TokenEstimator"/> this component is linked to. Drag the
/// bottom grip onto an estimator; Ctrl+drag to release it. Wire the Human Tool output into the
/// Conversation Log's Human Tools input.
/// <para>
/// Counting and showing the count are two jobs, and this component is the second one. The Token
/// Estimator measures — for a Token Threshold, for a compactor, for a panel on the canvas — and
/// knows nothing about the chat window; this says a human wants to watch the number. Neither one
/// alone puts a counter on screen: without an estimator there is nothing to show, and without this
/// tool the estimator goes on counting for the pipeline with the window staying quiet.
/// </para>
/// <para>
/// The link is explicit rather than inferred. A pipeline may carry several estimators (a cheap
/// local one gating a compactor, an exact API-backed one for the display), and picking "the first
/// one downstream" would silently show whichever the wires happened to reach first.
/// </para>
/// </summary>
public class TokenCount : HumanToolComponentBase, IGuidLinked
{
    private Guid _linkedGuid = Guid.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenCount"/> class.
    /// </summary>
    public TokenCount()
        : base("Token Count", "TokCount", "Shows the running token count in the corner of the chat window. Drag the bottom grip onto the Token Estimator whose count you want to watch.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4E1A7C36-9B58-42D0-8F7A-6C0D3B5E9A14");

    /// <summary>
    /// Gets the InstanceGuid of the linked Token Estimator, or <see cref="Guid.Empty"/> when
    /// nothing is linked.
    /// </summary>
    public Guid LinkedGuid => _linkedGuid;

    /// <summary>
    /// Gets the count the linked estimator is currently reporting, or null when nothing is linked,
    /// the target has gone, or it has not produced a count yet (an API-backed estimator is still
    /// waiting on the provider, or the pipeline has not run).
    ///
    /// <para>Read LIVE off the estimator's output rather than cached on solve, and for the same
    /// reason the Conversation Log's setting owners are: the chat window asks on its own tick,
    /// between solves, and nothing re-solves this component when the estimator recounts. The value
    /// therefore always matches what the canvas shows.</para>
    /// </summary>
    public int? CurrentCount
    {
        get
        {
            if (_linkedGuid == Guid.Empty
                || OnPingDocument()?.FindObject(_linkedGuid, false) is not TokenEstimator estimator
                || estimator.Params.Output.Count == 0)
            {
                return null;
            }

            foreach (IGH_Goo goo in estimator.Params.Output[0].VolatileData.AllData(true))
            {
                if (goo is GH_Integer integer)
                {
                    return integer.Value;
                }
            }

            return null;
        }
    }

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Puts the token counter in the chat window. Wire into a Conversation Log's Human Tools input.";

    /// <inheritdoc/>
    protected override HumanTool Tool => new TokenCountTool();

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new TokenCountAttrib(this);
    }

    /// <summary>
    /// Links this tool to a Token Estimator.
    /// Called by <see cref="TokenCountAttrib"/> when the user drops the wire.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the estimator to link.</param>
    public void LinkTo(Guid guid)
    {
        _linkedGuid = guid;
    }

    /// <summary>
    /// Removes the current link, so the chat window stops showing a count.
    /// Called by <see cref="TokenCountAttrib"/> when the user Ctrl+drops the wire.
    /// </summary>
    public void Unlink()
    {
        _linkedGuid = Guid.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The linked estimator is a peer in the same document, so this link is re-pointed when a
    /// document's ids are re-issued (loading a preset) — which is what lets a pipeline ship with
    /// its counter already wired up.
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
        {
            _linkedGuid = reader.GetGuid("LinkedGuid");
        }

        return base.Read(reader);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Says on the node itself when the counter cannot appear. Without this the failure is silent
    /// and off-canvas: the tool is wired, the window shows nothing, and there is nowhere to look.
    /// </remarks>
    protected override void OnSolveEnd()
    {
        if (_linkedGuid == Guid.Empty)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "No Token Estimator linked. Drag the bottom grip onto one — the chat window shows no count until you do.");
            return;
        }

        if (OnPingDocument()?.FindObject(_linkedGuid, false) is not TokenEstimator)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "The linked Token Estimator is gone. Drag the bottom grip onto another one.");
        }
    }
}
