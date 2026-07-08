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
    string? PostApplyChecksum = null);

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
    /// Returns whether <paramref name="json"/> parses and validates as a ghpatch document.
    /// </summary>
    /// <param name="json">The JSON string to validate.</param>
    /// <param name="message">Validation failure message, or null on success.</param>
    /// <returns>true if valid; false otherwise.</returns>
    internal static bool IsPatchJson(string json, out string? message)
    {
        try
        {
            GhPatchDocument patch = GhJson.PatchFromJson(json);
            ValidationResult validation = GhJson.ValidatePatch(patch);
            if (!validation.IsValid)
            {
                message = string.Join("\n", validation.Errors.Select(e => e.ToString()));
                return false;
            }

            message = null;
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

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
    /// <returns>A <see cref="CanvasPatchOutcome"/> describing the outcome.</returns>
    internal static CanvasPatchOutcome ApplyPatchToCanvas(string patchJson, PointF fallbackOrigin, bool verifyBase)
    {
        GH_Document? doc = Grasshopper.Instances.ActiveCanvas?.Document;
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

        CanvasStateSnapshot? snapshot = TryExportCanvasState(doc);
        if (snapshot is null)
        {
            return PatchFailure("The canvas state could not be exported.");
        }

        GhJsonDocument baseExport = snapshot.Document;

        string? carried = patch.Patch?.Base?.Checksum?.Trim();
        if (verifyBase && !string.IsNullOrEmpty(carried)
            && !string.Equals(carried, snapshot.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            // Carry the FRESH canvas state and checksum in the feedback itself, not just a pointer to
            // the system-prompt grounding (which rides a separate, eventually-fresh channel). This
            // makes the retry deterministic: the model regenerates against the exact state the next
            // patch will apply against, so a concurrent user edit converges in one round-trip instead
            // of dead-ending. Kept as Payload text — carrier discipline holds (no new signal field).
            var mismatch = new StringBuilder();
            mismatch.AppendLine(
                "base_checksum_mismatch: the canvas changed since you last saw it, so this patch was NOT "
                + "applied. Regenerate the patch against the CURRENT canvas state below.");
            mismatch.AppendLine();
            mismatch.AppendLine("Base checksum — copy this verbatim into patch.base.checksum: " + snapshot.Checksum);

            if (snapshot.ComponentCount > 0)
            {
                mismatch.AppendLine();
                mismatch.AppendLine("Current canvas state (GhJSON):");
                mismatch.Append(snapshot.Json);
            }
            else
            {
                mismatch.AppendLine();
                mismatch.Append("The canvas is now empty — emit a full GhJSON document instead of a patch.");
            }

            return PatchFailure(mismatch.ToString());
        }

        var conflicts = new List<string>();
        var warnings = new List<string>();
        var addedGuids = new List<Guid>();
        var modifiedGuids = new List<Guid>();
        var removedGuids = new List<Guid>();

        // ---- Pre-pass on adds: stamp real component guids, remap colliding ids, and give
        // pivot-less adds a fallback position. All before the document-level apply so the
        // post-merge document carries the corrected components. (Rhino-referenced inputs need no
        // reference pre-pass: they are ordinary components in the canvas state, marked
        // physalia.rhinoRef, and the model wires to them by id like anything else.)
        List<GhJsonComponent> adds = patch.Patch?.Components?.Add ?? new List<GhJsonComponent>();
        if (adds.Count > 0)
        {
            StampComponentGuids(new GhJsonDocument("1.0", null, adds, null, null));
            RemapCollidingAddIds(patch, adds, baseExport);
            AssignFallbackPivots(adds, fallbackOrigin);
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

            ApplyModifies(patch, doc, baseExport, applyResult.Document, baseIndex, modifiedGuids, conflicts, warnings);
            ApplyRemoves(patch, baseExport, baseIndex, removedGuids, conflicts);

            Dictionary<int, IGH_DocumentObject> addedById =
                ApplyAdds(patch, adds, addIds, doc, baseIndex, addedGuids, modifiedGuids, conflicts, warnings);

            ApplyGroupOps(patch, doc, baseExport, baseIndex, addedById, conflicts);
            ApplyConnectionRemoves(patch, doc, addIds, baseIndex, removedGuids, modifiedGuids, conflicts, warnings);
            ApplyConnectionAdds(patch, doc, addIds, addedById, baseIndex, modifiedGuids, conflicts);

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

        // A partial apply means the model must resubmit — and the operations that DID land changed
        // the canvas, so its old base checksum is guaranteed stale. Hand the fresh one back in the
        // outcome so the feedback can carry it and the retry cannot mismatch.
        string? postApplyChecksum = conflicts.Count > 0 ? TryExportCanvasState(doc)?.Checksum : null;

        return new CanvasPatchOutcome(
            conflicts.Count == 0,
            null,
            addedGuids,
            modifiedGuids.Distinct().ToList(),
            removedGuids,
            conflicts,
            warnings,
            postApplyChecksum);
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

    // Positions pivot-less adds in a vertical stack at the fallback origin. The preamble instructs
    // the model to author pivots relative to the existing layout it can see, so this is a net only.
    private static void AssignFallbackPivots(List<GhJsonComponent> adds, PointF fallbackOrigin)
    {
        int stacked = 0;
        foreach (GhJsonComponent add in adds)
        {
            if (add.Pivot is null)
            {
                add.Pivot = new GhJsonPivot(
                    (int)MathF.Round(fallbackOrigin.X),
                    (int)MathF.Round(fallbackOrigin.Y) + (stacked * 80));
                stacked++;
            }
        }
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
        List<string> warnings)
    {
        foreach (GhPatchComponentModify modify in patch.Patch?.Components?.Modify ?? Enumerable.Empty<GhPatchComponentModify>())
        {
            GhJsonComponent? baseComp = ResolveComponentMatch(modify.Match, baseExport, index);
            if (baseComp?.InstanceGuid is not Guid guid)
            {
                continue; // the document-level apply reported match_not_found/ambiguous already
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
                continue; // e.g. the same component was also removed; the remove pass handles it
            }

            if (TryFastPathModify(modify, live))
            {
                modifiedGuids.Add(guid);
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
            }
        }
    }

    // Applies a modify by mutating the live object directly when EVERY requested change is one of
    // the simple cases: nickName, pivot, locked/hidden, a slider value, or panel text. Anything
    // else (field removes, parameter settings, other extensions) returns false so the caller falls
    // back to replacing the object with its full post-merge state.
    private static bool TryFastPathModify(GhPatchComponentModify modify, IGH_DocumentObject live)
    {
        if (modify.Remove is { Count: > 0 }
            || modify.InputSettings is not null
            || modify.OutputSettings is not null)
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

        (live as IGH_ActiveObject)?.ExpireSolution(false);
        live.Attributes?.ExpireLayout();
        return true;
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
        PutResult result = GhJsonGrasshopper.Put(oneDoc, new PutOptions
        {
            Offset = PointF.Empty,
            AutoOffset = false,
            CreateConnections = false,
            CreateGroups = false,
            RegenerateInstanceGuids = false,
            SkipInvalidComponents = false,
            SelectPlacedObjects = false,
        });

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

        DeleteResult result = GhJsonGrasshopper.Delete(guids, new DeleteOptions { Redraw = false });
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
        PutResult result = GhJsonGrasshopper.Put(addDoc, BuildPutOptions(PointF.Empty));

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
        // keeps the numbering the model authored and its remembered ids stay valid.
        RegisterStableIds(doc, result.IdToGuidMapping);

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
            conflicts.Add($"{verb}: parameter '{(source is null ? from.ParamName : to.ParamName)}' was not found on its component.");
            return;
        }

        if (removing)
        {
            if (!sink.Sources.Contains(source))
            {
                conflicts.Add($"connection_not_found: no wire from '{from.ParamName}' to '{to.ParamName}' exists.");
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

    private static void ApplyGroupOps(
        GhPatchDocument patch,
        GH_Document doc,
        GhJsonDocument baseExport,
        BaseIndex index,
        Dictionary<int, IGH_DocumentObject> addedById,
        List<string> conflicts)
    {
        GhPatchGroupsOp? groups = patch.Patch?.Groups;
        if (groups is null)
        {
            return;
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
        }
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
