// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
/// The export is deterministic for an unchanged canvas. The drift checksum is computed over the
/// export's STRUCTURE only (see <see cref="ComputeCanvasChecksum"/>), so cosmetic edits the user
/// makes while the model is generating — dragging components, pressing a button, tweaking a
/// slider — do not invalidate the model's patch; structural edits still do.
///
/// <para>Component ids are SESSION-STABLE: the library assigns ids in document insertion order on
/// every export, which renumbers the whole graph whenever anything is added or removed — and the
/// model demonstrably reasons from ids it saw (or authored) on earlier turns, so a renumber turns
/// its next patch into silent wrong-target wiring. A per-document registry therefore maps each
/// instanceGuid to the first id it was ever exported (or placed) under; exports remap onto those
/// ids, retired ids are never reused, and patch adds claim the ids the model gave them. An id the
/// model remembers either still means the same component or resolves to nothing — never to a
/// different component.</para>
/// </summary>
internal static partial class GhJsonBridge
{
    // componentState.extensions key marking an exported parameter as referencing live geometry in
    // the Rhino model. Injected into the canvas-state export (with the baked geometry stripped) so
    // the model treats the parameter as a data source: wire FROM it, never modify its value or
    // recreate it. Rebuilding such a parameter from the export would sever the Rhino link — the
    // GhJSON round-trip bakes values, never reference ids.
    private const string RhinoRefExtensionKey = "physalia.rhinoRef";

    // Session-only stable-id registry, one per document (weak-keyed so a closed document releases
    // its map). Nothing here persists — like the rest of the lifecycle, ids restart at 1 when the
    // process restarts, and the first export of a session defines the numbering from then on.
    private static readonly ConditionalWeakTable<GH_Document, StableIdRegistry> StableIdRegistries = new();

    /// <summary>
    /// One export of the user's canvas: the parsed document (the patch base), its serialized JSON
    /// (the grounding text), the checksum over that JSON, and the component count.
    /// </summary>
    /// <param name="Document">The exported document; the reference frame a ghpatch resolves against.</param>
    /// <param name="Json">The document serialized compactly, exactly as handed to the model.</param>
    /// <param name="Checksum">Structural fingerprint of <paramref name="Document"/> (<c>sha256-…</c>).</param>
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

        // Remap the library's insertion-order numbering onto the session-stable ids, so the ids the
        // model saw (or authored) on earlier turns keep meaning the same components.
        AssignStableIds(export, doc);

        // Mark Rhino-referenced parameters before serialization, so the marker rides the checksum.
        AnnotateRhinoReferences(export, doc);

        // Compact serialization: the model reads it fine and it keeps the per-turn token cost down.
        string json = GhJson.ToJson(export, new WriteOptions { Indented = false });

