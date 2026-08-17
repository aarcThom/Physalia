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
using Physalia.GH.Harness;

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

    // Set when an LLM placement could not keep the model's authored component ids (the restore
    // precondition failed, or the registry claims did not verify), consumed by the next canvas-state
    // fold so the model is TOLD the numbering moved instead of silently patching against ids only it
    // remembers. Weak-keyed and session-only, like the registry itself.
    private static readonly ConditionalWeakTable<GH_Document, object> PlacementNumberingLossFlags = new();

    /// <summary>
    /// One export of the user's canvas: the parsed document (the patch base), its serialized JSON
    /// (the grounding text), the checksum over that JSON, and the component count.
    /// </summary>
    /// <param name="Document">The exported document; the reference frame a ghpatch resolves against.</param>
    /// <param name="Json">The document serialized compactly, exactly as handed to the model.</param>
    /// <param name="Checksum">Structural fingerprint of <paramref name="Document"/> (<c>sha256-…</c>).</param>
    /// <param name="ComponentCount">Number of exported components; zero for an empty canvas.</param>
    /// <param name="GroupScoped">True when the export covers only the master group's contents.</param>
    internal sealed record CanvasStateSnapshot(
        GhJsonDocument Document,
        string Json,
        string Checksum,
        int ComponentCount,
        bool GroupScoped = false);

    /// <summary>
    /// Exports the current state of the user's canvas — every object whose type does not come from
    /// the Physalia assembly, which keeps the work product (native components, floating params
    /// placed by the Rhino Geometry tool, clusters, groups) and drops the Physalia pipeline itself.
    /// Returns null when there is no document to export.
    /// </summary>
    /// <param name="doc">The Grasshopper document to export; null falls back to the active canvas.</param>
    /// <param name="groupScope">
    /// True to export only the master group's contents (nested groups expanded) — the frame the
    /// group-scoped grounder shows the model. The master group itself — and the hint panel inviting
    /// the user to drop components into it — is excluded from BOTH frames: that is Physalia
    /// infrastructure, not part of the model's or the user's graph. Both frames use
    /// the same plain <c>sha256-…</c> checksum form (the GhJSON library's patch schema regex-rejects
    /// anything else); the patch path tells frames apart by matching the carried checksum against
    /// each frame's export (<see cref="ResolveBaseSnapshot"/>), never by the string's shape.
    /// </param>
    /// <param name="harness">
    /// The harness whose pipeline is asking, which is what a group-scoped export is scoped BY — each
    /// harness has its own master group. Ignored for the full frame.
    /// </param>
    /// <returns>The snapshot, or null when no document is available.</returns>
    internal static CanvasStateSnapshot? TryExportCanvasState(
        GH_Document? doc = null, bool groupScope = false, HarnessComponent? harness = null)
    {
        // Host-resolve whatever was handed in: a pipeline component inside a harness pings its own
        // sub-document, and grounding the model on the pipeline itself would be nonsense.
        doc = PhyDocuments.Host(doc) ?? PhyDocuments.ActiveHost();
        if (doc is null)
        {
            return null;
        }

        HashSet<Guid>? scope = groupScope ? MasterGroupScope(doc, harness) : null;
        var guids = doc.Objects
            .Where(o => o is not null
                && o.GetType().Assembly != typeof(PhyBase).Assembly
                && !IsMasterGroup(o)
                && !IsHintPanel(o)
                && (scope is null || scope.Contains(o.InstanceGuid)))
            .Select(o => o.InstanceGuid)
            .ToList();

        if (guids.Count == 0)
        {
            return new CanvasStateSnapshot(new GhJsonDocument(), string.Empty, string.Empty, 0, groupScope);
        }

        // Resolved against `doc` (already host-resolved above), not the active canvas: while the
        // user is inside a harness the canvas shows the pipeline, and the library's GetByGuids
        // would find none of these guids and hand the model an empty canvas.
        GhJsonDocument export = SerializeByGuids(doc, guids);

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
            export.Components?.Count ?? 0,
            groupScope);
    }

    /// <summary>
    /// Computes the drift-check fingerprint over the STRUCTURE of an exported canvas: the
    /// component set (stable id, instanceGuid, name), the wire topology, group membership, and each
    /// parameter's data-tree modifiers (graft/flatten, simplify, reverse, reparameterize, invert,
    /// unitize, principal, expression). Value-like state that cannot change the graph's meaning —
    /// pivots, slider/button/panel values, nicknames, internalized data — is deliberately excluded,
    /// so a user moving components or tweaking values while the model is generating does NOT
    /// invalidate its patch. Structural edits (adding, removing, renaming, rewiring, or regrouping
    /// components) and data-tree modifier changes (grafting or flattening a port) still change the
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

        foreach (GhJsonComponent component in export.Components
            .OrderBy(c => c.Id ?? int.MaxValue)
            .ThenBy(c => c.InstanceGuid))
        {
            AppendParamModifierLines(sb, component.Id, 'i', component.InputSettings);
            AppendParamModifierLines(sb, component.Id, 'o', component.OutputSettings);
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return "sha256-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Appends one fingerprint line per parameter that carries a data-tree modifier, so grafting or
    // flattening a port registers as drift. Params with no modifier emit nothing, so an unmodified
    // canvas keeps the checksum it had before modifiers were fingerprinted. Ordered by parameter
    // name so the library's parameter ordering cannot shuffle the fingerprint.
    private static void AppendParamModifierLines(
        StringBuilder sb, int? componentId, char side, IEnumerable<GhJsonParameterSettings>? settings)
    {
        if (settings is null)
        {
            return;
        }

        foreach (GhJsonParameterSettings s in settings
            .Where(HasAnyModifier)
            .OrderBy(s => s.ParameterName, StringComparer.Ordinal))
        {
            sb.Append("m:").Append(componentId).Append(':').Append(side).Append(':')
                .Append(s.ParameterName).Append(':')
                .Append(s.DataMapping?.ToLowerInvariant()).Append('|')
                .Append(s.IsSimplified == true ? 's' : '-')
                .Append(s.IsReversed == true ? 'r' : '-')
                .Append(s.IsReparameterized == true ? 'p' : '-')
                .Append(s.IsInverted == true ? 'i' : '-')
                .Append(s.IsUnitized == true ? 'u' : '-')
                .Append(s.IsPrincipal == true ? 'x' : '-')
                .Append('|').Append(s.Expression)
                .Append('\n');
        }
    }

    // True when a parameter carries any data-tree modifier the library round-trips.
    private static bool HasAnyModifier(GhJsonParameterSettings s) =>
        !string.IsNullOrEmpty(s.DataMapping)
        || s.IsSimplified == true
        || s.IsReversed == true
        || s.IsReparameterized == true
        || s.IsInverted == true
        || s.IsUnitized == true
        || s.IsPrincipal == true
        || !string.IsNullOrEmpty(s.Expression);

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
        registry.ClaimBatch(idToGuid);
    }

    /// <summary>
    /// Frees the stable ids of objects no longer on the document, so a fresh full-graph placement
    /// can keep the numbering it authored. Called only on that path: ids stay retired for the whole
    /// life of a patch conversation, where the model reasons from ids it saw on earlier turns and a
    /// recycled id would silently retarget its next patch. A full document supersedes that history —
    /// the model has just renumbered everything from 1 — and the canvas state it reads next carries
    /// whatever numbering actually resulted.
    /// </summary>
    /// <param name="doc">The document whose registry to compact; null is a no-op.</param>
    /// <returns>How many ids were released.</returns>
    internal static int ReleaseRetiredStableIds(GH_Document? doc)
    {
        if (doc is null || !StableIdRegistries.TryGetValue(doc, out StableIdRegistry? registry))
        {
            return 0;
        }

        return registry.ReleaseRetired(guid => doc.FindObject(guid, false) is not null);
    }

    /// <summary>
    /// Looks up the session-stable id an object was exported/placed under, without assigning one.
    /// False when the object has never been through an export or a claimed placement — callers
    /// (e.g. the Geometry Report) omit the id rather than mint numbering the model has not seen.
    /// </summary>
    /// <param name="doc">The document the object lives in; null returns false.</param>
    /// <param name="guid">The object's instanceGuid.</param>
    /// <param name="id">The stable id when known.</param>
    /// <returns>True when the object has a registered stable id.</returns>
    internal static bool TryGetStableId(GH_Document? doc, Guid guid, out int id)
    {
        id = 0;
        return doc is not null
            && StableIdRegistries.TryGetValue(doc, out StableIdRegistry? registry)
            && registry.TryGet(guid, out id);
    }

    /// <summary>
    /// Records that an LLM placement failed to keep the model's authored component ids on this
    /// document, so the next canvas-state fold warns the model that the numbering moved.
    /// </summary>
    /// <param name="doc">The document the placement landed on; null is a no-op.</param>
    internal static void MarkPlacementNumberingLoss(GH_Document? doc)
    {
        if (doc is not null)
        {
            PlacementNumberingLossFlags.AddOrUpdate(doc, new object());
        }
    }

    /// <summary>
    /// Consumes the placement-numbering-loss flag: true exactly once after a placement that lost
    /// the model's numbering, then false until it happens again. Called by the Conversation Log
    /// when folding the fresh canvas state, so the warning rides exactly the turn where the model
    /// would otherwise patch against ids only it remembers.
    /// </summary>
    /// <param name="doc">The document to check; null returns false.</param>
    /// <returns>True when a numbering loss was pending.</returns>
    internal static bool ConsumePlacementNumberingLoss(GH_Document? doc)
    {
        if (doc is null || !PlacementNumberingLossFlags.TryGetValue(doc, out _))
        {
            return false;
        }

        PlacementNumberingLossFlags.Remove(doc);
        return true;
    }

    // Rewrites the export's insertion-order ids (components, connection endpoints, groups and
    // their members) onto the session-stable ids. Two passes: the old->stable map is computed
    // from the original ids first, then applied, so a swapped pair cannot alias mid-rewrite.
    private static void AssignStableIds(GhJsonDocument export, GH_Document doc)
    {
        StableIdRegistry registry = StableIdRegistries.GetOrCreateValue(doc);

        // Second chance for model-authored numbering: objects the authored-placement ledger knows
        // (placed from the model's own submissions) but the registry has never seen claim their
        // authored ids before anything resolves fresh — so even a placement whose registry claims
        // were lost still exports under the model's numbering wherever those ids are free. A
        // pre-pass, so a fresh Resolve cannot mint an id the ledger is about to claim.
        if (AuthoredPlacementLedgers.TryGetValue(doc, out Dictionary<Guid, int>? ledger))
        {
            foreach (GhJsonComponent component in export.Components ?? Enumerable.Empty<GhJsonComponent>())
            {
                if (component.InstanceGuid is Guid guid && ledger.TryGetValue(guid, out int authoredId))
                {
                    registry.Claim(guid, authoredId);
                }
            }
        }

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

        // Looks up the object's stable id without assigning one.
        public bool TryGet(Guid guid, out int id) => _byGuid.TryGetValue(guid, out id);

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

        // Claims the authored id for each freshly placed object, as one batch.
        //
        // Two phases, and the first is essential. GhJsonGrasshopper.Put calls NewSolution
        // internally, the CanvasStateGrounder exports canvas state on EVERY solve, and that export
        // calls Resolve — so by the time a placement reaches this method, every object it just
        // created already holds an export-order id. Those interim ids overlap the authored ones the
        // batch is about to claim (48 objects holding 5..52 while asking for 1..50), so claiming
        // one at a time refuses every single time: the id wanted is "taken" by another object in
        // the same batch. That is the 1-of-48 in the 2026-07-25 23:19 session, and the tidy +4
        // offset in the 23:03 one — the model's components were numbered by export order, not by
        // its own authoring.
        //
        // Releasing the interim ids first is safe precisely because they are interim: these guids
        // were created moments ago by this placement, and the export the model actually reads
        // happens after this. Ids belonging to anything NOT in the batch are never touched, so a
        // number the model has already seen can neither move nor be handed to a different object.
        public void ClaimBatch(IEnumerable<KeyValuePair<int, Guid>> idToGuid)
        {
            List<KeyValuePair<int, Guid>> pairs = idToGuid.ToList();

            foreach (KeyValuePair<int, Guid> pair in pairs)
            {
                if (_byGuid.TryGetValue(pair.Value, out int interim))
                {
                    _byGuid.Remove(pair.Value);
                    _used.Remove(interim);
                }
            }

            foreach (KeyValuePair<int, Guid> pair in pairs)
            {
                Claim(pair.Value, pair.Key);
            }
        }

        // Claims a specific id for a guid. Refused when the guid is already registered or when a
        // DIFFERENT object holds the id (live or retired) — stealing a retired id would silently
        // retarget a patch that still referenced it.
        public void Claim(Guid guid, int id)
        {
            if (_byGuid.ContainsKey(guid) || !_used.Add(id))
            {
                return;
            }

            _byGuid[guid] = id;
        }

        // Frees every id whose object is no longer on the document. Retiring ids forever is what
        // keeps a patch safe (a stale id resolves to nothing instead of to a different component),
        // but it also means a whole graph placed and then deleted burns its numbers for the rest of
        // the session — and a full document always numbers from 1, so the SECOND full placement in a
        // session could never keep its authored ids. That is the 1-of-48 in the 2026-07-25 23:19
        // session: the previous run's graph had already claimed 1..48 and been deleted. Live objects
        // keep their ids, so nothing the model can currently see is ever renumbered by this.
        public int ReleaseRetired(Predicate<Guid> isAlive)
        {
            List<Guid> retired = _byGuid.Keys.Where(guid => !isAlive(guid)).ToList();
            foreach (Guid guid in retired)
            {
                _used.Remove(_byGuid[guid]);
                _byGuid.Remove(guid);
            }

            _next = 1;
            return retired.Count;
        }
    }
}
