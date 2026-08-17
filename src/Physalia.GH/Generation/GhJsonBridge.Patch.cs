// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using GhJSON.Core;
using GhJSON.Core.DiffOperations;
using GhJSON.Core.PatchModels;
using GhJSON.Core.SchemaModels;
using GhJSON.Core.Validation;
using GhJSON.Grasshopper;
using GhJSON.Grasshopper.DeleteOperations;
using GhJSON.Grasshopper.PutOperations;
using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;
using Physalia.GH.Components;
using Physalia.GH.Harness;
using GHGroupObject = Grasshopper.Kernel.Special.GH_Group;
using GHNumberSlider = Grasshopper.Kernel.Special.GH_NumberSlider;
using GHPanel = Grasshopper.Kernel.Special.GH_Panel;

namespace Physalia.GH.Generation;

/// <summary>
/// Result returned by <see cref="GhJsonBridge.ApplyPatchToCanvas"/>. Conflicts follow the ghpatch
/// "apply what can be applied, report the rest" policy: a non-empty conflict list means those
/// operations did NOT land while everything else DID, so the caller routes Fail with feedback that
/// tells the model to resubmit only the corrected operations.
/// </summary>
internal sealed record CanvasPatchOutcome(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<Guid> AddedGuids,
    IReadOnlyList<Guid> ModifiedGuids,
    IReadOnlyList<Guid> RemovedGuids,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Warnings,
    string? PostApplyChecksum = null,
    IReadOnlyList<string>? AppliedOps = null);