        return new CanvasStateSnapshot(
            export,
            json,
            ComputeCanvasChecksum(export),
            export.Components?.Count ?? 0);
    }

    /// <summary>
    /// Computes the drift-check fingerprint over the STRUCTURE of an exported canvas: the
    /// component set (stable id, instanceGuid, name), the wire topology, and group membership.
    /// State that cannot affect how a ghpatch applies — pivots, slider/button/panel values,
    /// nicknames, internalized data — is deliberately excluded, so a user moving components or
    /// tweaking values while the model is generating does NOT invalidate its patch. Structural
    /// edits (adding, removing, renaming, or rewiring components, or regrouping) still change the
    /// fingerprint and force the model to regenerate against the fresh canvas state.
    /// </summary>
    /// <param name="export">The exported canvas-state document.</param>
    /// <returns>The fingerprint in <c>sha256-&lt;hex&gt;</c> form, or an empty string for an empty export.</returns>
    internal static string ComputeCanvasChecksum(GhJsonDocument export)
    {
        if (export.Components is null || export.Components.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach (GhJsonComponent component in export.Components
            .OrderBy(c => c.Id ?? int.MaxValue)
            .ThenBy(c => c.InstanceGuid))
        {
            sb.Append(component.Id).Append('|').Append(component.InstanceGuid).Append('|').Append(component.Name).Append('\n');
        }

        IEnumerable<string> wires = (export.Connections ?? Enumerable.Empty<GhJsonConnection>())
            .Where(w => w.From is not null && w.To is not null)
            .Select(w => $"{w.From!.Id}:{w.From.ParamIndex?.ToString() ?? w.From.ParamName}>{w.To!.Id}:{w.To.ParamIndex?.ToString() ?? w.To.ParamName}")
            .OrderBy(s => s, StringComparer.Ordinal);
        foreach (string wire in wires)
        {
            sb.Append(wire).Append('\n');
        }

        foreach (GhJsonGroup group in (export.Groups ?? Enumerable.Empty<GhJsonGroup>())
            .OrderBy(g => g.Id ?? int.MaxValue))
        {
            sb.Append("g:").Append(group.Id).Append('|').Append(group.InstanceGuid).Append('|');
            sb.Append(string.Join(",", (group.Members ?? Enumerable.Empty<int>()).OrderBy(m => m)));
            sb.Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
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
                new Dictionary<string, object> { ["type"] = ComponentSignatureProvider.SafeTypeName(param) };

            foreach (GhJsonParameterSettings settings in
                (component.InputSettings ?? Enumerable.Empty<GhJsonParameterSettings>())
                .Concat(component.OutputSettings ?? Enumerable.Empty<GhJsonParameterSettings>()))
            {
                settings.InternalizedData = null;
            }
        }
    }

    /// <summary>
    /// Claims exported/placed ids for their objects in the document's stable-id registry, so later
    /// canvas exports keep the numbering the model authored. An id already claimed by another
    /// object (or a guid already registered) is skipped — the export falls through to a fresh
    /// assignment for that object, which is still stable from then on.
    /// </summary>
    /// <param name="doc">The document the objects live in; null is a no-op.</param>
    /// <param name="idToGuid">The id-to-instanceGuid pairs to claim (e.g. a Put result's mapping).</param>
    internal static void RegisterStableIds(GH_Document? doc, IEnumerable<KeyValuePair<int, Guid>> idToGuid)
    {
        if (doc is null)
        {
            return;
        }

        StableIdRegistry registry = StableIdRegistries.GetOrCreateValue(doc);
        foreach (KeyValuePair<int, Guid> pair in idToGuid)
        {
            registry.Claim(pair.Value, pair.Key);
        }
    }

    // Rewrites the export's insertion-order ids (components, connection endpoints, groups and
    // their members) onto the session-stable ids. Two passes: the old->stable map is computed
    // from the original ids first, then applied, so a swapped pair cannot alias mid-rewrite.
    private static void AssignStableIds(GhJsonDocument export, GH_Document doc)
    {
        StableIdRegistry registry = StableIdRegistries.GetOrCreateValue(doc);

        var remap = new Dictionary<int, int>();
        foreach (GhJsonComponent component in export.Components ?? Enumerable.Empty<GhJsonComponent>())
        {
            if (component.InstanceGuid is Guid guid && component.Id is int oldId)
            {
                remap[oldId] = registry.Resolve(guid);
            }
        }

        foreach (GhJsonComponent component in export.Components ?? Enumerable.Empty<GhJsonComponent>())
        {
            if (component.Id is int oldId && remap.TryGetValue(oldId, out int stable))
            {
                component.Id = stable;
            }
        }

        foreach (GhJsonConnection connection in export.Connections ?? Enumerable.Empty<GhJsonConnection>())
        {
            if (connection.From is { } from && remap.TryGetValue(from.Id, out int fromId))
            {
                from.Id = fromId;
            }

            if (connection.To is { } to && remap.TryGetValue(to.Id, out int toId))
            {
                to.Id = toId;
            }
        }

        foreach (GhJsonGroup group in export.Groups ?? Enumerable.Empty<GhJsonGroup>())
        {
            if (group.InstanceGuid is Guid groupGuid && group.Id is not null)
            {
                group.Id = registry.Resolve(groupGuid);
            }

            if (group.Members is { } members)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    if (remap.TryGetValue(members[i], out int member))
                    {
                        members[i] = member;
                    }
                }
            }
        }
    }

    // instanceGuid -> the id the model knows the object by. Ids are handed out once and never
    // reused: a removed component retires its id forever, so a stale id in a patch resolves to
    // nothing (a loud conflict) instead of silently landing on a different component.
    private sealed class StableIdRegistry
    {
        private readonly Dictionary<Guid, int> _byGuid = new();
        private readonly HashSet<int> _used = new();
        private int _next = 1;

        // Returns the object's stable id, assigning the next free one on first sight.
        public int Resolve(Guid guid)
        {
            if (_byGuid.TryGetValue(guid, out int id))
            {
                return id;
            }

            while (!_used.Add(_next))
            {
                _next++;
            }

            _byGuid[guid] = _next;
            return _next;
        }

        // Claims a specific id for a guid (a patch add keeping the model's numbering, or a full
        // placement keeping the file's). No-op when the guid is known or the id is taken.
        public void Claim(Guid guid, int id)
        {
            if (_byGuid.ContainsKey(guid) || !_used.Add(id))
            {
                return;
            }

            _byGuid[guid] = id;
        }
    }
}
