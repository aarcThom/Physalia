// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Scans the document for tool nodes that are genuinely hooked into a dispatch loop — those whose
/// Signal input is wired from a <see cref="Router"/> — and emits their tool definitions as a single
/// grounding. Wire its one output into a Conversation Log's Grounding input (alongside any Component Catalog, Cluster,
/// or Document Units grounding) instead of fanning each tool node's Tool output in by hand: the
/// Conversation Log lifts the tool definitions onto the Instructions it mints, so the LLM Call advertises them
/// to the model, and surfaces their names for the chat input's <c>/t/&lt;toolname&gt;</c> reference.
///
/// <para>A stray, unwired tool node is ignored: a tool counts as in use only once a Router can
/// actually dispatch to it. The list refreshes live as tools are wired, unwired, added, or removed —
/// the component watches the document's solution end and re-solves itself only when the in-use set
/// actually changes, so the new definitions reach the Conversation Log without a runaway solve loop.</para>
///
/// <para>Each tool node also carries its own "Advertise To The Model" switch, and this grounding
/// reports only the nodes that have it on. The switch lives on the node (so it travels with it and is
/// saved in the file) rather than as a name-keyed selection here, so this component keeps no settings
/// of its own — it merely reads theirs. <see cref="ScannedTools"/> is the full in-use set, flags
/// included, which is what the chat window's tools page lists and edits.</para>
/// </summary>
public class ToolsInUse : PhyBase
{
    private const int OutTools = 0;

    private string _lastSignature = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsInUse"/> class.
    /// </summary>
    public ToolsInUse()
        : base("Tools Present", "ToolsUsed", "Tells the model which tools it can actually reach, by looking at what is wired into the Router. Nothing to set: add or remove a tool node and this follows.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("E3A7C612-9F84-4B0D-A5E1-7C2D8F61B934");

    /// <inheritdoc/>
    /// <remarks>
    /// Watches the document so the list refreshes the moment a tool is wired into or out of a Router
    /// — such a wire change does not re-solve this (the source) component, so the refresh is driven
    /// off the end of the solution that solved the changed wiring.
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
        // No inputs — the component discovers tools by scanning the document.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "Every tool currently reachable through a Router, described so the model knows when each one is worth calling. Wire into a Conversation Log's Grounding input.", GH_ParamAccess.item);
    }

    /// <summary>
    /// Gets every tool node currently in use — wired into a Router — whether or not it is advertised
    /// to the model. Read live off the document, so it is current without this component having
    /// solved. The chat window's tools page renders this list and flips each node's own advertise
    /// switch; the grounding on the wire carries only the advertised subset.
    /// </summary>
    public IReadOnlyList<LlmToolComponentBase> ScannedTools => InUseTools(OnPingDocument()).ToList();

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var tools = InUseTools(OnPingDocument()).ToList();
        _lastSignature = Signature(tools);

        // Only the advertised nodes are described to the model. A parked tool stays wired and able to
        // answer — it is simply never mentioned, so it is never called.
        var definitions = tools
            .Where(t => t.Advertise)
            .Select(t => t.AdvertisedDefinition)
            .ToList();

        DA.SetData(OutTools, new GH_Grounding(new ToolsGrounding(definitions)));
    }

    /// <summary>
    /// Enumerates the tool nodes in the document that are dispatched by a Router (Signal input wired
    /// from a <see cref="Router"/>). Returns nothing when the document is unavailable.
    /// </summary>
    /// <param name="doc">The owning document, or null.</param>
    /// <returns>The in-use tool nodes.</returns>
    private static IEnumerable<LlmToolComponentBase> InUseTools(GH_Document? doc)
    {
        if (doc is null)
        {
            yield break;
        }

        foreach (LlmToolComponentBase tool in doc.Objects.OfType<LlmToolComponentBase>())
        {
            if (IsDispatchedFromRouter(tool))
            {
                yield return tool;
            }
        }
    }

    /// <summary>
    /// Reports whether a tool node's Signal input (index 0) is wired from a Router, meaning a Router
    /// can dispatch tool calls to it.
    /// </summary>
    /// <param name="tool">The tool node to inspect.</param>
    /// <returns>True when at least one Signal-input source is a Router.</returns>
    private static bool IsDispatchedFromRouter(LlmToolComponentBase tool)
    {
        foreach (IGH_Param source in tool.Params.Input[0].Sources)
        {
            if (source.Attributes?.GetTopLevel?.DocObject is Router)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds a stable, order-independent signature of the in-use set — each tool's instance id, its
    /// advertised name, and whether it is advertised at all — so a change (wire, add/remove, a renamed
    /// tool, or a flipped advertise switch) is detected without re-solving when nothing changed.
    /// </summary>
    /// <param name="tools">The in-use tool nodes.</param>
    /// <returns>A signature string.</returns>
    private static string Signature(IEnumerable<LlmToolComponentBase> tools) =>
        string.Join(
            "|",
            tools
                .Select(t => $"{t.InstanceGuid:N}:{t.AdvertisedDefinition.Name}:{(t.Advertise ? '1' : '0')}")
                .OrderBy(s => s, StringComparer.Ordinal));

    private void OnDocumentSolutionEnd(object sender, GH_SolutionEventArgs e)
    {
        // A wire may have been added/removed to a tool node during this solution, which does not
        // re-solve this source component. Re-solve only when the in-use set actually changed, so the
        // refreshed definitions reach the Conversation Log and the comparison breaks any solve loop once it
        // converges.
        if (Signature(InUseTools(OnPingDocument())) != _lastSignature)
        {
            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(false));
        }
    }
}
