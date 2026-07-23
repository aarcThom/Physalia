// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GhJSON.Core;
using GhJSON.Core.SchemaModels;
using Grasshopper.Kernel;

namespace Physalia.GH.Generation;

/// <summary>
/// Post-placement fidelity verification for the Fidelity Check guardrail: did the live canvas end
/// up matching the definition the model authored? The diff runs against the LIVE document with
/// the same endpoint resolver placement used to create the wires (<c>FindEndpointParam</c>), so a
/// reported discrepancy is a true miss by construction. Identity comes from the authored-placement
/// ledger — placed instanceGuid → the authored id it realised — recorded at placement time. The
/// stable-id registry cannot serve that role: it retires ids forever, so a corrective
/// resubmission's export ids routinely drift from the model's own numbering. Groups are out of
/// scope: created groups are not reliably reported in the placed set, and group drift is cosmetic
/// for solve correctness (and fingerprinted by the canvas checksum anyway).
/// </summary>
internal static partial class GhJsonBridge
{
    // Placed instanceGuid -> the authored GhJSON id it was placed under. Written by the LLM
    // full-graph placement path (library components only when RestoreAuthoredIds held; clusters
    // always). Weak-keyed per document and session-only, like the stable-id registry. Guids are
    // regenerated per placement, so entries never collide across turns or transmitters; stale
    // entries for removed objects are harmless (lookups are scoped to one turn's placed guids).
    private static readonly ConditionalWeakTable<GH_Document, Dictionary<Guid, int>> AuthoredPlacementLedgers = new();

    // The authored full-graph GhJSON of the most recent LLM placement, with the exact guid set it
    // placed. Lets the Fidelity Check self-source its Definition when its input is unwired or
    // miswired (the observed failure mode: a markdown wire that never parses). The guid set gates
    // the fallback — it may only verify THE placement it recorded, never a later turn — and any
    // applied ghpatch invalidates the record, because the canvas has legitimately evolved past it.
    private static readonly ConditionalWeakTable<GH_Document, AuthoredDefinition> AuthoredDefinitions = new();

