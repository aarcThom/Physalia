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
/// </summary>
internal static class DocumentIds
{
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