/// <summary>
/// Ghpatch half of the façade: applies a model-authored <c>.ghpatch</c> document to the LIVE
/// canvas. The patch is first applied document-level by the GhJSON library (one source of truth
/// for merge semantics and conflicts), then reconciled onto the live objects: removes are batch
/// deleted, simple modifies (slider value, panel text, nickname, pivot, locked/hidden) mutate the
/// live object directly, anything richer replaces the object with its post-merge state while
/// preserving its InstanceGuid and captured wires, adds are placed as a subgraph and wired to the
/// existing objects they reference, and connection/group operations are executed with the GH SDK.
/// The canvas itself is the source of truth for identity — no session bookkeeping survives here.
/// </summary>
internal static partial class GhJsonBridge
{
    /// <summary>
    /// Applies a ghpatch to the active canvas. Re-exports the user's canvas as the patch's base
    /// reference frame (the same export the grounder showed the model), optionally drift-checks the
    /// patch's carried checksum against it, applies the patch document-level for merge semantics
    /// and conflicts, then reconciles the result onto the live objects.
    /// </summary>
    /// <param name="patchJson">The ghpatch document as a string.</param>
    /// <param name="fallbackOrigin">Canvas position for added components that carry no pivot.</param>
    /// <param name="verifyBase">
    /// True to refuse the patch when its <c>patch.base.checksum</c> no longer matches a fresh
    /// export (the canvas changed since the model last saw it); false applies regardless.
    /// </param>
    /// <param name="harness">
    /// The harness whose pipeline authored the patch: its master group is the group-scoped reference
    /// frame the patch is resolved against, and where anything the patch adds is enrolled.
    /// </param>
    /// <returns>A <see cref="CanvasPatchOutcome"/> describing the outcome.</returns>
    internal static CanvasPatchOutcome ApplyPatchToCanvas(
        string patchJson, PointF fallbackOrigin, bool verifyBase, HarnessComponent? harness)
    {
        GH_Document? doc = PhyDocuments.ActiveHost();
        if (doc is null)
        {
            return PatchFailure("No active Grasshopper document to apply the patch to.");
        }

        GhPatchDocument patch;
        try
        {
            patch = GhJson.PatchFromJson(patchJson);
        }
        catch (Exception ex)
        {
            return PatchFailure("The patch could not be parsed: " + ex.Message);
        }

        // A patch with no operations is a no-op: nothing will be applied, so canvas drift is
        // irrelevant — succeed before the checksum gate. The model emits one when it concludes no
        // further work is needed; refusing it over a stale base (its own earlier ops advanced the
        // canvas) traps the loop in a mismatch -> regenerate -> empty-patch cycle.
        if (!HasOperations(patch))
        {
            return new CanvasPatchOutcome(
                true,
                null,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<string>(),
                new[] { "The patch contained no operations; the canvas is unchanged." });
        }

        // The base frame is resolved by matching the carried checksum against each frame's export
        // (full canvas vs master-group scope) — ids resolve identically in both (one registry per
        // document), but the drift check must never compare checksums across frames. Matching, not
        // a checksum prefix: the GhJSON library's patch schema regex-rejects any non-standard
        // checksum shape, so the string itself cannot carry the frame.
        string? carried = patch.Patch?.Base?.Checksum?.Trim();
        CanvasStateSnapshot? snapshot = ResolveBaseSnapshot(doc, harness, carried);
        if (snapshot is null)
        {
            return PatchFailure("The canvas state could not be exported.");
        }

        bool groupFrame = snapshot.GroupScoped;
        GhJsonDocument baseExport = snapshot.Document;

        if (verifyBase && !string.IsNullOrEmpty(carried)
            && !string.Equals(carried, snapshot.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            // The feedback carries the fresh checksum but NOT the canvas state: the Conversation Log
            // re-exports the canvas into the system prompt at the moment this feedback is folded, so
            // the state is already in the same request — embedding a second copy here doubled the
            // canvas cost of every mismatch round for nothing. Kept as Payload text — carrier
            // discipline holds (no new signal field).
            var mismatch = new StringBuilder();
            mismatch.AppendLine(
                "base_checksum_mismatch: the canvas changed since you last saw it, so this patch was NOT "
                + "applied. Regenerate the patch against the CURRENT canvas state in your instructions.");
            mismatch.AppendLine();
            mismatch.Append("Base checksum — copy this verbatim into patch.base.checksum: " + snapshot.Checksum);

            if (snapshot.ComponentCount == 0)
            {
                mismatch.AppendLine();
                mismatch.AppendLine();
                mismatch.Append(groupFrame
                    ? "The Physalia group is now empty — emit a full GhJSON document instead of a patch."
                    : "The canvas is now empty — emit a full GhJSON document instead of a patch.");
            }

            return PatchFailure(mismatch.ToString());
        }

        var conflicts = new List<string>();
        var warnings = new List<string>();
        var addedGuids = new List<Guid>();
        var modifiedGuids = new List<Guid>();
        var removedGuids = new List<Guid>();
        var appliedOps = new List<string>();

        // ---- Pre-pass on adds: stamp real component guids, remap colliding ids, and replace the
        // authored pivots with a relaid-out group anchored beside the LLM-built graph. All before
        // the document-level apply so the post-merge document carries the corrected components.
        // (Rhino-referenced inputs need no reference pre-pass: they are ordinary components in the
        // canvas state, marked physalia.rhinoRef, and the model wires to them by id like anything
        // else.)
        List<GhJsonComponent> adds = patch.Patch?.Components?.Add ?? new List<GhJsonComponent>();
        if (adds.Count > 0)
        {
            StampComponentGuids(new GhJsonDocument("1.0", null, adds, null, null));
            RemapCollidingAddIds(patch, adds, baseExport);
            AnchorPatchAdds(patch, adds, doc, baseExport, fallbackOrigin);

            // The added components' required-input lint now lives in the Required Input Check
            // guardrail (via GhJsonBridge.LintRequiredInputsJson), an explicit upstream node — the
            // patch path no longer refuses missing required inputs inline.
        }

        // ---- Document-level apply: the library owns merge semantics, identity resolution and
        // conflict reporting. Drift is checked above against the exact grounding text, so the
        // library's canonical-checksum verification stays off. Colliding add ids were pre-remapped,
        // so the library's renumbering (which does not remap connection endpoints) never triggers.
        ApplyPatchResult applyResult;
        try
        {
            applyResult = GhJson.ApplyPatch(baseExport, patch, new ApplyPatchOptions
            {
                VerifyBase = false,
                ContinueOnConflict = true,
                RenumberCollidingAddedIds = false,
            });
        }
        catch (Exception ex)
        {
            return PatchFailure("The patch could not be applied: " + ex.Message);
        }

        // The canvas reconciliation below re-validates every connection operation itself
        // (WireConnection, paramIndex-first) and reports its own conflicts, so the library's
        // connection verdicts are redundant — and wrong for endpoints addressed by paramIndex
        // only: the grounding tells the model to wire by integer id + paramIndex, but the 1.1.1
        // library matches connection removes by paramName and reports ConnectionNotFound for
        // name-less endpoints even when the wire exists and IS removed on the canvas. Keeping
        // those verdicts falsely fails the outcome ("your ops did NOT apply" when they did).
        // The library stays authoritative for component and group merge conflicts only.
        conflicts.AddRange(applyResult.Conflicts
            .Select(c => c.ToString())
            .Where(c => !c.Contains("at connections.", StringComparison.Ordinal)));

        // ---- Canvas reconciliation, in the spec's apply order (modify, remove, add, groups after
        // adds so members resolve, connection removes, connection adds).
        IsImporting = true;
        try
        {
            var baseIndex = BuildBaseIndex(baseExport);
            var addIds = new HashSet<int>(adds.Where(a => a.Id is int).Select(a => a.Id!.Value));

            ApplyModifies(patch, doc, baseExport, applyResult.Document, baseIndex, modifiedGuids, conflicts, warnings, appliedOps);
            ApplyRemoves(patch, baseExport, baseIndex, removedGuids, conflicts);

            Dictionary<int, IGH_DocumentObject> addedById =
                ApplyAdds(patch, adds, addIds, doc, baseIndex, addedGuids, modifiedGuids, conflicts, warnings);

            // Record the adds in the authored-placement ledger: the model authored these ids, and
            // the canvas grounding's provenance line (and future patch-mode fidelity) reads them.
            RecordAuthoredPlacement(doc, addedById.Select(p => new KeyValuePair<int, Guid>(p.Key, p.Value.InstanceGuid)));

            List<Guid> createdGroups = ApplyGroupOps(patch, doc, baseExport, baseIndex, addedById, conflicts);
            ApplyConnectionRemoves(patch, doc, addIds, baseIndex, removedGuids, modifiedGuids, conflicts, warnings);
            ApplyConnectionAdds(patch, doc, addIds, addedById, baseIndex, modifiedGuids, conflicts);

            // Everything this patch added — components and the groups it organized them into —
            // lands in the master "Physalia" group, keeping the shared workspace complete.
            EnrollPlaced(doc, harness, addedGuids, createdGroups);

            // Group membership is final here, so every area the patch touched can be rebuilt around
            // its new members — inserting them into the data flow and pushing the existing downstream
            // components right — and any remaining overlap nudged out.
            SeparatePlacedOverlaps(doc, addedGuids);

            doc.NewSolution(false);
            Grasshopper.Instances.ActiveCanvas?.Refresh();
        }
        catch (Exception ex)
        {
            conflicts.Add("The patch was only partially applied: " + ex.Message);
        }
        finally
        {
            IsImporting = false;
        }

        // The canvas has evolved past the last recorded full-graph placement — a Fidelity Check
        // falling back to that record now would report this patch's own edits as violations.
        InvalidateAuthoredDefinition(doc);

        // A partial apply means the model must resubmit — and the operations that DID land changed
        // the canvas, so its old base checksum is guaranteed stale. Hand the fresh one back in the
        // outcome so the feedback can carry it and the retry cannot mismatch.
        string? postApplyChecksum = conflicts.Count > 0 ? TryExportCanvasState(doc, groupFrame, harness)?.Checksum : null;

        return new CanvasPatchOutcome(
            conflicts.Count == 0,
            null,
            addedGuids,
            modifiedGuids.Distinct().ToList(),
            removedGuids,
            conflicts,
            warnings,
            postApplyChecksum,
            appliedOps);
    }

    // True when the patch carries at least one operation; a fully empty patch is a no-op.
    private static bool HasOperations(GhPatchDocument patch) =>
        (patch.Patch?.Components?.Add?.Count ?? 0) > 0
        || (patch.Patch?.Components?.Remove?.Count ?? 0) > 0
        || (patch.Patch?.Components?.Modify?.Count ?? 0) > 0
        || (patch.Patch?.Connections?.Add?.Count ?? 0) > 0
        || (patch.Patch?.Connections?.Remove?.Count ?? 0) > 0
        || (patch.Patch?.Groups?.Add?.Count ?? 0) > 0
        || (patch.Patch?.Groups?.Modify?.Count ?? 0) > 0
        || (patch.Patch?.Groups?.Remove?.Count ?? 0) > 0;

    private static CanvasPatchOutcome PatchFailure(string message) => new CanvasPatchOutcome(
        false,
        message,
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        new[] { message },
        Array.Empty<string>());

    // Index of the base export: component id -> component, and instanceGuid -> component.
    private sealed record BaseIndex(
        IReadOnlyDictionary<int, GhJsonComponent> ById,
        IReadOnlyDictionary<Guid, GhJsonComponent> ByGuid);

    private static BaseIndex BuildBaseIndex(GhJsonDocument baseExport)
    {
        var byId = new Dictionary<int, GhJsonComponent>();
        var byGuid = new Dictionary<Guid, GhJsonComponent>();
        foreach (GhJsonComponent component in baseExport.Components ?? Enumerable.Empty<GhJsonComponent>())
        {
            if (component.Id is int id)
            {
                byId[id] = component;
            }

            if (component.InstanceGuid is Guid guid)
            {
                byGuid[guid] = component;
            }
        }

        return new BaseIndex(byId, byGuid);
    }

    // Resolves a component match against the base export with the spec's identity precedence:
    // instanceGuid, then id, then structural fingerprint (componentGuid + name, unique-match only).
    // Returns null when nothing (or more than one candidate) matches; the document-level apply has
    // already reported that as a conflict, so no message is added here.
    private static GhJsonComponent? ResolveComponentMatch(GhPatchComponentMatch match, GhJsonDocument baseExport, BaseIndex index)
    {
        if (match.InstanceGuid is Guid guid)
        {
            return index.ByGuid.TryGetValue(guid, out GhJsonComponent? byGuid) ? byGuid : null;
        }

        if (match.Id is int id)
        {
            return index.ById.TryGetValue(id, out GhJsonComponent? byId) ? byId : null;
        }

        var candidates = (baseExport.Components ?? Enumerable.Empty<GhJsonComponent>())
            .Where(c => match.ComponentGuid is not Guid cg || c.ComponentGuid == cg)
            .Where(c => match.Name is null || string.Equals(c.Name, match.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count > 1 && match.Pivot is not null)
        {
            candidates = candidates
                .Where(c => c.Pivot is not null
                    && Math.Abs(c.Pivot.X - match.Pivot.X) < 0.5
                    && Math.Abs(c.Pivot.Y - match.Pivot.Y) < 0.5)
                .ToList();
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    // Assigns fresh ids to added components whose id is missing, collides with a base-export id, or
    // duplicates an earlier add, and rewrites every patch reference to the old id. This keeps the
    // library's RenumberCollidingAddedIds path (which renumbers WITHOUT remapping connection
    // endpoints — a known 1.1.1 bug) from ever triggering. An endpoint carrying a collided id is
    // interpreted as referring to the ADD, per the spec's rewriting rule; the preamble additionally
    // instructs the model to number adds above the highest id in the canvas state.
    private static void RemapCollidingAddIds(GhPatchDocument patch, List<GhJsonComponent> adds, GhJsonDocument baseExport)
    {
        var taken = new HashSet<int>((baseExport.Components ?? Enumerable.Empty<GhJsonComponent>())
            .Where(c => c.Id is int)
            .Select(c => c.Id!.Value));

        int nextId = taken.Concat(adds.Where(a => a.Id is int).Select(a => a.Id!.Value))
            .DefaultIfEmpty(0)
            .Max() + 1;

        var seenAddIds = new HashSet<int>();
        foreach (GhJsonComponent add in adds)
        {
            if (add.Id is int id && !taken.Contains(id) && seenAddIds.Add(id))
            {
                continue;
            }

            int fresh = nextId++;
            if (add.Id is int old)
            {
                RewriteEndpointIds(patch, old, fresh);
                RewriteGroupMemberIds(patch, old, fresh);
            }

            add.Id = fresh;
            seenAddIds.Add(fresh);
        }
    }

    private static void RewriteEndpointIds(GhPatchDocument patch, int oldId, int newId)
    {
        foreach (GhJsonConnection conn in (patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>())
            .Concat(patch.Patch?.Connections?.Remove ?? Enumerable.Empty<GhJsonConnection>()))
        {
            if (conn.From is { } from && from.Id == oldId)
            {
                from.Id = newId;
            }

            if (conn.To is { } to && to.Id == oldId)
            {
                to.Id = newId;
            }
        }
    }

    private static void RewriteGroupMemberIds(GhPatchDocument patch, int oldId, int newId)
    {
        static void Rewrite(List<int>? members, int oldId, int newId)
        {
            if (members is null)
            {
                return;
            }

            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] == oldId)
                {
                    members[i] = newId;
                }
            }
        }

        foreach (GhJsonGroup group in patch.Patch?.Groups?.Add ?? Enumerable.Empty<GhJsonGroup>())
        {
            Rewrite(group.Members, oldId, newId);
        }

        foreach (GhPatchGroupModify modify in patch.Patch?.Groups?.Modify ?? Enumerable.Empty<GhPatchGroupModify>())
        {
            Rewrite(modify.Members?.Add, oldId, newId);
            Rewrite(modify.Members?.Remove, oldId, newId);
        }
    }

    // Positions pivot-less adds in a vertical stack. The preamble instructs the model to author
    // pivots relative to the existing layout it can see, so this is a net only — but when it fires
    // we keep the stack clear of the current graph: just below the existing content (adjacent to
    // it, not on top of it), falling back to the arrow tip when the canvas is empty.
    // Horizontal/vertical clearance between the existing graph and a patch's added group.
    private const float PatchAddGap = 60f;

    /// <summary>
    /// Positions a patch's added components. Their authored pivots are the intended RELATIVE
    /// arrangement and <see cref="RelayoutLlmGraph"/> keeps them (falling back to a hierarchical
    /// relayout from the group's internal wires only when that arrangement is degenerate), then the
    /// whole group is translated as one block and anchored to the right of the LLM-built graph it
    /// extends. The translation is what makes authoring in the model's own coordinate frame safe:
    /// without it, authored pivots land at those raw coordinates, nowhere near the anchored graph
    /// (the 2026-07-13 balcony landed a screen away from its tower).
    /// </summary>
    /// <param name="patch">The parsed patch (source of the add-internal connections).</param>
    /// <param name="adds">The added components, mutated in place.</param>
    /// <param name="doc">The live document.</param>
    /// <param name="baseExport">The canvas-state export the patch was authored against.</param>
    /// <param name="fallbackOrigin">The transmitter's arrow target, used when the canvas offers no anchor.</param>
    private static void AnchorPatchAdds(
        GhPatchDocument patch,
        List<GhJsonComponent> adds,
        GH_Document doc,
        GhJsonDocument baseExport,
        PointF fallbackOrigin)
    {
        var addIds = new HashSet<int>(adds.Where(a => a.Id is int).Select(a => a.Id!.Value));

        // An add that JOINS A GROUP belongs beside that group, not out at the right-hand end of the
        // whole graph — whether it is an unwired documentation Panel or a wired node extending the
        // area's maths. Anchoring these with the general block is what stretched every group box
        // across the canvas in the 2026-07-25 White House session: three math nodes joining the
        // Main Block group landed past the Wings group, so all three boxes overlapped and the new
        // upstream nodes sat far to the RIGHT of the components they feed.
        List<GhJsonComponent> homed = ExtractGroupHomedAdds(patch, adds, doc, baseExport);

        // The relayout fallback reads the group's INTERNAL wires only — wires into existing
        // components anchor the group as a whole, not individual nodes.
        List<GhJsonConnection> internalWires = (patch.Patch?.Connections?.Add ?? new List<GhJsonConnection>())
            .Where(c => c.From?.Id is int from && c.To?.Id is int to
                && addIds.Contains(from) && addIds.Contains(to))
            .ToList();
        RelayoutLlmGraph(new GhJsonDocument("1.0", null, adds, internalWires, null));

        // The anchor, best frame first: everything the model has placed so far (the graph the
        // transmitter's arrow originally anchored), then the existing components this patch wires
        // into, then the whole canvas. The group lands to the right of it, top-aligned.
        RectangleF? anchor = LiveAuthoredBounds(doc)
            ?? ConnectedExistingBounds(patch, addIds, baseExport, doc)
            ?? ExistingCanvasBounds(doc);
        PointF target = anchor is { } a ? new PointF(a.Right + PatchAddGap, a.Top) : fallbackOrigin;

        // Translate the relaid-out group so its top-left pivot lands on the target.
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        foreach (GhJsonComponent add in adds)
        {
            if (add.Pivot is { } pivot)
            {
                minX = Math.Min(minX, pivot.X);
                minY = Math.Min(minY, pivot.Y);
            }
        }

        if (minX < float.MaxValue)
        {
            int dx = (int)MathF.Round(target.X - minX);
            int dy = (int)MathF.Round(target.Y - minY);
            foreach (GhJsonComponent add in adds)
            {
                if (add.Pivot is { } pivot)
                {
                    pivot.X += dx;
                    pivot.Y += dy;
                }
            }
        }

        // Safety net for adds the relayout could not position (no integer id).
        AssignFallbackPivots(adds, target, null);

        // The group-homed adds rejoin the list once the general block has been anchored, so nothing
        // downstream has to know they were treated specially. Any that found no live group anchor
        // stacks below the general block rather than on top of it.
        if (homed.Count > 0)
        {
            RectangleF placed = PivotBounds(adds) ?? new RectangleF(target, SizeF.Empty);
            adds.AddRange(homed);
            AssignFallbackPivots(adds, target, placed);
        }
    }

    // Vertical clearance between an annotation Panel and the top of the group it documents, and
    // between two annotations stacked on the same group. Panels auto-size to their text, so this
    // is generous enough that a paragraph-length note clears the one above it.
    private const float AnnotationGap = 120f;

    // Bounding box of the pivots already assigned in a set of adds, or null when none are placed.
    // Pivots are points, not bounds — good enough to keep a fallback stack off the placed block.
    private static RectangleF? PivotBounds(IEnumerable<GhJsonComponent> adds)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (GhJsonComponent add in adds)
        {
            if (add.Pivot is not { } pivot)
            {
                continue;
            }

            minX = Math.Min(minX, pivot.X);
            minY = Math.Min(minY, pivot.Y);
            maxX = Math.Max(maxX, pivot.X);
            maxY = Math.Max(maxY, pivot.Y);
        }

        return minX < float.MaxValue
            ? new RectangleF(minX, minY, maxX - minX, maxY - minY)
            : null;
    }

