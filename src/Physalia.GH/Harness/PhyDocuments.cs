// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Grasshopper;
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
/// checks, Rhino references, and the per-document memory folder.</description></item>
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
            IGH_DocumentOwner? owner = current.Owner;
            if (owner is null)
            {
                return current; // top-level: nobody owns it
            }

            GH_Document? parent = owner.OwnerDocument();
            if (parent is null || ReferenceEquals(parent, current))
            {
                // An owner that is not itself placed on a document yet (or a self-reference from a
                // corrupt file) — the current document is as far up as we can honestly go.
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
    /// Resolves the host document behind whatever the canvas is currently showing. Use this in
    /// place of <c>Instances.ActiveCanvas?.Document</c> for canvas-facing work: while the user is
    /// editing inside a harness the canvas document IS the pipeline, and acting on it would place
    /// components into the harness or ground the model on itself.
    /// </summary>
    /// <returns>The root document behind the active canvas, or null when there is none.</returns>
    internal static GH_Document? ActiveHost() => Host(Instances.ActiveCanvas?.Document);

    /// <summary>
    /// Gets a value indicating whether a document is a harness sub-document rather than a
    /// user-facing file.
    /// </summary>
    /// <param name="document">The document to test.</param>
    /// <returns>True when the document is owned by a harness.</returns>
    internal static bool IsHarnessDocument(GH_Document? document) =>
        document?.Owner is HarnessComponent;

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
