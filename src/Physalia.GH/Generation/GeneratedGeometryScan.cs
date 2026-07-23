// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Physalia.GH.Components;
using Rhino.Geometry;

namespace Physalia.GH.Generation;

/// <summary>
/// Locates the geometry the LLM pipeline has generated on the live document: the preview geometry
/// of the script components targeted by Py Transmitters (grip-linked) and of the components a
/// Component Transmitter placed (the authored-placement ledger — full-graph placements and ghpatch
/// adds alike). Used by the Geometry Snapshot grounding both to decide whether a snapshot is worth
/// taking (the chat window's geometry indicator) and to frame the viewport when it is.
/// </summary>
internal static class GeneratedGeometryScan
{
    /// <summary>
    /// Gets a value indicating whether any transmitter-generated component currently previews
    /// geometry on the document.
    /// </summary>
    /// <param name="doc">The document to scan; null returns false.</param>
    /// <returns>True when generated geometry is present.</returns>
    internal static bool HasGeneratedGeometry(GH_Document? doc) => ComputeBounds(doc).IsValid;

    /// <summary>
    /// Unions the preview clipping boxes of every transmitter-generated component on the document.
    /// </summary>
    /// <param name="doc">The document to scan; null returns an invalid box.</param>
    /// <returns>The combined bounding box, or an invalid box when nothing previewable was found.</returns>
    internal static BoundingBox ComputeBounds(GH_Document? doc)
    {
        BoundingBox union = BoundingBox.Empty;
        if (doc is null)
        {
            return union;
        }

        var seen = new HashSet<Guid>();

        // Scripts a Py Transmitter pushes generated Python into (resolved through its grip link).
        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (obj is PyTransmitter transmitter
                && transmitter.LinkedGuid != Guid.Empty
                && seen.Add(transmitter.LinkedGuid)
                && doc.FindObject(transmitter.LinkedGuid, false) is IGH_DocumentObject target)
            {
                union = BoundingBox.Union(union, PreviewBounds(target));
            }
        }

        // Components placed from the model's submissions by a Component Transmitter.
        foreach (Guid guid in GhJsonBridge.ModelPlacedGuids(doc))
        {
            if (seen.Add(guid) && doc.FindObject(guid, false) is IGH_DocumentObject placed)
            {
                union = BoundingBox.Union(union, PreviewBounds(placed));
            }
        }

        return union;
    }

    // The object's preview clipping box, or an invalid box when it previews nothing (hidden,
    // preview-incapable, or currently producing no geometry).
    private static BoundingBox PreviewBounds(IGH_DocumentObject obj)
    {
        if (obj is IGH_PreviewObject preview && !preview.Hidden && preview.IsPreviewCapable)
        {
            BoundingBox box = preview.ClippingBox;
            if (box.IsValid)
            {
                return box;
            }
        }

        return BoundingBox.Empty;
    }
}
