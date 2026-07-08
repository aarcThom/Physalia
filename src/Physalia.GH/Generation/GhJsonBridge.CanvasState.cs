// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GhJSON.Core;
using GhJSON.Core.SchemaModels;
using GhJSON.Core.Serialization;
using GhJSON.Grasshopper;
using Grasshopper.Kernel;
using Physalia.GH.Components;

namespace Physalia.GH.Generation;

/// <summary>
/// Canvas-state export half of the façade: serializes the user's work product — every canvas
/// object that is not a Physalia component — to GhJSON. This is the SINGLE reference frame shared
/// by the Canvas State grounder (what the model sees) and the patch-apply path (the base a ghpatch
/// is interpreted against): one code path means the two can never disagree on scope or options.
/// The export is deterministic for an unchanged canvas (ids are assigned in document insertion
/// order), so the checksum computed over the grounding text still matches a fresh export at apply
/// time unless the canvas actually changed.
/// </summary>
internal static partial class GhJsonBridge
{
    // componentState.extensions key marking an exported parameter as referencing live geometry in
    // the Rhino model. Injected into the canvas-state export (with the baked geometry stripped) so
    // the model treats the parameter as a data source: wire FROM it, never modify its value or
    // recreate it. Rebuilding such a parameter from the export would sever the Rhino link — the
    // GhJSON round-trip bakes values, never reference ids.
    private const string RhinoRefExtensionKey = "physalia.rhinoRef";

    /// <summary>
    /// One export of the user's canvas: the parsed document (the patch base), its serialized JSON
    /// (the grounding text), the checksum over that JSON, and the component count.
    /// </summary>
    /// <param name="Document">The exported document; the reference frame a ghpatch resolves against.</param>
    /// <param name="Json">The document serialized compactly, exactly as handed to the model.</param>
    /// <param name="Checksum">SHA-256 fingerprint of <paramref name="Json"/> (<c>sha256-…</c>).</param>
    /// <param name="ComponentCount">Number of exported components; zero for an empty canvas.</param>
    internal sealed record CanvasStateSnapshot(
        GhJsonDocument Document,
        string Json,
        string Checksum,
        int ComponentCount);

    /// <summary>
    /// Exports the current state of the user's canvas — every object whose type does not come from
    /// the Physalia assembly, which keeps the work product (native components, floating params
    /// placed by the Rhino Geometry tool, clusters, groups) and drops the Physalia pipeline itself.
    /// Returns null when there is no document to export.
    /// </summary>
    /// <param name="doc">The Grasshopper document to export; null falls back to the active canvas.</param>
    /// <returns>The snapshot, or null when no document is available.</returns>
    internal static CanvasStateSnapshot? TryExportCanvasState(GH_Document? doc = null)
    {
        doc ??= Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc is null)
        {
            return null;
        }

        var guids = doc.Objects
            .Where(o => o is not null && o.GetType().Assembly != typeof(PhyBase).Assembly)
            .Select(o => o.InstanceGuid)
            .ToList();

        if (guids.Count == 0)
        {
            return new CanvasStateSnapshot(new GhJsonDocument(), string.Empty, string.Empty, 0);
        }

        GhJsonDocument export = GhJsonGrasshopper.GetByGuids(guids);

        // Mark Rhino-referenced parameters before serialization, so the marker rides the checksum.
        AnnotateRhinoReferences(export, doc);

        // Compact serialization: the model reads it fine and it keeps the per-turn token cost down.
        string json = GhJson.ToJson(export, new WriteOptions { Indented = false });

        return new CanvasStateSnapshot(
            export,
            json,
            ComputeCanvasChecksum(json),
            export.Components?.Count ?? 0);
    }

    /// <summary>
    /// Computes the drift-check fingerprint over an exported canvas-state JSON string. Physalia
    /// generates AND verifies this checksum itself (the grounder prints it, the patch carries it
    /// back verbatim, apply re-exports and compares), so a plain hash of the exact text suffices —
    /// no canonical normalization is needed.
    /// </summary>
    /// <param name="json">The exported canvas-state JSON.</param>
    /// <returns>The fingerprint in <c>sha256-&lt;hex&gt;</c> form, or an empty string for blank input.</returns>
    internal static string ComputeCanvasChecksum(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return string.Empty;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Stamps every exported component whose live object references Rhino geometry with the
    // physalia.rhinoRef extension, and strips its baked geometry (the library serializes a
    // referenced param's CURRENT VALUE into internalizedData — pure token bloat in the prompt, and
    // data the model must never copy into a patch).
    private static void AnnotateRhinoReferences(GhJsonDocument export, GH_Document doc)
    {
        var referenced = new Dictionary<Guid, IGH_Param>();
        foreach (IGH_Param param in doc.Objects.OfType<IGH_Param>())
        {
            if (CanvasRhinoReferences.IsRhinoReferenced(param))
            {
                referenced[param.InstanceGuid] = param;
            }
        }

        if (referenced.Count == 0)
        {
            return;
        }

        foreach (GhJsonComponent component in export.Components ?? Enumerable.Empty<GhJsonComponent>())
        {
            if (component.InstanceGuid is not Guid guid || !referenced.TryGetValue(guid, out IGH_Param? param))
            {
                continue;
            }

            component.ComponentState ??= new GhJsonComponentState();
            component.ComponentState.Extensions ??= new Dictionary<string, object>();
            component.ComponentState.Extensions[RhinoRefExtensionKey] =
                new Dictionary<string, object> { ["type"] = param.TypeName };

            foreach (GhJsonParameterSettings settings in
                (component.InputSettings ?? Enumerable.Empty<GhJsonParameterSettings>())
                .Concat(component.OutputSettings ?? Enumerable.Empty<GhJsonParameterSettings>()))
            {
                settings.InternalizedData = null;
            }
        }
    }
}
