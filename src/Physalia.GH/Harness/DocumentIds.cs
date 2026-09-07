// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Physalia.GH.Components;

namespace Physalia.GH.Harness;

/// <summary>
/// Re-issues the instance ids of a whole document, and repairs the links that point within it.
///
/// <para>Needed because a preset is read from a file rather than pasted: the archive carries the
/// instance ids it was saved with, so placing the same preset twice puts two objects with the SAME
/// InstanceGuid in one file. Grasshopper re-issues ids on paste for exactly this reason — ids are
/// assumed unique per file by anything that identifies an object by one, from the chat window's
/// switcher row to the signal trace to GhJSON export.</para>
///
/// <para><b>A harness inside the document is descended into.</b> A nested harness's contents are in
/// a document of their own, which <c>MutateAllIds</c> knows nothing about, so without the recursion
/// they keep the ids the archive was saved with — and a preset placed twice puts two components with
/// the SAME InstanceGuid in one file. That is not a corner case: a Delegate links to a worker
/// harness, so every delegation preset is this shape. Found by placing one twice (2026-09-07) and
/// finding two Timers sharing an id, which is exactly what Trigger Control's guid addressing exists
/// to prevent.</para>
/// </summary>
internal static class DocumentIds
{
    /// <summary>
    /// How deep the harness nesting may go before the walk gives up, so a malformed archive cannot
    /// recurse without end. Mirrors the hop limit on <see cref="PhyDocuments"/>'s owner walk.
    /// </summary>
    private const int MaxNesting = 8;

    /// <summary>
    /// Gives every object in a document a fresh instance id, then remaps the guid-held links between
    /// them so the pipeline behaves exactly as it did before.
    ///
    /// <para>Wires need no help — Grasshopper keeps sources as object references, which is why
    /// <c>MutateAllIds</c> requires proxy sources to be resolved first — and groups are handled by
    /// Grasshopper itself. What it cannot know about is a guid stored in one of our own fields; those
    /// components declare themselves with <see cref="IGuidLinked"/>.</para>
    /// </summary>
    /// <param name="document">The document to re-issue. Its proxy sources must already be resolved.</param>
    internal static void MutateAll(GH_Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        MutateNested(document, depth: 0);
    }

    /// <summary>
    /// Re-issues one document's ids, having first done the same for each harness nested inside it.
    ///
    /// <para>Deliberately NOT an overload of <see cref="MutateAll(GH_Document)"/>: the existing
    /// <c>cref</c>s to that name elsewhere would become ambiguous, and a repo with doc-comment
    /// validation on would start warning about references that were fine.</para>
    /// </summary>
    /// <param name="document">The document to re-issue.</param>
    /// <param name="depth">How many harnesses deep this document sits.</param>
    private static void MutateNested(GH_Document document, int depth)
    {
        // Each document gets its OWN replacement map, which is right rather than merely convenient:
        // every guid-held link points at something in the same document as the component holding it
        // (a Delegate and its worker are peers, so are a Script I/O and its transmitter). One shared
        // map would gain nothing and would hand a component ids from a document it cannot reach.
        // Descend FIRST, so the early return below cannot skip it.
        if (depth < MaxNesting)
        {
            foreach (IGH_DocumentObject obj in document.Objects)
            {
                if (obj is HarnessComponent harness && harness.InnerDocument is GH_Document inner)
                {
                    // InnerDocument, never EnsureInnerDocument: a harness that has no document yet
                    // has nothing to re-issue, and materialising one here would be a side effect.
                    MutateNested(inner, depth + 1);
                }
            }
        }

        // Old ids paired to the objects themselves, because MutateAllIds reports no mapping and object
        // identity is the only thing that survives it. Parameters are included: they carry instance ids
        // too, and a link could name one.
        var before = new List<(IGH_DocumentObject Object, Guid OldId)>();
        foreach (IGH_DocumentObject obj in document.Objects)
        {
            before.Add((obj, obj.InstanceGuid));

            if (obj is IGH_Component component)
            {
                foreach (IGH_Param param in component.Params.Input)
                {
                    before.Add((param, param.InstanceGuid));
                }

                foreach (IGH_Param param in component.Params.Output)
                {
                    before.Add((param, param.InstanceGuid));
                }
            }
        }

        document.MutateAllIds();

        var replacements = new Dictionary<Guid, Guid>();
        foreach ((IGH_DocumentObject obj, Guid oldId) in before)
        {
            if (oldId != obj.InstanceGuid)
            {
                replacements[oldId] = obj.InstanceGuid;
            }
        }

        if (replacements.Count == 0)
        {
            return;
        }

        foreach (IGH_DocumentObject obj in document.Objects)
        {
            if (obj is IGuidLinked linked)
            {
                linked.RemapLinks(replacements);
            }
        }
    }
}