    /// <summary>
    /// Records the authored definition a successful full-graph placement realised, so the Fidelity
    /// Check can fall back to it when its Definition input is unwired or miswired.
    /// </summary>
    /// <param name="doc">The host document; null is tolerated (nothing recorded).</param>
    /// <param name="json">The authored full-graph GhJSON exactly as placed.</param>
    /// <param name="placedGuids">The instanceGuids that placement reported.</param>
    internal static void RecordAuthoredDefinition(GH_Document? doc, string json, IEnumerable<Guid> placedGuids)
    {
        if (doc is null || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        AuthoredDefinitions.AddOrUpdate(doc, new AuthoredDefinition(json, new HashSet<Guid>(placedGuids)));
    }

    /// <summary>
    /// Drops the recorded authored definition — called when a ghpatch applies, because the canvas
    /// has legitimately evolved beyond the recorded full graph and verifying against it would
    /// report the patch's own edits as fidelity violations.
    /// </summary>
    /// <param name="doc">The host document; null is tolerated.</param>
    internal static void InvalidateAuthoredDefinition(GH_Document? doc)
    {
        if (doc is not null)
        {
            AuthoredDefinitions.Remove(doc);
        }
    }

    /// <summary>
    /// Returns the recorded authored definition when — and only when — the trigger's placed guids
    /// are exactly the set that placement recorded, i.e. the caller is verifying the same turn.
    /// </summary>
    /// <param name="doc">The host document.</param>
    /// <param name="triggerGuids">The placed guids carried by the trigger signal.</param>
    /// <returns>The recorded GhJSON, or null when absent or from a different turn.</returns>
    internal static string? TryGetAuthoredDefinition(GH_Document? doc, IReadOnlyCollection<Guid> triggerGuids)
    {
        if (doc is null
            || triggerGuids.Count == 0
            || !AuthoredDefinitions.TryGetValue(doc, out AuthoredDefinition? recorded)
            || !recorded.PlacedGuids.SetEquals(triggerGuids))
        {
            return null;
        }

        return recorded.Json;
    }

    // CWT values must be reference types; the guid set gates the fallback to the recorded turn.
    private sealed record AuthoredDefinition(string Json, HashSet<Guid> PlacedGuids);

    /// <summary>
    /// Records which authored id each placed object realised, for later fidelity verification.
    /// </summary>
    /// <param name="doc">The host document; null is tolerated (nothing recorded).</param>
    /// <param name="idToGuid">Authored id → placed instanceGuid pairs.</param>
    internal static void RecordAuthoredPlacement(GH_Document? doc, IEnumerable<KeyValuePair<int, Guid>> idToGuid)
    {
        if (doc is null)
        {
            return;
        }

        Dictionary<Guid, int> ledger = AuthoredPlacementLedgers.GetOrCreateValue(doc);
        foreach (KeyValuePair<int, Guid> pair in idToGuid)
        {
            ledger[pair.Value] = pair.Key;
        }
    }

    /// <summary>
    /// Counts the live document objects that were placed from the model's own submissions (present
    /// in the authored-placement ledger and still on the canvas). Drives the canvas grounding's
    /// provenance line, so the model is told outright whether anything of its authorship is live
    /// instead of inferring placement status — that inference is what makes corrective turns
    /// wobble between full-document and ghpatch modes.
    /// </summary>
    /// <param name="doc">The document to count against; null returns zero.</param>
    /// <returns>The number of model-placed components still alive on the document.</returns>
    internal static int CountModelPlaced(GH_Document? doc)
    {
        if (doc is null || !AuthoredPlacementLedgers.TryGetValue(doc, out Dictionary<Guid, int>? ledger))
        {
            return 0;
        }

        int count = 0;
        foreach (Guid guid in ledger.Keys)
        {
            if (doc.FindObject(guid, false) is not null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Enumerates the live document objects that were placed from the model's own submissions —
    /// the authored-placement ledger entries (full-graph placements and ghpatch adds alike) still
    /// alive on the canvas. Drives the Geometry Snapshot grounding's "generated geometry" scope.
    /// </summary>
    /// <param name="doc">The document to enumerate against; null returns nothing.</param>
    /// <returns>The instanceGuids of the model-placed components still on the document.</returns>
    internal static IEnumerable<Guid> ModelPlacedGuids(GH_Document? doc)
    {
        if (doc is null || !AuthoredPlacementLedgers.TryGetValue(doc, out Dictionary<Guid, int>? ledger))
        {
            yield break;
        }

        foreach (Guid guid in ledger.Keys)
        {
            if (doc.FindObject(guid, false) is not null)
            {
                yield return guid;
            }
        }
    }

    /// <summary>
    /// The result of a fidelity verification: one violation line per discrepancy, plus the fresh
    /// base checksum when there are violations (the placed-but-wrong graph is live, so the model's
    /// remembered checksum is stale for the corrective patch). A non-null
    /// <paramref name="Misconfiguration"/> means the check could not run at all for a reason that
    /// can never be the model's fault (the Definition input does not parse — yet the definition
    /// that PLACED always parses — or no live document): the component surfaces it as a LOCAL
    /// error and passes the trigger through, never as model feedback.
    /// </summary>
    /// <param name="Violations">One model-facing line per discrepancy; empty when faithful.</param>
    /// <param name="BaseChecksum">The current canvas checksum, non-null only on violations.</param>
    /// <param name="Misconfiguration">Human-facing wiring/setup problem, or null when the check ran.</param>
    internal sealed record FidelityReport(IReadOnlyList<string> Violations, string? BaseChecksum, string? Misconfiguration = null);

    /// <summary>
    /// Verifies that the components and connections of an authored full-graph GhJSON document are
    /// realised on the live canvas. Components are matched through the authored-placement ledger
    /// (falling back to a conservative name-claim among the placed guids when a node is not in the
    /// ledger — matched-but-ambiguous nodes have their wires skipped rather than guessed at).
    /// Reference nodes resolve to the pre-existing live parameters they stand for; cluster nodes
    /// resolve through the ledger like ordinary nodes.
    /// </summary>
    /// <param name="authoredJson">The authored full-graph GhJSON document (not a ghpatch).</param>
    /// <param name="placedGuids">The instanceGuids the transmitter reported placing this turn.</param>
    /// <param name="doc">The live document; null falls back to the active canvas.</param>
    /// <returns>The fidelity report.</returns>
    internal static FidelityReport VerifyPlacementFidelity(
        string authoredJson,
        IReadOnlyCollection<Guid> placedGuids,
        GH_Document? doc = null)
    {
        doc ??= Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc is null)
        {
            return new FidelityReport(
                Array.Empty<string>(),
                null,
                "The live Grasshopper document is unavailable, so fidelity could not be verified.");
        }

        GhJsonDocument authored;
        try
        {
            authored = GhJson.FromJson(authoredJson);
        }
        catch (Exception ex)
        {
            // This can NEVER be the model's fault: the check only triggers after a successful
            // placement, and a definition that placed always parses. A non-parsing Definition is
            // definitionally a mis-wire — the user's problem, surfaced locally, never routed into
            // the conversation.
            return new FidelityReport(
                Array.Empty<string>(),
                null,
                "The Definition input did not parse as GhJSON (" + ex.Message + "). The definition "
                + "that placed always parses, so this input is wired to the wrong output — wire it "
                + "to the same signal wire the Component Transmitter's Signal input consumes.");
        }

        if (authored.Components is null || authored.Components.Count == 0)
        {
            return new FidelityReport(Array.Empty<string>(), null);
        }

        var violations = new List<string>();
        var failedIds = new HashSet<int>();        // component-level root causes; their wires are skipped
        var unverifiableIds = new HashSet<int>();  // ambiguous matches; their wires are skipped, not guessed

        // ---- Classify: reference nodes resolve to pre-existing live params (deliberately NOT in
        // placedGuids); everything else — clusters included — resolves through the ledger.
        var referenceParams = new Dictionary<int, IGH_Param>();
        var ordinary = new List<GhJsonComponent>();
        ClassifyForFidelity(doc, authored.Components, referenceParams, ordinary, violations, failedIds);

        // ---- Component check, through the ledger scoped to this turn's placed guids.
        AuthoredPlacementLedgers.TryGetValue(doc, out Dictionary<Guid, int>? ledger);
        var liveByAuthoredId = new Dictionary<int, IGH_DocumentObject>();
        var goneAuthoredIds = new HashSet<int>();
        bool anyLedgerHit = false;
        foreach (Guid guid in placedGuids)
        {
            if (ledger is null || !ledger.TryGetValue(guid, out int authoredId))
            {
                continue;
            }

            anyLedgerHit = true;
            if (doc.FindObject(guid, false) is IGH_DocumentObject obj)
            {
                liveByAuthoredId[authoredId] = obj;
            }
            else
            {
                goneAuthoredIds.Add(authoredId);
            }
        }

        var claimed = new HashSet<Guid>(liveByAuthoredId.Values.Select(o => o.InstanceGuid));
        foreach (GhJsonComponent component in ordinary)
        {
            if (component.Id is not int id)
            {
                continue; // no authored identity to verify against — nothing safe to claim
            }

            if (liveByAuthoredId.ContainsKey(id))
            {
                continue; // matched — the ledger is ground truth; a name delta means the resolver renamed it
            }

            if (goneAuthoredIds.Contains(id))
            {
                violations.Add(
                    $"Component '{component.Name}' (id {id}) was placed but is no longer on the canvas — it was removed after placement.");
                failedIds.Add(id);
                continue;
            }

            // Fallback for a node outside the ledger (skipped by Put, or the whole placement lost
            // its authored numbering): claim an unclaimed placed object by name. A claim is a
            // match we cannot address precisely, so its wires are skipped rather than guessed at.
            IGH_DocumentObject? byName = placedGuids
                .Where(g => !claimed.Contains(g))
                .Select(g => doc.FindObject(g, false))
                .FirstOrDefault(o => o is not null
                    && (string.Equals(o.Name, component.Name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(o.NickName, component.Name, StringComparison.OrdinalIgnoreCase)));
            if (byName is not null)
            {
                claimed.Add(byName.InstanceGuid);
                unverifiableIds.Add(id);
            }
            else
            {
                violations.Add(
                    $"Component '{component.Name}' (id {id}) from your definition was not placed on the canvas. Check its name against the component catalog and resubmit.");
                failedIds.Add(id);
            }
        }

        if (!anyLedgerHit && placedGuids.Count > 0 && unverifiableIds.Count > 0)
        {
            violations.Add(
                "Note: connection fidelity could not be verified this turn (the authored component ids could not be preserved through placement); only component existence was checked.");
        }

        // ---- Connection check, with the same resolver that created the wires.
        var authoredIds = new HashSet<int>(authored.Components.Where(c => c.Id is int).Select(c => c.Id!.Value));
        var authoredById = authored.Components.Where(c => c.Id is int).ToDictionary(c => c.Id!.Value);
        foreach (GhJsonConnection conn in authored.Connections ?? Enumerable.Empty<GhJsonConnection>())
        {
            if (conn.From is not { } from || conn.To is not { } to)
            {
                continue;
            }

            if (failedIds.Contains(from.Id) || failedIds.Contains(to.Id)
                || unverifiableIds.Contains(from.Id) || unverifiableIds.Contains(to.Id))
            {
                continue; // root cause already reported, or match too ambiguous to judge
            }

            if (!authoredIds.Contains(from.Id) || !authoredIds.Contains(to.Id))
            {
                int bogus = authoredIds.Contains(from.Id) ? to.Id : from.Id;
                violations.Add(
                    $"Connection endpoint id {bogus} does not correspond to any component in your definition.");
                continue;
            }

            IGH_DocumentObject? fromObj = ResolveLive(from.Id, liveByAuthoredId, referenceParams);
            IGH_DocumentObject? toObj = ResolveLive(to.Id, liveByAuthoredId, referenceParams);
            if (fromObj is null || toObj is null)
            {
                continue; // endpoint had no authored identity to resolve — never flag on a guess
            }

            IGH_Param? source = FindEndpointParam(fromObj, from, output: true, preferIndex: true);
            IGH_Param? sink = FindEndpointParam(toObj, to, output: false, preferIndex: true);
            if (source is null || sink is null)
            {
                string side = source is null ? "output" : "input";
                string param = (source is null ? from.ParamName : to.ParamName) ?? "?";
                violations.Add(
                    $"Connection {DescribeEndpoint(authoredById, from, fromObj, output: true)} -> {DescribeEndpoint(authoredById, to, toObj, output: false)}: "
                    + $"the {side} parameter '{param}' does not exist on its component. Check the paramIndex against the component's signature.");
            }
            else if (!sink.Sources.Contains(source))
            {
                violations.Add(
                    $"Connection {DescribeEndpoint(authoredById, from, fromObj, output: true)} -> {DescribeEndpoint(authoredById, to, toObj, output: false)} "
                    + "is missing on the canvas. Re-emit this connection with correct endpoint ids and paramIndex values.");
            }
        }

        return violations.Count == 0
            ? new FidelityReport(Array.Empty<string>(), null)
            : Report(doc, violations);
    }

    // Partitions authored components into reference nodes (resolved to their live canvas params,
    // mirroring ExtractReferences: explicit physalia.reference name first, nickname-implied second)
    // and ordinary nodes (clusters included — they resolve through the ledger). An explicit
    // reference that no longer names a live input is a violation, same wording as placement used.
    private static void ClassifyForFidelity(
        GH_Document doc,
        IReadOnlyList<GhJsonComponent> components,
        IDictionary<int, IGH_Param> referenceParams,
        ICollection<GhJsonComponent> ordinary,
        ICollection<string> violations,
        ISet<int> failedIds)
    {
        var liveByName = new Dictionary<string, IGH_Param>(StringComparer.OrdinalIgnoreCase);
        foreach (ReferencedRhinoGeometry input in CanvasRhinoReferences.Collect(doc))
        {
            if (!string.IsNullOrWhiteSpace(input.Name))
            {
                liveByName[input.Name] = input.LiveOutput;
            }
        }

        foreach (GhJsonComponent component in components)
        {
            if (component.Id is not int id)
            {
                ordinary.Add(component);
                continue;
            }

            string? explicitName = ReadReferenceName(component);
            if (explicitName is not null)
            {
                if (liveByName.TryGetValue(explicitName, out IGH_Param? live))
                {
                    referenceParams[id] = live;
                }
                else
                {
                    violations.Add(UnresolvedReferenceMessage(explicitName, liveByName.Keys));
                    failedIds.Add(id);
                }
            }
            else if (!string.IsNullOrWhiteSpace(component.NickName) && liveByName.TryGetValue(component.NickName!, out IGH_Param? implied))
            {
                referenceParams[id] = implied;
            }
            else
            {
                ordinary.Add(component);
            }
        }
    }

    private static IGH_DocumentObject? ResolveLive(
        int id,
        IReadOnlyDictionary<int, IGH_DocumentObject> liveByAuthoredId,
        IReadOnlyDictionary<int, IGH_Param> referenceParams)
    {
        if (liveByAuthoredId.TryGetValue(id, out IGH_DocumentObject? obj))
        {
            return obj;
        }

        return referenceParams.TryGetValue(id, out IGH_Param? param) ? param : null;
    }

    // "'Circle' (id 3, guid 6f2e...) output 'C' (paramIndex 0)" — both identity frames, so the
    // model can map its own numbering onto the live instanceGuid it must patch by.
    private static string DescribeEndpoint(
        IReadOnlyDictionary<int, GhJsonComponent> authoredById,
        GhJsonConnectionEndpoint endpoint,
        IGH_DocumentObject obj,
        bool output)
    {
        string name = authoredById.TryGetValue(endpoint.Id, out GhJsonComponent? node) && node.Name is { } n
            ? n
            : obj.Name;
        string side = output ? "output" : "input";
        string param = endpoint.ParamName ?? "?";
        string index = endpoint.ParamIndex?.ToString() ?? "?";
        return $"'{name}' (id {endpoint.Id}, guid {obj.InstanceGuid}) {side} '{param}' (paramIndex {index})";
    }

    private static FidelityReport Report(GH_Document doc, IReadOnlyList<string> violations) =>
        new FidelityReport(violations, TryExportCanvasState(doc)?.Checksum);
}