    // Clearance below a group's live bounds for a row of wired adds joining it, and the step used
    // when that row has to be pushed further down to clear unrelated components.
    private const float GroupRowGap = 60f;

    // Nominal size of a placed component, used to turn pivot POINTS into an approximate occupied
    // rectangle for the clear-space search. A pivot sits at the capsule's mid-left, so the box runs
    // right from it and is centred vertically.
    private const float NominalComponentWidth = 130f;
    private const float NominalComponentHeight = 32f;

    /// <summary>
    /// One group a patch's added components are joining, and the live canvas bounds of the members
    /// it already has.
    /// </summary>
    /// <param name="LiveBounds">Union bounds of the group's members already on the canvas.</param>
    /// <param name="JoiningIds">Ids of this patch's adds that become members of the group.</param>
    private sealed record GroupHome(RectangleF LiveBounds, HashSet<int> JoiningIds);

    /// <summary>
    /// Removes from <paramref name="adds"/> every added component that a group in this patch makes
    /// a member of, and positions it against that group's live bounds instead of letting it ride the
    /// general right-of-graph anchor. Unwired adds (a documentation Panel) stack ABOVE the group so
    /// the note reads as a caption; wired adds are laid out as their own island and placed in a row
    /// BELOW it, pushed down until clear of unrelated components, so the group box grows by one row
    /// instead of stretching across the canvas. Covers both groups the patch creates
    /// (<c>groups.add</c>) and groups it extends (<c>groups.modify</c> with <c>members.add</c>).
    /// An add whose group offers no live bounds — a brand-new group of brand-new components — is
    /// returned unpositioned for the caller's fallback.
    /// </summary>
    /// <param name="patch">The parsed patch, read for its added wires and group operations.</param>
    /// <param name="adds">The added components; group-homed ones are removed from it.</param>
    /// <param name="doc">The live document, source of the members' real bounds.</param>
    /// <param name="baseExport">The canvas-state export, mapping member ids to live instanceGuids.</param>
    /// <returns>The extracted adds, pivoted where a live group anchor was found.</returns>
    private static List<GhJsonComponent> ExtractGroupHomedAdds(
        GhPatchDocument patch,
        List<GhJsonComponent> adds,
        GH_Document doc,
        GhJsonDocument baseExport)
    {
        var addIds = new HashSet<int>(adds.Where(a => a.Id is int).Select(a => a.Id!.Value));
        List<GroupHome> homes = BuildGroupHomes(patch, doc, baseExport, addIds);
        if (homes.Count == 0)
        {
            return new List<GhJsonComponent>();
        }

        var wiredIds = new HashSet<int>();
        foreach (GhJsonConnection connection in patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>())
        {
            foreach (int? endpoint in new[] { connection.From?.Id, connection.To?.Id })
            {
                if (endpoint is int id)
                {
                    wiredIds.Add(id);
                }
            }
        }

        var extracted = new List<GhJsonComponent>();
        var claimed = new HashSet<int>();

        foreach (GroupHome home in homes)
        {
            // A component listed by two groups belongs to the first that claims it — never moved twice.
            List<GhJsonComponent> joining = adds
                .Where(a => a.Id is int id && home.JoiningIds.Contains(id) && !claimed.Contains(id))
                .ToList();
            if (joining.Count == 0)
            {
                continue;
            }

            foreach (GhJsonComponent add in joining)
            {
                adds.Remove(add);
                extracted.Add(add);
                claimed.Add(add.Id!.Value);
            }

            if (home.LiveBounds.IsEmpty)
            {
                // Nothing of this group is on canvas yet: leave them for the caller's fallback.
                foreach (GhJsonComponent add in joining)
                {
                    add.Pivot = null;
                }

                continue;
            }

            List<GhJsonComponent> annotations = joining.Where(a => !wiredIds.Contains(a.Id!.Value)).ToList();
            List<GhJsonComponent> wired = joining.Where(a => wiredIds.Contains(a.Id!.Value)).ToList();

            // Captions stack upward off the group's top-left corner.
            float stackY = home.LiveBounds.Top;
            foreach (GhJsonComponent annotation in annotations)
            {
                stackY -= AnnotationGap;
                annotation.Pivot = new GhJsonPivot(
                    (int)MathF.Round(home.LiveBounds.Left),
                    (int)MathF.Round(stackY));
            }

            if (wired.Count > 0)
            {
                PlaceWiredRowBelowGroup(patch, wired, home, doc);
            }
        }

        return extracted;
    }

    /// <summary>
    /// Lays a patch's wired adds out as their own island and parks it in clear space below the group
    /// it is joining, left-aligned with it. This is a STAGING position, not the final layout: the
    /// wires do not exist yet at this point in the apply, so the column a new node really belongs in
    /// is not yet knowable. Once they do, <see cref="SeparatePlacedOverlaps"/> rebuilds every area
    /// that gained a member and the island is absorbed into its proper columns. The staged position
    /// only survives for an area with no usable wiring to rebuild from.
    /// </summary>
    /// <param name="patch">The parsed patch, source of the island's internal wires.</param>
    /// <param name="wired">The wired adds joining this group, pivoted in place.</param>
    /// <param name="home">The group being extended.</param>
    /// <param name="doc">The live document, scanned for components the row must clear.</param>
    private static void PlaceWiredRowBelowGroup(
        GhPatchDocument patch,
        List<GhJsonComponent> wired,
        GroupHome home,
        GH_Document doc)
    {
        var islandIds = new HashSet<int>(wired.Select(a => a.Id!.Value));
        List<GhJsonConnection> islandWires = (patch.Patch?.Connections?.Add ?? new List<GhJsonConnection>())
            .Where(c => c.From?.Id is int from && c.To?.Id is int to
                && islandIds.Contains(from) && islandIds.Contains(to))
            .ToList();
        RelayoutLlmGraph(new GhJsonDocument("1.0", null, wired, islandWires, null));

        RectangleF island = PivotBounds(wired) ?? RectangleF.Empty;
        float targetX = home.LiveBounds.Left;
        float targetY = home.LiveBounds.Bottom + GroupRowGap;

        // Walk down until the row's footprint clears every live component. The footprint spans only
        // the group's own column, so this steps over whatever sits directly beneath it. Capped
        // deliberately low: pushing a row hundreds of units down to find a gap would strand it from
        // the group it belongs to, so past the cap the first candidate is accepted and a little
        // overlap is left for the user to nudge.
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var footprint = new RectangleF(
                targetX,
                targetY - (NominalComponentHeight / 2f),
                island.Width + NominalComponentWidth,
                island.Height + NominalComponentHeight);

            if (!IntersectsAnyLiveComponent(doc, footprint))
            {
                break;
            }

            targetY += GroupRowGap;
        }

