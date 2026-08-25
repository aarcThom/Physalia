// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace Physalia.GH.Harness;

/// <summary>
/// Resolves which Grasshopper document a pipeline component should act on.
///
/// <para>A Physalia pipeline lives inside a harness sub-document owned by a
/// <see cref="HarnessComponent"/>, so <c>OnPingDocument()</c> on a pipeline component returns the
/// <em>sub</em>-document, and <c>Instances.ActiveCanvas.Document</c> returns the sub-document
/// whenever the user is editing inside the harness. Neither is the user's model.</para>
///
/// <para>The split every caller must respect:</para>
/// <list type="bullet">
/// <item><description><b>Local document</b> (<c>OnPingDocument()</c>) — the component's own
/// lifecycle: <c>ScheduleSolution</c>, <c>NewSolution</c>, resolving wired or co-resident peers
/// (Feedback to Feedback Collector, Tools Present to its tool nodes). Every endpoint of those
/// relationships moves into the harness together, so they must stay local.</description></item>
/// <item><description><b>Host document</b> (this class) — anything that means "the user's
/// canvas": canvas-state grounding, component placement, geometry and health reports, fidelity
/// checks, and Rhino references.</description></item>
/// <item><description><b>The harness</b> (<see cref="Harness(IGH_DocumentObject)"/>) — anything
/// belonging to one line of work rather than to a file: the master group a pipeline writes into,
/// and the local memory folder, which is keyed by the harness's NAME so the model's notes travel
/// with the pipeline instead of with whatever document it was dropped into.</description></item>
/// </list>
/// </summary>
internal static class PhyDocuments
{
    // Ownership chains are one level deep in practice (a harness inside the user's document).
    // The cap only exists so a corrupt Owner cycle can never spin the climb forever.
    private const int MaxOwnerHops = 32;

    /// <summary>
    /// Climbs the document ownership chain to the root document — the one the user sees as their
    /// file. Returns the document itself when it is already top-level.
    /// </summary>
    /// <param name="document">The document to resolve from, which may be a harness sub-document.</param>
    /// <returns>The root document, or null when none was supplied.</returns>
    internal static GH_Document? Host(GH_Document? document)
    {
        GH_Document? current = document;

        for (int hop = 0; current is not null && hop < MaxOwnerHops; hop++)
        {
            // A harness records its own ownership rather than using GH_Document.Owner (see
            // HarnessComponent.Owners for why); a real GH cluster uses Owner, so both are followed.
            GH_Document? parent = HarnessComponent.OwnerOf(current)?.OnPingDocument()
                ?? current.Owner?.OwnerDocument();

            if (parent is null || ReferenceEquals(parent, current))
            {
                // Top-level, an owner not yet placed on a document, or a self-reference from a
                // corrupt file — this is as far up as we can honestly go.
                return current;
            }

            current = parent;
        }

        return current;
    }

    /// <summary>
    /// Resolves the host document for a document object, so a component inside a harness acts on
    /// the user's canvas rather than on the pipeline it lives in.
    /// </summary>
    /// <param name="obj">The document object, typically the calling component.</param>
    /// <returns>The root document, or null when the object is not on a document.</returns>
    internal static GH_Document? Host(IGH_DocumentObject? obj) =>
        obj is null ? null : Host(obj.OnPingDocument());

    /// <summary>
    /// Resolves the harness a pipeline component lives in — its identity as one line of work, which is
    /// what per-pipeline state is filed under (the master group it writes into, the canvas frame the
    /// model is reading). Null for a component placed straight onto the canvas rather than into a
    /// harness, which is allowed: everything keyed on the harness falls back to an unscoped default
    /// (the master group loses its per-harness suffix, the chat switcher sorts it ahead of the rest).
    /// </summary>
    /// <param name="obj">The document object, typically the calling component.</param>
    /// <returns>The owning harness, or null.</returns>
    internal static HarnessComponent? Harness(IGH_DocumentObject? obj) =>
        obj is null ? null : HarnessComponent.OwnerOf(obj.OnPingDocument());

    /// <summary>
    /// Resolves the host document behind whatever the canvas is currently showing. Use this in
    /// place of <c>Instances.ActiveCanvas?.Document</c> for canvas-facing work: while the user is
    /// editing inside a harness the canvas document IS the pipeline, and acting on it would place
    /// components into the harness or ground the model on itself.
    /// </summary>
    /// <returns>The root document behind the active canvas, or null when there is none.</returns>
    internal static GH_Document? ActiveHost() => Host(Instances.ActiveCanvas?.Document);

    /// <summary>
    /// Runs an action with the canvas pointed at the host document, restoring the previous view
    /// afterwards.
    ///
    /// <para>The GhJSON library resolves its target from <c>Instances.ActiveCanvas.Document</c>
    /// internally (<c>CanvasReader.GetActiveDocument</c>) and takes no document parameter — its
    /// placer is <c>internal</c>, so there is no seam to pass one through. If the user happens to
    /// be inside a harness when the model places a graph, every component lands in the pipeline's
    /// own document instead of on their canvas. Wrapping the library's write calls in this keeps
    /// placement on the host no matter what the user is looking at.</para>
    ///
    /// <para>Costs nothing in the normal case: when the canvas is already showing the host, the
    /// action is invoked directly. Grasshopper stores each document's viewport target and zoom on
    /// the way out and restores them on the way back in, so the user's position inside the harness
    /// survives the round trip. Only for writes — reads resolve their objects from the host
    /// document directly rather than swapping the canvas, which would thrash on every solve.</para>
    /// </summary>
    /// <typeparam name="T">The action's result type.</typeparam>
    /// <param name="action">The work to run against the host document.</param>
    /// <returns>Whatever the action returned.</returns>
    internal static T OnHostCanvas<T>(Func<T> action)
    {
        GH_Canvas? canvas = Instances.ActiveCanvas;
        GH_Document? shown = canvas?.Document;
        GH_Document? host = Host(shown);

        if (canvas is null || shown is null || host is null || ReferenceEquals(shown, host))
        {
            return action();
        }

        canvas.Document = host;
        try
        {
            return action();
        }
        finally
        {
            canvas.Document = shown;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a document is a harness sub-document rather than a
    /// user-facing file.
    /// </summary>
    /// <param name="document">The document to test.</param>
    /// <returns>True when the document is owned by a harness.</returns>
    internal static bool IsHarnessDocument(GH_Document? document) =>
        HarnessComponent.OwnerOf(document) is not null;

    /// <summary>
    /// Enumerates a document's objects together with everything inside any harness it contains,
    /// depth first. Use this for lookups that must find pipeline components wherever they live —
    /// finding a Chat to open the chat window on, for instance — as opposed to canvas scans, which
    /// want the host document's own objects only.
    /// </summary>
    /// <param name="document">The document to walk, normally a host document.</param>
    /// <returns>Every object in the document and in the harnesses nested within it.</returns>
    internal static IEnumerable<IGH_DocumentObject> ObjectsIncludingHarnesses(GH_Document? document) =>
        Walk(document, 0);

    private static IEnumerable<IGH_DocumentObject> Walk(GH_Document? document, int depth)
    {
        if (document is null || depth >= MaxOwnerHops)
        {
            yield break;
        }

        foreach (IGH_DocumentObject obj in document.Objects)
        {
            yield return obj;

            if (obj is HarnessComponent harness)
            {
                foreach (IGH_DocumentObject nested in Walk(harness.InnerDocument, depth + 1))
                {
                    yield return nested;
                }
            }
        }
    }
}