        int dx = (int)MathF.Round(targetX - island.X);
        int dy = (int)MathF.Round(targetY - island.Y);
        foreach (GhJsonComponent add in wired)
        {
            if (add.Pivot is { } pivot)
            {
                pivot.X += dx;
                pivot.Y += dy;
            }
        }
    }

    /// <summary>
    /// Collects the groups this patch's adds belong beside, paired with the live bounds of the
    /// members each group already has. Three sources, most explicit first: <c>groups.add</c> (a new
    /// group whose member list may name existing components), <c>groups.modify</c> with
    /// <c>members.add</c> (an existing group gaining new components), and — for an add the model
    /// grouped nowhere — the group its own WIRES point at.
    /// </summary>
    /// <param name="patch">The parsed patch.</param>
    /// <param name="doc">The live document.</param>
    /// <param name="baseExport">The canvas-state export the patch was authored against.</param>
    /// <param name="addIds">Ids of every component this patch adds.</param>
    /// <returns>One entry per group that at least one add belongs beside.</returns>
    private static List<GroupHome> BuildGroupHomes(
        GhPatchDocument patch,
        GH_Document doc,
        GhJsonDocument baseExport,
        IReadOnlySet<int> addIds)
    {
        var homes = new List<GroupHome>();
        GhPatchGroupsOp? groups = patch.Patch?.Groups;

        var guidById = new Dictionary<int, Guid>();
        foreach (GhJsonComponent component in baseExport.Components ?? Enumerable.Empty<GhJsonComponent>())
        {
            if (component.Id is int id && component.InstanceGuid is Guid guid)
            {
                guidById[id] = guid;
            }
        }

        RectangleF LiveBoundsOf(IEnumerable<int> memberIds)
        {
            RectangleF union = RectangleF.Empty;
            foreach (int member in memberIds)
            {
                if (guidById.TryGetValue(member, out Guid guid)
                    && doc.FindObject(guid, false) is { Attributes: { } attr } && !attr.Bounds.IsEmpty)
                {
                    union = union.IsEmpty ? attr.Bounds : RectangleF.Union(union, attr.Bounds);
                }
            }

            return union;
        }

        // New groups: the live half of the member list anchors the new half.
        foreach (GhJsonGroup add in groups?.Add ?? Enumerable.Empty<GhJsonGroup>())
        {
            IReadOnlyList<int> members = add.Members ?? (IReadOnlyList<int>)Array.Empty<int>();
            if (members.Count > 0)
            {
                homes.Add(new GroupHome(LiveBoundsOf(members), new HashSet<int>(members)));
            }
        }

        // Existing groups gaining members: the group's CURRENT membership is the anchor.
        foreach (GhPatchGroupModify modify in groups?.Modify ?? Enumerable.Empty<GhPatchGroupModify>())
        {
            List<int> joining = (modify.Members?.Add ?? Enumerable.Empty<int>()).ToList();
            if (joining.Count == 0)
            {
                continue;
            }

            GhJsonGroup? baseGroup = (baseExport.Groups ?? Enumerable.Empty<GhJsonGroup>())
                .FirstOrDefault(g => (modify.Match.InstanceGuid is Guid ig && g.InstanceGuid == ig)
                    || (modify.Match.InstanceGuid is null && modify.Match.Id is int i && g.Id == i));
            if (baseGroup is null)
            {
                continue;
            }

            homes.Add(new GroupHome(
                LiveBoundsOf(baseGroup.Members ?? Enumerable.Empty<int>()),
                new HashSet<int>(joining)));
        }

        AddWireInferredHomes(patch, baseExport, addIds, homes, LiveBoundsOf);
        return homes;
    }

    /// <summary>
    /// Homes the adds no group operation mentions, by following their wires: an add whose new wires
    /// reach existing components sitting in exactly one group belongs beside THAT group, not out at
    /// the right-hand end of the whole graph. This is the case that stranded a lone Deconstruct
    /// Domain across the canvas in the 2026-07-25 23:19 session — the model wired it into the
    /// Portico's Construct Domain and Division but never put it in a group, so nothing tied it to
    /// the area it serves. Adds whose wires straddle several groups, reach only ungrouped
    /// components, or reach only other adds are left alone for the general anchor.
    /// </summary>
    /// <param name="patch">The parsed patch.</param>
    /// <param name="baseExport">The canvas-state export the patch was authored against.</param>
    /// <param name="addIds">Ids of every component this patch adds.</param>
    /// <param name="homes">The homes collected so far; inferred ones are appended.</param>
    /// <param name="liveBoundsOf">Resolves a member-id set to its live union bounds.</param>
    private static void AddWireInferredHomes(
        GhPatchDocument patch,
        GhJsonDocument baseExport,
        IReadOnlySet<int> addIds,
        List<GroupHome> homes,
        Func<IEnumerable<int>, RectangleF> liveBoundsOf)
    {
        var alreadyHomed = new HashSet<int>(homes.SelectMany(h => h.JoiningIds));
        List<int> orphans = addIds.Where(id => !alreadyHomed.Contains(id)).ToList();
        if (orphans.Count == 0)
        {
            return;
        }

        // Which base group owns each existing component. A component in two groups is ambiguous and
        // is simply not used as evidence.
        var groupByMember = new Dictionary<int, GhJsonGroup>();
        var ambiguous = new HashSet<int>();
        foreach (GhJsonGroup group in baseExport.Groups ?? Enumerable.Empty<GhJsonGroup>())
        {
            foreach (int member in group.Members ?? Enumerable.Empty<int>())
            {
                if (!groupByMember.TryAdd(member, group))
                {
                    ambiguous.Add(member);
                }
            }
        }

        foreach (int member in ambiguous)
        {
            groupByMember.Remove(member);
        }

        if (groupByMember.Count == 0)
        {
            return;
        }

        // For each orphan, the distinct groups its wires reach.
        var reachedByOrphan = new Dictionary<int, HashSet<GhJsonGroup>>();
        foreach (GhJsonConnection connection in patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>())
        {
            if (connection.From?.Id is not int from || connection.To?.Id is not int to)
            {
                continue;
            }

            foreach ((int mine, int theirs) in new[] { (from, to), (to, from) })
            {
                if (!orphans.Contains(mine)
                    || addIds.Contains(theirs)
                    || !groupByMember.TryGetValue(theirs, out GhJsonGroup? group))
                {
                    continue;
                }

                if (!reachedByOrphan.TryGetValue(mine, out HashSet<GhJsonGroup>? reached))
                {
                    reached = reachedByOrphan[mine] = new HashSet<GhJsonGroup>();
                }

                reached.Add(group);
            }
        }

        // One group reached, unambiguously → that is the home. Orphans sharing a home share an island.
        foreach (IGrouping<GhJsonGroup, int> byGroup in reachedByOrphan
            .Where(pair => pair.Value.Count == 1)
            .GroupBy(pair => pair.Value.Single(), pair => pair.Key))
        {
            homes.Add(new GroupHome(
                liveBoundsOf(byGroup.Key.Members ?? Enumerable.Empty<int>()),
                new HashSet<int>(byGroup)));
        }
    }

    // True when a rectangle overlaps any live document object's bounds — the clear-space test for a
    // row of adds joining an existing group.
    private static bool IntersectsAnyLiveComponent(GH_Document doc, RectangleF area)
    {
        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            // Group boxes enclose their members and would always "hit"; the members themselves are
            // what a new row has to avoid.
            if (obj is GHGroupObject || obj.Attributes is not IGH_Attributes attr || attr.Bounds.IsEmpty)
            {
                continue;
            }

            if (attr.Bounds.IntersectsWith(area))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Union bounds of every live component the model has placed so far (via the
    /// authored-placement ledger), or null when nothing of its authorship is alive.
    /// </summary>
    /// <param name="doc">The live document.</param>
    /// <returns>The union bounds, or null.</returns>
    private static RectangleF? LiveAuthoredBounds(GH_Document doc)
    {
        if (!AuthoredPlacementLedgers.TryGetValue(doc, out Dictionary<Guid, int>? ledger))
        {
            return null;
        }

        RectangleF union = RectangleF.Empty;
        foreach (Guid guid in ledger.Keys)
        {
            if (doc.FindObject(guid, false) is { Attributes: { } attr } && !attr.Bounds.IsEmpty)
            {
                union = union.IsEmpty ? attr.Bounds : RectangleF.Union(union, attr.Bounds);
            }
        }

        return union.IsEmpty ? null : union;
    }

    /// <summary>
    /// Union bounds of the EXISTING components this patch's added wires connect to — the graph
    /// being extended — resolved through the base export's id → instanceGuid mapping. The
    /// fallback anchor when the authored-placement ledger is empty (fresh session, canvas built
    /// in an earlier one).
    /// </summary>
    /// <param name="patch">The parsed patch.</param>
    /// <param name="addIds">The ids of the added components.</param>
    /// <param name="baseExport">The canvas-state export the patch was authored against.</param>
    /// <param name="doc">The live document.</param>
    /// <returns>The union bounds, or null.</returns>
    private static RectangleF? ConnectedExistingBounds(
        GhPatchDocument patch,
        IReadOnlySet<int> addIds,
        GhJsonDocument baseExport,
        GH_Document doc)
    {
        var guidById = new Dictionary<int, Guid>();
        foreach (GhJsonComponent component in baseExport.Components ?? Enumerable.Empty<GhJsonComponent>())
        {
            if (component.Id is int id && component.InstanceGuid is Guid guid)
            {
                guidById[id] = guid;
            }
        }

        RectangleF union = RectangleF.Empty;
        foreach (GhJsonConnection connection in patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>())
        {
            foreach (int? endpoint in new[] { connection.From?.Id, connection.To?.Id })
            {
                if (endpoint is int id && !addIds.Contains(id)
                    && guidById.TryGetValue(id, out Guid guid)
                    && doc.FindObject(guid, false) is { Attributes: { } attr } && !attr.Bounds.IsEmpty)
                {
                    union = union.IsEmpty ? attr.Bounds : RectangleF.Union(union, attr.Bounds);
                }
            }
        }

        return union.IsEmpty ? null : union;
    }

    private static void AssignFallbackPivots(List<GhJsonComponent> adds, PointF fallbackOrigin, RectangleF? occupied)
    {
        float x = occupied is { } o ? o.X : fallbackOrigin.X;
        float y = occupied is { } b ? b.Bottom + 60f : fallbackOrigin.Y;

        int stacked = 0;
        foreach (GhJsonComponent add in adds)
        {
            if (add.Pivot is null)
            {
                add.Pivot = new GhJsonPivot(
                    (int)MathF.Round(x),
                    (int)MathF.Round(y) + (stacked * 80));
                stacked++;
            }
        }
    }

    // Union of the current canvas objects' real bounds, or null when the canvas is empty. Used to
    // drop pivot-less patch adds into clear space adjacent to (not on top of) the existing graph.
    private static RectangleF? ExistingCanvasBounds(GH_Document doc)
    {
        RectangleF union = RectangleF.Empty;
        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (obj.Attributes is not IGH_Attributes attr || attr.Bounds.IsEmpty)
            {
                continue;
            }

            union = union.IsEmpty ? attr.Bounds : RectangleF.Union(union, attr.Bounds);
        }

        return union.IsEmpty ? null : union;
    }

    // ---- Modify ------------------------------------------------------------------------------

    private static void ApplyModifies(
        GhPatchDocument patch,
        GH_Document doc,
        GhJsonDocument baseExport,
        GhJsonDocument mergedDoc,
        BaseIndex index,
        List<Guid> modifiedGuids,
        List<string> conflicts,
        List<string> warnings,
        List<string> appliedOps)
    {
        foreach (GhPatchComponentModify modify in patch.Patch?.Components?.Modify ?? Enumerable.Empty<GhPatchComponentModify>())
        {
            GhJsonComponent? baseComp = ResolveComponentMatch(modify.Match, baseExport, index);
            if (baseComp?.InstanceGuid is not Guid guid)
            {
                // The document-level apply normally reports match_not_found/ambiguous itself, but a
                // modify must never vanish without a trace — add our own verdict when it did not.
                if (!conflicts.Any(c => c.Contains("components.modify", StringComparison.Ordinal)))
                {
                    conflicts.Add($"match_not_found: components.modify target ({DescribeMatch(modify.Match)}) did not resolve against the canvas state; the modify was not applied.");
                }

                continue;
            }

            IGH_DocumentObject? live = doc.FindObject(guid, false);
            if (live is null)
            {
                conflicts.Add($"match_not_found: component {guid} vanished from the canvas between export and apply.");
                continue;
            }

            GhJsonComponent? merged = (mergedDoc.Components ?? Enumerable.Empty<GhJsonComponent>())
                .FirstOrDefault(c => c.Id == baseComp.Id);
            if (merged is null)
            {
                // Legitimate when the same patch also removes the component (the remove wins), but
                // say so instead of dropping the modify silently.
                warnings.Add($"modify skipped for '{live.NickName}' ({guid}): the component is absent from the post-merge document — the same patch likely also removes it.");
                continue;
            }

            // A componentState extension that cannot apply to this component's type must fail
            // LOUDLY here. Without this check the op falls to the replace path, the rebuilt
            // component silently ignores the foreign extension, and the outcome reports the op as
            // applied — the model is then told a do-nothing change landed and burns rounds
            // reverse-engineering why the value never moved (observed live: gh.numberslider on a
            // Construct Domain, confirmed "rebuilt with wires preserved" twice).
            if (!ValidateModifyExtensions(modify, live, conflicts))
            {
                continue;
            }

            if (TryFastPathModify(modify, live))
            {
                modifiedGuids.Add(guid);
                appliedOps.Add(DescribeModify(modify, live, inPlace: true));
                continue;
            }

            // A Rhino-referenced input must never take the replace path: the GhJSON round-trip
            // bakes its current value, so rebuilding it would sever the live link to the Rhino
            // object. Simple changes (nickName, pivot, locked, hidden) went through the fast path
            // above; anything richer is refused.
            if (CanvasRhinoReferences.IsRhinoReferenced(live))
            {
                conflicts.Add(
                    $"cannot rebuild Rhino-referenced input '{live.NickName}' ({guid}): it references live "
                    + "geometry in the Rhino model, and rebuilding it would sever that link. Only nickName and "
                    + "pivot changes are allowed on it — wire from it as a data source, or remove it.");
                continue;
            }

            if (TryReplaceComponent(doc, live, merged, conflicts, warnings))
            {
                modifiedGuids.Add(guid);
                appliedOps.Add(DescribeModify(modify, live, inPlace: false));
            }
        }
    }

    // Rejects componentState extension sets that cannot apply to the matched component's type:
    // gh.numberslider only means something on a Number Slider, gh.panel only on a Panel. Anything
    // else (physalia.* markers, library extensions) passes through unchecked as before. Returns
    // false — with one model-facing conflict per offending key, naming the fix — when the modify
    // must not be applied.
    private static bool ValidateModifyExtensions(GhPatchComponentModify modify, IGH_DocumentObject live, List<string> conflicts)
    {
        if (modify.ComponentState?.Extensions?.Set is not { } sets)
        {
            return true;
        }

        bool valid = true;
        foreach (string key in sets.Keys)
        {
            string? fix = key switch
            {
                "gh.numberslider" when live is not GHNumberSlider =>
                    "gh.numberslider applies only to Number Slider components. To change this component's "
                    + "values, set its inputSettings.byParameterName internalizedData instead; to change a "
                    + "slider, match the slider component itself.",
                "gh.panel" when live is not GHPanel =>
                    "gh.panel applies only to Panel components. To change this component's values, set its "
                    + "inputSettings.byParameterName internalizedData instead.",
                _ => null,
            };

            if (fix is not null)
            {
                conflicts.Add(
                    $"invalid_op: componentState extension '{key}' does not apply to '{live.NickName}' "
                    + $"({live.Name}, {live.InstanceGuid}) — the modify was NOT applied. {fix}");
                valid = false;
            }
        }

        return valid;
    }

    // Names a modify's target for the match_not_found conflict, using whatever identity the model authored.
    private static string DescribeMatch(GhPatchComponentMatch match) =>
        match.InstanceGuid is Guid guid ? $"instanceGuid {guid}"
        : match.Id is int id ? $"id {id}"
        : match.Name is not null ? $"name '{match.Name}'"
        : $"componentGuid {match.ComponentGuid?.ToString() ?? "?"}";

    // Describes one applied modify for the outcome's applied-ops ledger: the target's identity plus
    // every field the op touched. Positive per-op confirmation matters because the canvas checksum
    // deliberately excludes internalized data — the model must never infer "my modify failed" from
    // an unchanged checksum.
    private static string DescribeModify(GhPatchComponentModify modify, IGH_DocumentObject live, bool inPlace)
    {
        var touched = new List<string>();
        touched.AddRange(modify.Set?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>());
        touched.AddRange(modify.ComponentState?.Set?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>());
        touched.AddRange(modify.ComponentState?.Extensions?.Set?.Keys ?? Enumerable.Empty<string>());
        touched.AddRange(modify.InputSettings?.ByParameterName?.Keys.Select(k => $"inputSettings['{k}']") ?? Enumerable.Empty<string>());
        touched.AddRange(modify.OutputSettings?.ByParameterName?.Keys.Select(k => $"outputSettings['{k}']") ?? Enumerable.Empty<string>());
        touched.AddRange(modify.Remove?.Select(r => $"removed {r}") ?? Enumerable.Empty<string>());

        string what = touched.Count > 0 ? string.Join(", ", touched) : "state";
        string how = inPlace ? "in place" : "rebuilt with wires preserved";
        return $"modified '{live.NickName}' ({live.InstanceGuid}): {what} ({how})";
    }

    // Applies a modify by mutating the live object directly when EVERY requested change is one of
    // the simple cases: nickName, pivot, locked/hidden, a slider value, panel text, or an in-place
    // parameter data-tree modifier (graft/flatten, simplify, reverse, etc.). Anything else (field
    // removes, internalized data, a renamed/unknown parameter, other extensions) returns false so
    // the caller falls back to replacing the object with its full post-merge state.
    private static bool TryFastPathModify(GhPatchComponentModify modify, IGH_DocumentObject live)
    {
        if (modify.Remove is { Count: > 0 })
        {
            return false;
        }

        // Parameter-settings changes qualify for the fast path only when they touch nothing but
        // data-tree modifiers / nickName on parameters that already exist; a structural settings
        // change (internalized data, a renamed or unknown parameter) still needs the replace path.
        if (!ParamSettingsOpFastPathEligible(live, modify.InputSettings, inputSide: true)
            || !ParamSettingsOpFastPathEligible(live, modify.OutputSettings, inputSide: false))
        {
            return false;
        }

        var setKeys = modify.Set?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
        if (setKeys.Any(k => k is not ("nickName" or "pivot")))
        {
            return false;
        }

        GhPatchComponentStateOp? state = modify.ComponentState;
        var stateKeys = state?.Set?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
        if (state?.Remove is { Count: > 0 }
            || state?.Extensions?.Remove is { Count: > 0 }
            || stateKeys.Any(k => k is not ("locked" or "hidden")))
        {
            return false;
        }

        var extensionSets = state?.Extensions?.Set ?? new Dictionary<string, JObject>();
        foreach (KeyValuePair<string, JObject> ext in extensionSets)
        {
            bool handled = ext.Key switch
            {
                "gh.numberslider" => live is GHNumberSlider
                    && ext.Value.Properties().All(p => p.Name == "value")
                    && TryParseSliderValue(ext.Value["value"]?.ToString(), out _, out _, out _),
                "gh.panel" => live is GHPanel && ext.Value.Properties().All(p => p.Name == "text"),
                _ => false,
            };

            if (!handled)
            {
                return false;
            }
        }

        if (modify.Set?["pivot"] is { } pivotToken && !TryReadPivot(pivotToken, out _))
        {
            return false;
        }

        // Everything requested is simple — apply it.
        live.RecordUndoEvent("Physalia AI patch");

        if (modify.Set?["nickName"]?.ToString() is { } nickName && !string.IsNullOrWhiteSpace(nickName))
        {
            live.NickName = nickName;
        }

        if (modify.Set?["pivot"] is { } pivot && TryReadPivot(pivot, out PointF point) && live.Attributes is not null)
        {
            live.Attributes.Pivot = point;
        }

        if (state?.Set?["locked"]?.Type == JTokenType.Boolean && live is IGH_ActiveObject active)
        {
            active.Locked = state.Set["locked"]!.Value<bool>();
        }

        if (state?.Set?["hidden"]?.Type == JTokenType.Boolean && live is IGH_PreviewObject preview)
        {
            preview.Hidden = state.Set["hidden"]!.Value<bool>();
        }

        foreach (KeyValuePair<string, JObject> ext in extensionSets)
        {
            if (ext.Key == "gh.numberslider" && live is GHNumberSlider slider
                && TryParseSliderValue(ext.Value["value"]?.ToString(), out decimal value, out decimal? min, out decimal? max))
            {
                if (min is decimal lo)
                {
                    slider.Slider.Minimum = lo;
                }

                if (max is decimal hi)
                {
                    slider.Slider.Maximum = hi;
                }

                slider.SetSliderValue(value);
            }
            else if (ext.Key == "gh.panel" && live is GHPanel panel)
            {
                panel.UserText = ext.Value["text"]?.ToString() ?? string.Empty;
            }
        }

        ApplyParamSettingsOp(live, modify.InputSettings, inputSide: true);
        ApplyParamSettingsOp(live, modify.OutputSettings, inputSide: false);

        (live as IGH_ActiveObject)?.ExpireSolution(false);
        live.Attributes?.ExpireLayout();
        return true;
    }

    // The data-tree modifier / nickName fields a ghpatch may change in place on an existing
    // parameter (matching the LLM schema's parameterSettings, plus the invert/unitize/principal
    // flags the library round-trips). Any other key forces the replace path.
    private static bool IsFastPathParamKey(string key) => key is
        "dataMapping" or "isSimplified" or "isReversed" or "isReparameterized"
        or "isInverted" or "isUnitized" or "isPrincipal" or "expression" or "nickName";

    // True when a parameter-settings op only sets/removes fast-path-safe fields on parameters that
    // exist on the live object. A null op is trivially eligible.
    private static bool ParamSettingsOpFastPathEligible(
        IGH_DocumentObject live, GhPatchParameterSettingsOp? op, bool inputSide)
    {
        if (op?.ByParameterName is not { } entries)
        {
            return true;
        }

        foreach (KeyValuePair<string, GhPatchParameterSettingsEntryOp> pair in entries)
        {
            if (ResolveLiveParam(live, pair.Key, inputSide) is null)
            {
                return false;
            }

            IEnumerable<string> setKeys = pair.Value.Set?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();
            IEnumerable<string> removeKeys = pair.Value.Remove ?? Enumerable.Empty<string>();
            if (setKeys.Concat(removeKeys).Any(k => !IsFastPathParamKey(k)))
            {
                return false;
            }
        }

        return true;
    }

    // Applies a fast-path-eligible parameter-settings op straight onto the live parameters: SET keys
    // assign a modifier, REMOVE keys clear it (dataMapping -> none, a flag -> false, expression ->
    // empty). Eligibility is assumed to have been checked already.
    private static void ApplyParamSettingsOp(
        IGH_DocumentObject live, GhPatchParameterSettingsOp? op, bool inputSide)
    {
        if (op?.ByParameterName is not { } entries)
        {
            return;
        }

        foreach (KeyValuePair<string, GhPatchParameterSettingsEntryOp> pair in entries)
        {
            IGH_Param? param = ResolveLiveParam(live, pair.Key, inputSide);
            if (param is null)
            {
                continue;
            }

            GhJsonParameterSettings settings = pair.Value.Set?.ToObject<GhJsonParameterSettings>() ?? new GhJsonParameterSettings();
            List<string> removals = pair.Value.Remove ?? new List<string>();
            ApplyParamSettingRemovals(settings, removals);

            if (settings.NickName is not null)
            {
                param.NickName = string.IsNullOrWhiteSpace(settings.NickName) ? param.Name : settings.NickName;
            }
            else if (removals.Contains("nickName"))
            {
                param.NickName = param.Name;
            }

            ApplyParamModifiers(param, settings);
        }
    }

    // Turns each REMOVE key into the modifier's cleared sentinel on the settings object, so the
    // shared ApplyParamModifiers pass clears it (a null field is "leave unchanged").
    private static void ApplyParamSettingRemovals(GhJsonParameterSettings settings, IEnumerable<string> removeKeys)
    {
        foreach (string key in removeKeys)
        {
            switch (key)
            {
                case "dataMapping": settings.DataMapping = "none"; break;
                case "isSimplified": settings.IsSimplified = false; break;
                case "isReversed": settings.IsReversed = false; break;
                case "isReparameterized": settings.IsReparameterized = false; break;
                case "isInverted": settings.IsInverted = false; break;
                case "isUnitized": settings.IsUnitized = false; break;
                case "isPrincipal": settings.IsPrincipal = false; break;
                case "expression": settings.Expression = string.Empty; break;
            }
        }
    }

    // Finds the live parameter a settings entry names, on the requested side of a component or on a
    // floating parameter. Matched by full Name (Ordinal), the same key the schema uses.
    private static IGH_Param? ResolveLiveParam(IGH_DocumentObject live, string name, bool inputSide)
    {
        if (live is IGH_Param floating)
        {
            return string.Equals(floating.Name, name, StringComparison.Ordinal) ? floating : null;
        }

        if (live is IGH_Component component)
        {
            IList<IGH_Param> side = inputSide ? component.Params.Input : component.Params.Output;
            return side.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
        }

        return null;
    }

    // Parses the gh.numberslider compact value format "current<min~max>" (e.g. "10<5~50>"), or a
    // bare number for a value-only change.
    private static bool TryParseSliderValue(string? text, out decimal value, out decimal? min, out decimal? max)
    {
        value = 0m;
        min = null;
        max = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        int lt = text.IndexOf('<');
        if (lt < 0)
        {
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        int tilde = text.IndexOf('~', lt);
        if (tilde < 0 || !text.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        if (!decimal.TryParse(text[..lt], NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            || !decimal.TryParse(text[(lt + 1)..tilde], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal lo)
            || !decimal.TryParse(text[(tilde + 1)..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal hi))
        {
            return false;
        }

        min = lo;
        max = hi;
        return true;
    }

    // Reads a patch pivot value: either the compact string form "120,200" or an object {x, y}.
    private static bool TryReadPivot(JToken token, out PointF point)
    {
        point = PointF.Empty;

        if (token.Type == JTokenType.String)
        {
            string[] parts = token.ToString().Split(',');
            if (parts.Length == 2
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                point = new PointF(x, y);
                return true;
            }

            return false;
        }

        if (token is JObject obj
            && obj["x"] is { } xt && obj["y"] is { } yt
            && float.TryParse(xt.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float ox)
            && float.TryParse(yt.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float oy))
        {
            point = new PointF(ox, oy);
            return true;
        }

        return false;
    }

    // One wire touching a component being replaced: which side it was on, the parameter's index and
    // name on the replaced component, and the live parameter at the OTHER end (which survives the
    // replacement untouched).
    private readonly record struct CapturedWire(bool InputSide, int ParamIndex, string ParamName, IGH_Param Other);

    private static List<CapturedWire> CaptureLiveWires(IGH_DocumentObject live)
    {
        var wires = new List<CapturedWire>();

        if (live is IGH_Component component)
        {
            for (int i = 0; i < component.Params.Input.Count; i++)
            {
                IGH_Param input = component.Params.Input[i];
                wires.AddRange(input.Sources.Select(s => new CapturedWire(true, i, input.Name, s)));
            }

            for (int i = 0; i < component.Params.Output.Count; i++)
            {
                IGH_Param output = component.Params.Output[i];
                wires.AddRange(output.Recipients.Select(r => new CapturedWire(false, i, output.Name, r)));
            }
        }
        else if (live is IGH_Param param)
        {
            wires.AddRange(param.Sources.Select(s => new CapturedWire(true, 0, param.Name, s)));
            wires.AddRange(param.Recipients.Select(r => new CapturedWire(false, 0, param.Name, r)));
        }

        return wires;
    }

    // Replaces a live object with its full post-merge state: capture its wires, remove it, place
    // the merged component with its ORIGINAL InstanceGuid preserved (so the model's handle on it
    // stays valid next turn), and re-establish the captured wires. On placement failure the
    // original object is restored, so the canvas never loses the component.
    private static bool TryReplaceComponent(GH_Document doc, IGH_DocumentObject live, GhJsonComponent merged, List<string> conflicts, List<string> warnings)
    {
        List<CapturedWire> wires = CaptureLiveWires(live);
        doc.RemoveObject(live, false);

        var oneDoc = new GhJsonDocument("1.0", null, new List<GhJsonComponent> { merged }, null, null);
        PutResult result = PhyDocuments.OnHostCanvas(() => GhJsonGrasshopper.Put(oneDoc, new PutOptions
        {
            Offset = PointF.Empty,
            AutoOffset = false,
            CreateConnections = false,
            CreateGroups = false,
            RegenerateInstanceGuids = false,
            SkipInvalidComponents = false,
            SelectPlacedObjects = false,
        }));

        IGH_DocumentObject? placed = result.Success ? result.PlacedObjects.FirstOrDefault() : null;
        if (placed is null)
        {
            // Put refused the merged state — put the original object back and rewire it.
            doc.AddObject(live, false);
            RewireCaptured(live, wires);
            conflicts.Add(
                $"modify failed for component '{merged.Name}' ({merged.InstanceGuid}): "
                + (result.ErrorMessage ?? "the merged state could not be placed") + ". The component is unchanged.");
            return false;
        }

        RewireCaptured(placed, wires);

        // The replacement Put re-applies the merged internalizedData with the library's own
        // casting, which nulls values on generic params — run the same repair the add paths use.
        RepairInternalizedData(new[] { merged }, _ => placed, warnings);

        placed.Attributes?.ExpireLayout();
        (placed as IGH_ActiveObject)?.ExpireSolution(false);
        return true;
    }

    // Re-establishes captured wires against a (re)placed object. The parameter is matched by name
    // first, index as fallback; a wire whose parameter no longer exists on the new state is dropped
    // silently — the modify may have legitimately removed it.
    private static void RewireCaptured(IGH_DocumentObject target, List<CapturedWire> wires)
    {
        foreach (CapturedWire wire in wires)
        {
            IGH_Param? param = FindReplacedParam(target, wire);
            if (param is null)
            {
                continue;
            }

            if (wire.InputSide)
            {
                if (!param.Sources.Contains(wire.Other))
                {
                    param.AddSource(wire.Other);
                }
            }
            else if (!wire.Other.Sources.Contains(param))
            {
                wire.Other.AddSource(param);
            }
        }
    }

    private static IGH_Param? FindReplacedParam(IGH_DocumentObject target, CapturedWire wire)
    {
        if (target is not IGH_Component component)
        {
            return target as IGH_Param;
        }

        IList<IGH_Param> list = wire.InputSide ? component.Params.Input : component.Params.Output;
        return list.FirstOrDefault(p => p.Name == wire.ParamName)
            ?? (wire.ParamIndex >= 0 && wire.ParamIndex < list.Count ? list[wire.ParamIndex] : null);
    }

    // ---- Remove ------------------------------------------------------------------------------

    private static void ApplyRemoves(
        GhPatchDocument patch,
        GhJsonDocument baseExport,
        BaseIndex index,
        List<Guid> removedGuids,
        List<string> conflicts)
    {
        var guids = new List<Guid>();
        foreach (GhPatchComponentMatch match in patch.Patch?.Components?.Remove ?? Enumerable.Empty<GhPatchComponentMatch>())
        {
            if (ResolveComponentMatch(match, baseExport, index)?.InstanceGuid is Guid guid)
            {
                guids.Add(guid);
            }

            // Unresolved matches were reported by the document-level apply.
        }

        if (guids.Count == 0)
        {
            return;
        }

        DeleteResult result = PhyDocuments.OnHostCanvas(
            () => GhJsonGrasshopper.Delete(guids, new DeleteOptions { Redraw = false }));
        removedGuids.AddRange(result.Deleted);
        foreach ((Guid guid, string error) in result.Failed)
        {
            conflicts.Add($"remove failed for component {guid}: {error}");
        }
    }

    // ---- Add ---------------------------------------------------------------------------------

    // Places the added components in one Put (components only — no connections), then wires every
    // connection that touches an added component through WireConnection so paramIndex stays
    // authoritative. The 1.1.1 library's Put matches connection endpoints by paramName, which
    // silently mis-wires the short names the model authors; it must never create wires here.
    // A wire targeting a variable param the model expected to auto-exist surfaces as an honest
    // conflict instead of landing on the wrong input. Returns the map of add id -> placed live
    // object for the group and connection passes.
    private static Dictionary<int, IGH_DocumentObject> ApplyAdds(
        GhPatchDocument patch,
        List<GhJsonComponent> adds,
        HashSet<int> addIds,
        GH_Document doc,
        BaseIndex index,
        List<Guid> addedGuids,
        List<Guid> modifiedGuids,
        List<string> conflicts,
        List<string> warnings)
    {
        var addedById = new Dictionary<int, IGH_DocumentObject>();
        if (adds.Count == 0)
        {
            return addedById;
        }

        var addDoc = new GhJsonDocument("1.0", null, adds, null, null);
        PutResult result = PhyDocuments.OnHostCanvas(() => GhJsonGrasshopper.Put(addDoc, BuildPutOptions(PointF.Empty)));

        if (!result.Success)
        {
            conflicts.Add("add failed: " + (result.ErrorMessage ?? "the added components could not be placed"));
            return addedById;
        }

        ComponentHelpers.ApplyNickNameDisplay(result.PlacedObjects, ExplicitlyNickNamedGuids(addDoc, result));
        DeselectPlaced(result);

        addedGuids.AddRange(result.PlacedObjects.Select(o => o.InstanceGuid));
        warnings.AddRange(result.Warnings);

        foreach (KeyValuePair<int, Guid> pair in result.IdToGuidMapping)
        {
            IGH_DocumentObject? obj = result.PlacedObjects.FirstOrDefault(o => o.InstanceGuid == pair.Value);
            if (obj is not null)
            {
                addedById[pair.Key] = obj;
            }
        }

        // Claim the (possibly remapped) add ids for the placed objects, so the next canvas export
        // keeps the numbering the model authored and its remembered ids stay valid. Claims come
        // from DeriveIdClaims (the live objects' CURRENT guids when every add placed), and are
        // verified: an add that lost its authored id flags the numbering loss so the next canvas
        // state tells the model instead of leaving it to patch by remembered ids.
        IReadOnlyList<KeyValuePair<int, Guid>> idClaims = DeriveIdClaims(addDoc, result);
        RegisterStableIds(doc, idClaims);

        int verifiedClaims = idClaims.Count(pair =>
            doc.FindObject(pair.Value, false) is not null
            && TryGetStableId(doc, pair.Value, out int stable)
            && stable == pair.Key);
        if (verifiedClaims < idClaims.Count)
        {
            MarkPlacementNumberingLoss(doc);
            warnings.Add(
                "Model-authored ids for this patch's added components were not fully preserved "
                + $"(id claims verified: {verifiedClaims}/{idClaims.Count}). "
                + "The model is warned via the next canvas state, which carries the authoritative renumbering.");
            Rhino.RhinoApp.WriteLine(
                $"[Physalia] Patch add did not preserve the model's component ids (claims verified: "
                + $"{verifiedClaims}/{idClaims.Count}; placed: {result.PlacedObjects.Count}; failed: {result.FailedComponents.Count}).");
        }

        // The library nulls internalized values it cannot cast (generic params like Division's B);
        // re-apply the authored data wherever the placed persistent data does not match it.
        RepairInternalizedData(adds, addId => addedById.TryGetValue(addId, out IGH_DocumentObject? obj) ? obj : null, warnings);

        // Every wire touching an added component — intra-add and add<->existing alike.
        foreach (GhJsonConnection conn in (patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>())
            .Where(c => c.From is { } f && c.To is { } t && (addIds.Contains(f.Id) || addIds.Contains(t.Id))))
        {
            WireConnection(conn, doc, addIds, addedById, index, modifiedGuids, conflicts, removing: false);
        }

        return addedById;
    }

    // ---- Connections -------------------------------------------------------------------------

    private static void ApplyConnectionRemoves(
        GhPatchDocument patch,
        GH_Document doc,
        HashSet<int> addIds,
        BaseIndex index,
        IReadOnlyCollection<Guid> removedGuids,
        List<Guid> modifiedGuids,
        List<string> conflicts,
        List<string> warnings)
    {
        // Component removal cascades its wires, so a remove op naming a component this patch
        // already deleted is satisfied, not missing — report it as information, never a conflict.
        var removedSet = new HashSet<Guid>(removedGuids);
        foreach (GhJsonConnection conn in patch.Patch?.Connections?.Remove ?? Enumerable.Empty<GhJsonConnection>())
        {
            if (conn.From is { } from && conn.To is { } to
                && ((EndpointGuid(from.Id, index) is Guid f && removedSet.Contains(f))
                    || (EndpointGuid(to.Id, index) is Guid t && removedSet.Contains(t))))
            {
                warnings.Add($"connection remove ({from.Id} -> {to.Id}): already satisfied — a component it touches was removed by this patch, which removed its wires.");
                continue;
            }

            WireConnection(conn, doc, addIds, new Dictionary<int, IGH_DocumentObject>(), index, modifiedGuids, conflicts, removing: true);
        }
    }

    // The instanceGuid an endpoint id had in the base export, if any.
    private static Guid? EndpointGuid(int id, BaseIndex index) =>
        index.ById.TryGetValue(id, out GhJsonComponent? comp) ? comp.InstanceGuid : null;

    private static void ApplyConnectionAdds(
        GhPatchDocument patch,
        GH_Document doc,
        HashSet<int> addIds,
        Dictionary<int, IGH_DocumentObject> addedById,
        BaseIndex index,
        List<Guid> modifiedGuids,
        List<string> conflicts)
    {
        // Connections touching an added component were handled in the add pass.
        foreach (GhJsonConnection conn in (patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>())
            .Where(c => c.From is { } f && c.To is { } t && !addIds.Contains(f.Id) && !addIds.Contains(t.Id)))
        {
            WireConnection(conn, doc, addIds, addedById, index, modifiedGuids, conflicts, removing: false);
        }
    }

    // Adds or removes one wire. Endpoints resolve through the base export (id -> instanceGuid ->
    // live object) or the freshly added objects; parameters resolve paramIndex-first with
    // name/nickname fallback via the shared FindEndpointParam. The sink component counts as
    // modified so the health scan downstream covers it.
    private static void WireConnection(
        GhJsonConnection conn,
        GH_Document doc,
        HashSet<int> addIds,
        Dictionary<int, IGH_DocumentObject> addedById,
        BaseIndex index,
        List<Guid> modifiedGuids,
        List<string> conflicts,
        bool removing)
    {
        string verb = removing ? "connection_not_found" : "connection failed";

        if (conn.From is not { } from || conn.To is not { } to)
        {
            return;
        }

        IGH_DocumentObject? fromObj = ResolveEndpointObject(from.Id, doc, addedById, index);
        IGH_DocumentObject? toObj = ResolveEndpointObject(to.Id, doc, addedById, index);
        if (fromObj is null || toObj is null)
        {
            conflicts.Add($"{verb}: endpoint id {(fromObj is null ? from.Id : to.Id)} does not resolve to a live object.");
            return;
        }

        // paramIndex wins (the schema requires it and, unlike names, it is exact against the
        // export's parameter order); name and nickname remain the fallback.
        IGH_Param? source = FindEndpointParam(fromObj, from, output: true, preferIndex: true);
        IGH_Param? sink = FindEndpointParam(toObj, to, output: false, preferIndex: true);
        if (source is null || sink is null)
        {
            conflicts.Add(source is null
                ? $"{verb}: parameter {DescribeEndpoint(from, fromObj, null)} was not found on its component."
                : $"{verb}: parameter {DescribeEndpoint(to, toObj, null)} was not found on its component.");
            return;
        }

        if (removing)
        {
            if (!sink.Sources.Contains(source))
            {
                // Already absent — the state the model asked for. Reporting this as a failed
                // operation is a trap it cannot escape: "resubmit the corrected operations" has no
                // correction to make, so the same remove comes back forever (three rounds of it in
                // the 2026-07-25 White House session, on a wire an earlier rewire had already
                // taken out). Symmetric with the add path below, which treats an existing wire as
                // satisfied rather than failing the turn.
                return;
            }

            sink.RemoveSource(source);
        }
        else
        {
            if (sink.Sources.Contains(source))
            {
                return; // already present — idempotent, not worth failing the turn over
            }

            sink.AddSource(source);
        }

        if (SinkOwner(sink) is { } owner)
        {
            modifiedGuids.Add(owner.InstanceGuid);
        }
    }

    private static IGH_DocumentObject? ResolveEndpointObject(
        int id,
        GH_Document doc,
        Dictionary<int, IGH_DocumentObject> addedById,
        BaseIndex index)
    {
        if (addedById.TryGetValue(id, out IGH_DocumentObject? added))
        {
            return added;
        }

        return index.ById.TryGetValue(id, out GhJsonComponent? baseComp) && baseComp.InstanceGuid is Guid guid
            ? doc.FindObject(guid, false)
            : null;
    }

    private static IGH_DocumentObject? SinkOwner(IGH_Param sink) =>
        sink.Attributes?.GetTopLevel?.DocObject;

    // ---- Groups ------------------------------------------------------------------------------

    // Returns the instanceGuids of the groups this patch created, for master-group enrollment.
    private static List<Guid> ApplyGroupOps(
        GhPatchDocument patch,
        GH_Document doc,
        GhJsonDocument baseExport,
        BaseIndex index,
        Dictionary<int, IGH_DocumentObject> addedById,
        List<string> conflicts)
    {
        var created = new List<Guid>();
        GhPatchGroupsOp? groups = patch.Patch?.Groups;
        if (groups is null)
        {
            return created;
        }

        Guid? MemberGuid(int id)
        {
            if (addedById.TryGetValue(id, out IGH_DocumentObject? added))
            {
                return added.InstanceGuid;
            }

            return index.ById.TryGetValue(id, out GhJsonComponent? comp) ? comp.InstanceGuid : null;
        }

        GHGroupObject? ResolveGroup(Guid? instanceGuid, int? id)
        {
            GhJsonGroup? baseGroup = (baseExport.Groups ?? Enumerable.Empty<GhJsonGroup>())
                .FirstOrDefault(g => (instanceGuid is Guid ig && g.InstanceGuid == ig)
                    || (instanceGuid is null && id is int i && g.Id == i));
            return baseGroup?.InstanceGuid is Guid guid ? doc.FindObject(guid, false) as GHGroupObject : null;
        }

        foreach (GhPatchGroupModify modify in groups.Modify ?? Enumerable.Empty<GhPatchGroupModify>())
        {
            GHGroupObject? group = ResolveGroup(modify.Match.InstanceGuid, modify.Match.Id);
            if (group is null)
            {
                continue; // reported by the document-level apply
            }

            group.RecordUndoEvent("Physalia AI patch");

            if (modify.Set?["name"]?.ToString() is { } name && !string.IsNullOrWhiteSpace(name))
            {
                group.NickName = name;
            }

            if (modify.Set?["color"]?.ToString() is { } color && TryParseArgb(color, out Color parsed))
            {
                group.Colour = parsed;
            }

            foreach (int id in modify.Members?.Add ?? Enumerable.Empty<int>())
            {
                if (MemberGuid(id) is Guid guid)
                {
                    group.AddObject(guid);
                }
                else
                {
                    conflicts.Add($"dangling_member: group member id {id} does not resolve to a component.");
                }
            }

            foreach (int id in modify.Members?.Remove ?? Enumerable.Empty<int>())
            {
                if (MemberGuid(id) is Guid guid)
                {
                    group.RemoveObject(guid);
                }
            }

            group.ExpireCaches();
        }

        foreach (GhPatchGroupMatch match in groups.Remove ?? Enumerable.Empty<GhPatchGroupMatch>())
        {
            GHGroupObject? group = ResolveGroup(match.InstanceGuid, match.Id);
            if (group is not null)
            {
                doc.RemoveObject(group, false);
            }
        }

        // Authored group ids claim their objects in the stable-id registry alongside components, so
        // the number the model gave a group keeps meaning that group on the next canvas export —
        // the same invariant component adds rely on.
        var groupIds = new List<KeyValuePair<int, Guid>>();

        foreach (GhJsonGroup add in groups.Add ?? Enumerable.Empty<GhJsonGroup>())
        {
            var group = new GHGroupObject();
            if (!string.IsNullOrWhiteSpace(add.Name))
            {
                group.NickName = add.Name;
            }

            if (add.Color is { } color && TryParseArgb(color, out Color parsed))
            {
                group.Colour = parsed;
            }

            doc.AddObject(group, false);
            created.Add(group.InstanceGuid);
            foreach (int id in add.Members ?? Enumerable.Empty<int>())
            {
                if (MemberGuid(id) is Guid guid)
                {
                    group.AddObject(guid);
                }
                else
                {
                    conflicts.Add($"dangling_member: group member id {id} does not resolve to a component.");
                }
            }

            if (group.Attributes is not null)
            {
                group.Attributes.Selected = false;
            }

            if (add.Id is int authoredId)
            {
                groupIds.Add(new KeyValuePair<int, Guid>(authoredId, group.InstanceGuid));
            }
        }

        RegisterStableIds(doc, groupIds);
        return created;
    }

    // Parses the GhJSON colour form "argb:255,200,220,255" (a bare "255,200,220,255" is tolerated).
    private static bool TryParseArgb(string text, out Color color)
    {
        color = Color.Empty;
        string body = text.StartsWith("argb:", StringComparison.OrdinalIgnoreCase) ? text[5..] : text;
        string[] parts = body.Split(',');
        if (parts.Length != 4
            || !int.TryParse(parts[0], out int a) || !int.TryParse(parts[1], out int r)
            || !int.TryParse(parts[2], out int g) || !int.TryParse(parts[3], out int b))
        {
            return false;
        }

        color = Color.FromArgb(
            Math.Clamp(a, 0, 255), Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        return true;
    }
}
