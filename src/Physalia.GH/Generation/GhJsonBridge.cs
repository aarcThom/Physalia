// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using GhJSON.Core;
using GhJSON.Core.SchemaModels;
using GhJSON.Core.Serialization;
using GhJSON.Grasshopper;
using GhJSON.Grasshopper.PutOperations;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Components;
using Physalia.GH.Components;
using Physalia.GH.Components.Utility;

namespace Physalia.GH.Generation;

/// <summary>
/// Result returned by <see cref="GhJsonBridge.LoadAndPlace"/>.
/// </summary>
internal sealed record PlaceResult(
    bool Success,
    int ComponentCount,
    int ConnectionCount,
    int WarningCount,
    string? ErrorMessage,
    IReadOnlyList<Guid> PlacedGuids,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> UnfixedIssues);

/// <summary>
/// Façade over the GhJSON library. All direct GhJSON API calls originate here;
/// components interact with the library exclusively through this class.
/// </summary>
internal static class GhJsonBridge
{
    /// <summary>
    /// True while <see cref="LoadAndPlace"/> is executing. Components that auto-place
    /// Pickers in <c>AddedToDocument</c> check this flag to suppress duplicate placement
    /// when the component was already wired in the imported file.
    /// </summary>
    internal static bool IsImporting { get; private set; }

    // componentState.extensions key under which a Feedback component's linked FeedbackCollector
    // references are stored, as GhJSON component ids (not InstanceGuids, which Put regenerates).
    // GhJSON treats extensions as an opaque pass-through, so this round-trips untouched.
    private const string FeedbackCollectorsExtensionKey = "physalia.feedbackCollectors";

    // componentState.extensions key under which a Picker's selected value is stored, so a
    // right-click selection survives .ghjson export/import (GhJSON does not capture a component's
    // native Write/Read blob, where the Picker otherwise persists it). The stored value is always
    // a benign label (a provider name, file name, or model id) — never a secret. GhJSON treats
    // extensions as an opaque pass-through, so this round-trips untouched.
    private const string PickerValueExtensionKey = "physalia.pickerValue";

    // componentState.extensions key under which a Recorder's grounding selection is stored, so a
    // preset carries which component-catalog tabs/panels are folded into the system prompt. Absent =
    // null selection = include everything (the default), matching the Picker's "skip when no
    // selection". Stored as benign labels (tab/panel names) — never a secret.
    private const string GroundingSelectionExtensionKey = "physalia.groundingSelection";

    /// <summary>
    /// Exports the Grasshopper objects identified by <paramref name="guids"/> to a
    /// <c>.ghjson</c> file at <paramref name="path"/>.
    /// </summary>
    /// <param name="guids">The instance GUIDs of the objects to export.</param>
    /// <param name="path">The destination file path.</param>
    /// <param name="comment">
    /// Optional free-text description written to the file's <c>metadata.description</c>. Blank or
    /// null leaves the metadata block out entirely.
    /// </param>
    internal static void ExportToFile(IReadOnlyList<Guid> guids, string path, string? comment = null)
    {
        GhJsonDocument doc = GhJsonGrasshopper.GetByGuids(guids);
        StripNickNames(doc);

        // Wireless Feedback -> FeedbackCollector links are not GH wires, so GetByGuids misses them.
        // Capture them (component-id based) into each Feedback's extensions before writing.
        InjectFeedbackLinks(doc);

        // The Picker's selected value lives in its native Write/Read blob, which GhJSON does not
        // capture; persist it into the component's extensions so the selection survives the round-trip.
        InjectPickerValues(doc);

        // A Recorder's grounding selection lives in its native Write/Read blob too; persist it so a
        // preset carries the chosen tabs/panels.
        InjectGroundingSelection(doc);

        // Metadata has a private setter, so a user comment is injected by rebuilding the document
        // (the component objects were mutated in place above, so they carry over).
        if (!string.IsNullOrWhiteSpace(comment))
        {
            doc = new GhJsonDocument(
                doc.Schema,
                new GhJsonMetadata { Description = comment.Trim() },
                doc.Components,
                doc.Connections,
                doc.Groups);
        }

        GhJson.ToFile(doc, path, new WriteOptions { Indented = true });
    }

    /// <summary>
    /// Records each exported <see cref="Feedback"/> component's linked FeedbackCollector targets as
    /// GhJSON component ids under <see cref="FeedbackCollectorsExtensionKey"/> in the component's
    /// state extensions. Only collectors that are themselves part of the export are recorded — a
    /// link to a collector outside the selection cannot be expressed in the file and is dropped.
    /// </summary>
    /// <param name="doc">The freshly captured document to annotate in place.</param>
    private static void InjectFeedbackLinks(GhJsonDocument doc)
    {
        GH_Document? live = Grasshopper.Instances.ActiveCanvas?.Document;
        if (live is null)
        {
            return;
        }

        var guidToId = new Dictionary<Guid, int>();
        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.InstanceGuid is Guid g && component.Id is int id)
            {
                guidToId[g] = id;
            }
        }

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.InstanceGuid is not Guid guid
                || live.FindObject(guid, false) is not Feedback feedback)
            {
                continue;
            }

            var collectorIds = new List<int>();
            foreach (Guid collectorGuid in feedback.CollectorGuids)
            {
                if (guidToId.TryGetValue(collectorGuid, out int collectorId))
                {
                    collectorIds.Add(collectorId);
                }
            }

            if (collectorIds.Count == 0)
            {
                continue;
            }

            component.ComponentState ??= new GhJsonComponentState();
            component.ComponentState.Extensions ??= new Dictionary<string, object>();
            component.ComponentState.Extensions[FeedbackCollectorsExtensionKey] =
                new FeedbackLinkExtension { CollectorIds = collectorIds };
        }
    }

    /// <summary>
    /// Records each exported <see cref="Picker"/> component's selected value under
    /// <see cref="PickerValueExtensionKey"/> in its state extensions, so a right-click selection
    /// survives the .ghjson round-trip. Pickers with no selection yet are skipped. The value is a
    /// plain label (provider name, file name, model id) — API keys themselves are never stored, only
    /// the choice of which key to use, which is safe.
    /// </summary>
    /// <param name="doc">The freshly captured document to annotate in place.</param>
    private static void InjectPickerValues(GhJsonDocument doc)
    {
        GH_Document? live = Grasshopper.Instances.ActiveCanvas?.Document;
        if (live is null)
        {
            return;
        }

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.InstanceGuid is not Guid guid
                || live.FindObject(guid, false) is not Picker picker
                || string.IsNullOrEmpty(picker.SelectedValue))
            {
                continue;
            }

            component.ComponentState ??= new GhJsonComponentState();
            component.ComponentState.Extensions ??= new Dictionary<string, object>();
            component.ComponentState.Extensions[PickerValueExtensionKey] = picker.SelectedValue;
        }
    }

    /// <summary>
    /// Records each exported <see cref="Recorder"/>'s grounding selection under
    /// <see cref="GroundingSelectionExtensionKey"/> in its state extensions, so a preset carries which
    /// component-catalog tabs/panels are folded into the system prompt. Recorders with the default
    /// (null = include everything) selection are skipped, so an absent extension restores as null.
    /// </summary>
    /// <param name="doc">The freshly captured document to annotate in place.</param>
    private static void InjectGroundingSelection(GhJsonDocument doc)
    {
        GH_Document? live = Grasshopper.Instances.ActiveCanvas?.Document;
        if (live is null)
        {
            return;
        }

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.InstanceGuid is not Guid guid
                || live.FindObject(guid, false) is not Recorder recorder
                || recorder.GroundingSelectionOrNull is not { } selection)
            {
                continue;
            }

            var leaves = selection.Leaves
                .Select(leaf => new GroundingLeaf { Category = leaf.Category, SubCategory = leaf.SubCategory })
                .ToList();

            component.ComponentState ??= new GhJsonComponentState();
            component.ComponentState.Extensions ??= new Dictionary<string, object>();
            component.ComponentState.Extensions[GroundingSelectionExtensionKey] =
                new GroundingSelectionExtension { Leaves = leaves };
        }
    }

    /// <summary>
    /// Serialises a <see cref="PhySchemaDocument"/> (the LLM-authored, position-free definition)
    /// to a GhJSON string, stamping each component with the canvas pivot computed by the caller.
    /// </summary>
    /// <param name="schema">The parsed PhySchema document.</param>
    /// <param name="pivots">Canvas pivot positions keyed by component id; components without an
    /// entry fall back to (50, 50).</param>
    /// <returns>The GhJSON document as an indented string.</returns>
    internal static string SerializePhySchema(PhySchemaDocument schema, IReadOnlyDictionary<int, PointF> pivots)
    {
        var components = new List<GhJsonComponent>();
        foreach (PhySchemaComponent component in schema.Components ?? Array.Empty<PhySchemaComponent>())
        {
            PointF pivot = pivots.TryGetValue(component.Id, out PointF p) ? p : new PointF(50f, 50f);

            var ghComponent = new GhJsonComponent
            {
                Name = component.Name,
                Id = component.Id,
                Pivot = new GhJsonPivot(pivot.X, pivot.Y),
            };

            if (Guid.TryParse(component.InstanceGuid, out Guid instanceGuid))
            {
                ghComponent.InstanceGuid = instanceGuid;
            }

            if (component.ComponentState is JsonElement state)
            {
                ghComponent.ComponentState = JsonConvert.DeserializeObject<GhJsonComponentState>(state.GetRawText());
            }

            components.Add(ghComponent);
        }

        var connections = new List<GhJsonConnection>();
        foreach (PhySchemaConnection connection in schema.Connections ?? Array.Empty<PhySchemaConnection>())
        {
            connections.Add(new GhJsonConnection
            {
                From = new GhJsonConnectionEndpoint { Id = connection.From.Id, ParamName = connection.From.ParamName },
                To = new GhJsonConnectionEndpoint { Id = connection.To.Id, ParamName = connection.To.ParamName },
            });
        }

        var doc = new GhJsonDocument("1.0", metadata: null, components, connections, groups: null);
        return GhJson.ToJson(doc, new WriteOptions { Indented = true });
    }

    /// <summary>
    /// Returns whether <paramref name="json"/> is a valid GhJSON document.
    /// </summary>
    /// <param name="json">The JSON string to validate.</param>
    /// <param name="message">Validation failure message, or null on success.</param>
    /// <returns>true if valid; false otherwise.</returns>
    internal static bool IsValidJson(string json, out string? message)
    {
        return GhJson.IsValid(json, out message);
    }

    /// <summary>
    /// Rewrites each component's name in a GhJSON string to a real installed component, matched
    /// against <paramref name="catalog"/>, and stamps the resolved component-type GUID so the
    /// library can create it exactly. Components that already carry a non-empty
    /// <c>componentGuid</c> are left untouched. Names that cannot be matched confidently are
    /// returned for feedback rather than guessed.
    /// </summary>
    /// <param name="json">The GhJSON document as a string.</param>
    /// <param name="catalog">The installed-component catalog to resolve against.</param>
    /// <returns>The resolved GhJSON and the list of names that could not be resolved.</returns>
    internal static (string Json, IReadOnlyList<string> Unresolved) ResolveComponentNames(string json, ComponentCatalog catalog)
    {
        GhJsonDocument doc = GhJson.FromJson(json);
        var unresolved = new List<string>();

        if (doc.Components is not null)
        {
            foreach (GhJsonComponent component in doc.Components)
            {
                if (component.ComponentGuid is not null && component.ComponentGuid.Value != Guid.Empty)
                {
                    continue;
                }

                string proposed = component.Name ?? string.Empty;
                ComponentMatcher.MatchResult match = ComponentMatcher.Match(proposed, catalog);

                if (match.IsConfident && match.Entry is not null)
                {
                    component.Name = match.Entry.Name;
                    component.ComponentGuid = match.Entry.ComponentGuid;
                }
                else
                {
                    unresolved.Add(string.IsNullOrWhiteSpace(proposed) ? "(unnamed component)" : proposed);
                }
            }
        }

        return (GhJson.ToJson(doc), unresolved);
    }

    /// <summary>
    /// Loads a <c>.ghjson</c> file and places its components onto the active Grasshopper
    /// canvas, with the content's top-left pivot aligned to <paramref name="targetOrigin"/>.
    /// </summary>
    /// <param name="path">Path to the <c>.ghjson</c> file.</param>
    /// <param name="targetOrigin">Canvas position for the top-left corner of the placed content.</param>
    /// <returns>A <see cref="PlaceResult"/> describing the outcome.</returns>
    internal static PlaceResult LoadAndPlace(string path, PointF targetOrigin)
    {
        return PlaceDocument(GhJson.FromFile(path), targetOrigin);
    }

    /// <summary>
    /// Reads the <c>metadata.description</c> from a <c>.ghjson</c> file, or null if the file is
    /// missing/invalid or carries no description. Used to surface preset descriptions in the UI.
    /// </summary>
    /// <param name="path">Path to the <c>.ghjson</c> file.</param>
    /// <returns>The description text, or null when none is available.</returns>
    internal static string? TryReadMetadataDescription(string path)
    {
        try
        {
            return GhJson.FromFile(path).Metadata?.Description;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a GhJSON string and places its components onto the active Grasshopper canvas,
    /// with the content's top-left pivot aligned to <paramref name="targetOrigin"/>.
    /// </summary>
    /// <param name="json">The GhJSON document as a string.</param>
    /// <param name="targetOrigin">Canvas position for the top-left corner of the placed content.</param>
    /// <returns>A <see cref="PlaceResult"/> describing the outcome.</returns>
    internal static PlaceResult LoadAndPlaceJson(string json, PointF targetOrigin)
    {
        return PlaceDocument(GhJson.FromJson(json), targetOrigin);
    }

    /// <summary>
    /// Places the components of an already-parsed GhJSON document onto the active Grasshopper
    /// canvas, with the content's top-left pivot aligned to <paramref name="targetOrigin"/>.
    /// </summary>
    /// <param name="doc">The parsed GhJSON document.</param>
    /// <param name="targetOrigin">Canvas position for the top-left corner of the placed content.</param>
    /// <returns>A <see cref="PlaceResult"/> describing the outcome.</returns>
    private static PlaceResult PlaceDocument(GhJsonDocument doc, PointF targetOrigin)
    {
        if (doc.Components is null || doc.Components.Count == 0)
        {
            return EmptyDocumentResult();
        }

        // Normalise AI-authored JSON (missing ids, stray fields, near-miss structure) before placing.
        // The GhJSON library's fixer is the single source of repair; anything it cannot fix is surfaced
        // to the caller so it can be routed back to the model as feedback.
        var fixResult = GhJson.Fix(doc);
        doc = fixResult.Document;
        IReadOnlyList<string> unfixedIssues = fixResult.UnfixedIssues ?? (IReadOnlyList<string>)Array.Empty<string>();

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Pivot is not null)
            {
                minX = Math.Min(minX, (float)component.Pivot.X);
                minY = Math.Min(minY, (float)component.Pivot.Y);
            }
        }

        if (minX == float.MaxValue)
        {
            minX = 0f;
            minY = 0f;
        }

        var options = BuildPutOptions(new PointF(targetOrigin.X - minX, targetOrigin.Y - minY));
        return ExecutePut(doc, options, unfixedIssues);
    }

    /// <summary>
    /// Loads a <c>.ghjson</c> preset and places it anchored to an existing live component. The first
    /// component in the file whose type matches <paramref name="placeholderComponentGuid"/> is treated
    /// as a placeholder slot: it is not instantiated; instead <paramref name="anchor"/> is spliced in
    /// where it would have gone. The rest of the graph is offset so the placeholder's pivot lands on
    /// the anchor's pivot (preserving the preset's relative layout), and every connection that touched
    /// the placeholder is re-wired to the matching parameter on <paramref name="anchor"/>.
    /// </summary>
    /// <param name="path">Path to the <c>.ghjson</c> file.</param>
    /// <param name="anchor">The live component to splice in for the placeholder slot.</param>
    /// <param name="placeholderComponentGuid">The component-type GUID that marks the placeholder slot.</param>
    /// <returns>A <see cref="PlaceResult"/> describing the outcome.</returns>
    internal static PlaceResult LoadAndPlaceAnchored(string path, IGH_Component anchor, Guid placeholderComponentGuid)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        GhJsonDocument doc = GhJson.FromFile(path);
        if (doc.Components is null || doc.Components.Count == 0)
        {
            return EmptyDocumentResult();
        }

        var fixResult = GhJson.Fix(doc);
        doc = fixResult.Document;
        IReadOnlyList<string> unfixedIssues = fixResult.UnfixedIssues ?? (IReadOnlyList<string>)Array.Empty<string>();

        // Locate the first placeholder slot (e.g. the preset's own Chatbox). With no slot to splice,
        // fall back to ordinary placement anchored at the live component's pivot.
        GhJsonComponent? placeholder = doc.Components.FirstOrDefault(
            c => c.ComponentGuid is Guid g && g == placeholderComponentGuid);

        PointF anchorPivot = anchor.Attributes?.Pivot ?? new PointF(50f, 50f);

        if (placeholder is null || placeholder.Id is not int placeholderId || placeholder.Pivot is null)
        {
            return PlaceDocument(doc, anchorPivot);
        }

        // Capture every connection touching the placeholder, recording the OTHER endpoint plus the
        // placeholder-side parameter name and direction, then keep only the connections that don't.
        var rewires = new List<RewireRequest>();
        var keptConnections = new List<GhJsonConnection>();
        foreach (GhJsonConnection conn in doc.Connections ?? Enumerable.Empty<GhJsonConnection>())
        {
            bool fromSlot = conn.From?.Id is int fromId && fromId == placeholderId;
            bool toSlot = conn.To?.Id is int toId && toId == placeholderId;

            if (fromSlot && toSlot)
            {
                continue; // self-loop on the slot — nothing meaningful to re-wire
            }

            if (fromSlot && conn.To is { } to && to.Id is int toOtherId)
            {
                rewires.Add(new RewireRequest(toOtherId, to.ParamName, conn.From!.ParamName, SlotIsSource: true));
            }
            else if (toSlot && conn.From is { } from && from.Id is int fromOtherId)
            {
                rewires.Add(new RewireRequest(fromOtherId, from.ParamName, conn.To!.ParamName, SlotIsSource: false));
            }
            else
            {
                keptConnections.Add(conn);
            }
        }

        var keptComponents = doc.Components.Where(c => !ReferenceEquals(c, placeholder)).ToList();
        var prunedDoc = new GhJsonDocument(doc.Schema, doc.Metadata, keptComponents, keptConnections, doc.Groups);

        var options = BuildPutOptions(new PointF(
            anchorPivot.X - (float)placeholder.Pivot.X,
            anchorPivot.Y - (float)placeholder.Pivot.Y));

        return ExecutePut(prunedDoc, options, unfixedIssues, result => RewireAnchor(anchor, rewires, result));
    }

    // Re-establishes the placeholder's connections against the live anchor component. Each captured
    // endpoint id is remapped to its newly-placed InstanceGuid via Put's id-to-guid mapping, the
    // matching parameter is found by name on both sides, and a source is added in the recorded
    // direction. Every lookup is guarded; an unresolved endpoint is skipped.
    private static void RewireAnchor(IGH_Component anchor, IReadOnlyList<RewireRequest> rewires, PutResult result)
    {
        foreach (RewireRequest rewire in rewires)
        {
            if (!result.IdToGuidMapping.TryGetValue(rewire.OtherId, out Guid otherGuid))
            {
                continue;
            }

            IGH_DocumentObject? placed = result.PlacedObjects.FirstOrDefault(o => o.InstanceGuid == otherGuid);
            if (placed is null)
            {
                continue;
            }

            IGH_Param? source;
            IGH_Param? sink;
            if (rewire.SlotIsSource)
            {
                // anchor output -> placed component input
                source = FindParam(anchor, rewire.SlotParamName, output: true);
                sink = FindParam(placed, rewire.OtherParamName, output: false);
            }
            else
            {
                // placed component output -> anchor input
                source = FindParam(placed, rewire.OtherParamName, output: true);
                sink = FindParam(anchor, rewire.SlotParamName, output: false);
            }

            if (source is not null && sink is not null)
            {
                sink.AddSource(source);
            }
        }
    }

    // Finds an input or output parameter by full Name on a placed object: a component's matching param
    // list, or the floating param itself. Returns null when no match exists.
    private static IGH_Param? FindParam(IGH_DocumentObject obj, string? name, bool output)
    {
        if (obj is IGH_Component component)
        {
            IList<IGH_Param> list = output ? component.Params.Output : component.Params.Input;
            return list.FirstOrDefault(p => p.Name == name);
        }

        return obj as IGH_Param;
    }

    // Shared PutOptions for every Physalia placement: explicit offset (no auto-layout), connections
    // and groups created, fresh instance guids, invalid components skipped, placed objects left
    // deselected (so a fresh deserialize/placement doesn't drop a selection lasso on the canvas).
    private static PutOptions BuildPutOptions(PointF offset) => new PutOptions
    {
        Offset = offset,
        AutoOffset = false,
        CreateConnections = true,
        CreateGroups = true,
        RegenerateInstanceGuids = true,
        SkipInvalidComponents = true,
        SelectPlacedObjects = false,
    };

    // Runs Put for an already-Fixed document with caller-built options, then on success applies
    // nickname display, restores wireless feedback links, and runs the optional post-placement step
    // (used to splice an anchor component into a placeholder slot). Shapes the PlaceResult.
    private static PlaceResult ExecutePut(GhJsonDocument doc, PutOptions options, IReadOnlyList<string> unfixedIssues, Action<PutResult>? afterPlace = null)
    {
        IsImporting = true;
        PutResult result;
        try
        {
            result = GhJsonGrasshopper.Put(doc, options);
        }
        finally
        {
            IsImporting = false;
        }

        if (result.Success)
        {
            ComponentHelpers.ApplyNickNameDisplay(result.PlacedObjects);

            // PutOptions.SelectPlacedObjects is false, but the GhJSON library still selects created
            // groups (and their members), so a preset carrying a group lands selected. Deselect every
            // freshly placed object so all placements come in clean.
            DeselectPlaced(result);

            // The GhJSON library only reconstructs variable parameters for recognised script
            // components, so a custom IGH_VariableParameterComponent (e.g. Router) is placed with
            // its default params only — every user-added input/output in the file is dropped, and the
            // wires that targeted them fail during Put. Recreate the missing params from the file's
            // settings, then restore those dropped connections.
            IReadOnlyCollection<int> reconciled = ReconcileVariableParameters(doc, result);
            if (reconciled.Count > 0)
            {
                RecreateMissingConnections(doc, result, reconciled);
            }

            RestoreFeedbackLinks(doc, result);
            bool pickersRestored = RestorePickerValues(doc, result);
            bool groundingRestored = RestoreGroundingSelection(doc, result);
            afterPlace?.Invoke(result);

            // Params and wires were changed after Put's own solution, so re-solve the now-expired
            // components (and their downstream) to bring recreated variable params and restored
            // Picker/grounding selections live.
            if (reconciled.Count > 0 || pickersRestored || groundingRestored)
            {
                result.PlacedObjects.FirstOrDefault()?.OnPingDocument()?.NewSolution(false);
            }
        }

        return result.Success
            ? new PlaceResult(true, result.ComponentsPlaced, result.ConnectionsCreated, result.Warnings.Count, null, result.PlacedObjects.Select(o => o.InstanceGuid).ToList(), result.Warnings.ToList(), unfixedIssues)
            : new PlaceResult(false, 0, 0, 0, result.ErrorMessage, Array.Empty<Guid>(), Array.Empty<string>(), unfixedIssues);
    }

    // Deselects every freshly placed object plus any group that contains one. The GhJSON library
    // selects groups it creates (and their members) regardless of PutOptions.SelectPlacedObjects, so
    // a preset carrying a group otherwise lands selected; a created group is not always reported in
    // PlacedObjects, hence the separate group sweep over the live document.
    private static void DeselectPlaced(PutResult result)
    {
        var placedGuids = new HashSet<Guid>(result.PlacedObjects.Select(o => o.InstanceGuid));

        foreach (IGH_DocumentObject obj in result.PlacedObjects)
        {
            if (obj.Attributes is not null)
            {
                obj.Attributes.Selected = false;
            }
        }

        GH_Document? doc = result.PlacedObjects.FirstOrDefault()?.OnPingDocument();
        if (doc is null)
        {
            return;
        }

        foreach (Grasshopper.Kernel.Special.GH_Group group in doc.Objects.OfType<Grasshopper.Kernel.Special.GH_Group>())
        {
            if (group.Attributes is not null && group.ObjectIDs.Any(placedGuids.Contains))
            {
                group.Attributes.Selected = false;
            }
        }
    }

    private static PlaceResult EmptyDocumentResult() =>
        new PlaceResult(false, 0, 0, 0, "The GhJSON file contains no components to place.", Array.Empty<Guid>(), Array.Empty<string>(), Array.Empty<string>());

    /// <summary>
    /// Recreates variable parameters that the GhJSON library left off a placed
    /// <see cref="IGH_VariableParameterComponent"/>. The library only reconstructs them for known
    /// script components, so a node like <see cref="Router"/> arrives with its default params only.
    /// Each setting whose <c>parameterName</c> has no matching live param is minted by the node's own
    /// <c>CreateParameter</c> (so it gets the correct type and respects the node's insertion rules)
    /// and registered at the file's index.
    /// </summary>
    /// <param name="doc">The placed document (post-Fix, so ids match the Put mapping).</param>
    /// <param name="result">The Put result carrying the id-to-guid mapping and placed objects.</param>
    /// <returns>The GhJSON ids of the components whose params were changed.</returns>
    private static IReadOnlyCollection<int> ReconcileVariableParameters(GhJsonDocument doc, PutResult result)
    {
        var reconciled = new HashSet<int>();
        if (doc.Components is null)
        {
            return reconciled;
        }

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Id is not int id
                || PlacedById(result, id) is not IGH_Component comp
                || comp is not IGH_VariableParameterComponent varComp)
            {
                continue;
            }

            bool changed = ReconcileParamSide(comp, varComp, component.InputSettings, GH_ParameterSide.Input);
            changed |= ReconcileParamSide(comp, varComp, component.OutputSettings, GH_ParameterSide.Output);

            if (changed)
            {
                varComp.VariableParameterMaintenance();
                comp.Params.OnParametersChanged();
                comp.Attributes?.ExpireLayout();
                reconciled.Add(id);
            }
        }

        return reconciled;
    }

    /// <summary>
    /// Adds any parameter named in <paramref name="settings"/> that is missing from one side of a
    /// variable-parameter component. Existing params (fixed or already added) are left untouched, so
    /// the pass is idempotent and a no-op for script components the library already handled.
    /// </summary>
    /// <param name="component">The placed component.</param>
    /// <param name="varComp">The same object as its variable-parameter interface.</param>
    /// <param name="settings">The file's parameter settings for this side, in order.</param>
    /// <param name="side">Which side (input/output) is being reconciled.</param>
    /// <returns>true when at least one parameter was added.</returns>
    private static bool ReconcileParamSide(
        IGH_Component component,
        IGH_VariableParameterComponent varComp,
        List<GhJsonParameterSettings>? settings,
        GH_ParameterSide side)
    {
        if (settings is null || settings.Count == 0)
        {
            return false;
        }

        bool changed = false;
        for (int i = 0; i < settings.Count; i++)
        {
            string? name = settings[i].ParameterName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            IList<IGH_Param> live = side == GH_ParameterSide.Input ? component.Params.Input : component.Params.Output;
            if (live.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)))
            {
                continue; // a fixed param, or one added on an earlier iteration
            }

            // Insert at the file's index when the node allows it there; fall back to the end. Skip if
            // the node refuses insertion on this side entirely (e.g. Router accepts only outputs).
            int index = Math.Min(i, live.Count);
            if (!varComp.CanInsertParameter(side, index))
            {
                if (varComp.CanInsertParameter(side, live.Count))
                {
                    index = live.Count;
                }
                else
                {
                    continue;
                }
            }

            IGH_Param param = varComp.CreateParameter(side, index);
            if (param is null)
            {
                continue;
            }

            param.Name = name;
            param.NickName = string.IsNullOrWhiteSpace(settings[i].NickName) ? name : settings[i].NickName;

            if (side == GH_ParameterSide.Input)
            {
                component.Params.RegisterInputParam(param, index);
            }
            else
            {
                component.Params.RegisterOutputParam(param, index);
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Re-creates the connections the library dropped because a variable param did not yet exist when
    /// it built wires. Only connections touching a reconciled component are considered, and a wire is
    /// added only when both endpoints resolve and it is not already present (idempotent).
    /// </summary>
    /// <param name="doc">The placed document.</param>
    /// <param name="result">The Put result carrying the id-to-guid mapping and placed objects.</param>
    /// <param name="reconciledIds">The ids of components whose params were just recreated.</param>
    private static void RecreateMissingConnections(GhJsonDocument doc, PutResult result, IReadOnlyCollection<int> reconciledIds)
    {
        foreach (GhJsonConnection conn in doc.Connections ?? Enumerable.Empty<GhJsonConnection>())
        {
            if (conn.From?.Id is not int fromId || conn.To?.Id is not int toId
                || (!reconciledIds.Contains(fromId) && !reconciledIds.Contains(toId)))
            {
                continue;
            }

            IGH_DocumentObject? fromObj = PlacedById(result, fromId);
            IGH_DocumentObject? toObj = PlacedById(result, toId);
            if (fromObj is null || toObj is null)
            {
                continue;
            }

            IGH_Param? source = FindParam(fromObj, conn.From.ParamName, output: true);
            IGH_Param? sink = FindParam(toObj, conn.To.ParamName, output: false);
            if (source is null || sink is null || sink.Sources.Contains(source))
            {
                continue;
            }

            sink.AddSource(source);
        }
    }

    /// <summary>
    /// Resolves a placed object from its GhJSON id via the Put id-to-guid mapping. Returns null when
    /// the id was not placed (e.g. an invalid component skipped by Put).
    /// </summary>
    /// <param name="result">The Put result.</param>
    /// <param name="id">The GhJSON component id.</param>
    /// <returns>The placed object, or null.</returns>
    private static IGH_DocumentObject? PlacedById(PutResult result, int id) =>
        result.IdToGuidMapping.TryGetValue(id, out Guid guid)
            ? result.PlacedObjects.FirstOrDefault(o => o.InstanceGuid == guid)
            : null;

    // A connection from the placeholder slot to re-wire against the live anchor: the other endpoint's
    // GhJSON id + parameter name, the slot-side parameter name, and whether the slot was the source.
    private readonly record struct RewireRequest(int OtherId, string? OtherParamName, string? SlotParamName, bool SlotIsSource);

    /// <summary>
    /// Re-establishes wireless Feedback -> FeedbackCollector links after placement. The links were
    /// stored as GhJSON component ids (see <see cref="InjectFeedbackLinks"/>); here each id is
    /// remapped to the newly-placed object's InstanceGuid via <see cref="PutResult.IdToGuidMapping"/>
    /// (Put regenerates guids, so the stored ones are stale) and re-applied with
    /// <see cref="Feedback.AddCollector"/>. Every lookup is guarded; a missing id is skipped.
    /// </summary>
    /// <param name="doc">The placed document (post-Fix, so ids match the Put mapping).</param>
    /// <param name="result">The Put result carrying the id-to-guid mapping and placed objects.</param>
    private static void RestoreFeedbackLinks(GhJsonDocument doc, PutResult result)
    {
        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Id is not int feedbackId
                || component.ComponentState?.Extensions is not { } extensions
                || !extensions.TryGetValue(FeedbackCollectorsExtensionKey, out object? raw))
            {
                continue;
            }

            FeedbackLinkExtension? link;
            try
            {
                // The extension round-trips as a Newtonsoft token; re-serialise to read it back typed.
                link = JsonConvert.DeserializeObject<FeedbackLinkExtension>(JsonConvert.SerializeObject(raw));
            }
            catch (Newtonsoft.Json.JsonException)
            {
                continue;
            }

            if (link?.CollectorIds is not { Count: > 0 } collectorIds
                || !result.IdToGuidMapping.TryGetValue(feedbackId, out Guid feedbackGuid))
            {
                continue;
            }

            if (result.PlacedObjects.FirstOrDefault(o => o.InstanceGuid == feedbackGuid) is not Feedback feedback)
            {
                continue;
            }

            foreach (int collectorId in collectorIds)
            {
                if (result.IdToGuidMapping.TryGetValue(collectorId, out Guid collectorGuid))
                {
                    feedback.AddCollector(collectorGuid);
                }
            }
        }
    }

    /// <summary>
    /// Restores each placed <see cref="Picker"/>'s selected value from the
    /// <see cref="PickerValueExtensionKey"/> extension written by <see cref="InjectPickerValues"/>.
    /// The component id is remapped to the newly-placed object's InstanceGuid via
    /// <see cref="PutResult.IdToGuidMapping"/> (Put regenerates guids). Returns whether any picker
    /// was restored, so the caller can trigger a re-solve to push the restored value downstream.
    /// </summary>
    /// <param name="doc">The placed document (post-Fix, so ids match the Put mapping).</param>
    /// <param name="result">The Put result carrying the id-to-guid mapping and placed objects.</param>
    /// <returns>true when at least one picker selection was restored.</returns>
    private static bool RestorePickerValues(GhJsonDocument doc, PutResult result)
    {
        bool restored = false;

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Id is not int id
                || component.ComponentState?.Extensions is not { } extensions
                || !extensions.TryGetValue(PickerValueExtensionKey, out object? raw))
            {
                continue;
            }

            // The extension round-trips as a Newtonsoft token (or a boxed string); ToString gives
            // the raw value either way.
            string? value = raw as string ?? raw?.ToString();
            if (string.IsNullOrEmpty(value)
                || !result.IdToGuidMapping.TryGetValue(id, out Guid guid)
                || result.PlacedObjects.FirstOrDefault(o => o.InstanceGuid == guid) is not Picker picker)
            {
                continue;
            }

            picker.SetSelectedValue(value);
            picker.ExpireSolution(false);
            restored = true;
        }

        return restored;
    }

    /// <summary>
    /// Restores each placed <see cref="Recorder"/>'s grounding selection from the
    /// <see cref="GroundingSelectionExtensionKey"/> extension written by
    /// <see cref="InjectGroundingSelection"/>. The component id is remapped to the newly-placed
    /// object's InstanceGuid via <see cref="PutResult.IdToGuidMapping"/>. Returns whether any
    /// selection was restored, so the caller can trigger a re-solve.
    /// </summary>
    /// <param name="doc">The placed document (post-Fix, so ids match the Put mapping).</param>
    /// <param name="result">The Put result carrying the id-to-guid mapping and placed objects.</param>
    /// <returns>true when at least one grounding selection was restored.</returns>
    private static bool RestoreGroundingSelection(GhJsonDocument doc, PutResult result)
    {
        bool restored = false;

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Id is not int id
                || component.ComponentState?.Extensions is not { } extensions
                || !extensions.TryGetValue(GroundingSelectionExtensionKey, out object? raw))
            {
                continue;
            }

            GroundingSelectionExtension? ext;
            try
            {
                // The extension round-trips as a Newtonsoft token; re-serialise to read it back typed.
                ext = JsonConvert.DeserializeObject<GroundingSelectionExtension>(JsonConvert.SerializeObject(raw));
            }
            catch (Newtonsoft.Json.JsonException)
            {
                continue;
            }

            if (ext?.Leaves is null
                || !result.IdToGuidMapping.TryGetValue(id, out Guid guid)
                || result.PlacedObjects.FirstOrDefault(o => o.InstanceGuid == guid) is not Recorder recorder)
            {
                continue;
            }

            var leaves = ext.Leaves
                .Where(l => l is not null)
                .Select(l => (l.Category ?? string.Empty, l.SubCategory ?? string.Empty));

            recorder.SetGroundingSelection(GroundingSelection.FromLeaves(leaves));
            restored = true;
        }

        return restored;
    }

    /// <summary>
    /// Removes abbreviated <c>nickName</c> entries from all parameter settings in a document so
    /// that the exported JSON contains only the full <c>parameterName</c> values.
    /// </summary>
    /// <param name="doc">The document to normalise in place.</param>
    private static void StripNickNames(GhJsonDocument doc)
    {
        if (doc.Components is null)
        {
            return;
        }

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.InputSettings is not null)
            {
                foreach (GhJsonParameterSettings s in component.InputSettings)
                {
                    s.NickName = null;
                }
            }

            if (component.OutputSettings is not null)
            {
                foreach (GhJsonParameterSettings s in component.OutputSettings)
                {
                    s.NickName = null;
                }
            }
        }
    }

    /// <summary>
    /// Serialised shape of a Feedback component's linked-collector list, stored under
    /// <see cref="FeedbackCollectorsExtensionKey"/> in the component's state extensions.
    /// </summary>
    private sealed class FeedbackLinkExtension
    {
        /// <summary>
        /// Gets or sets the GhJSON component ids of the linked FeedbackCollectors.
        /// </summary>
        [JsonProperty("collectorIds")]
        public List<int>? CollectorIds { get; set; }
    }

    /// <summary>
    /// Serialised shape of a Recorder's grounding selection, stored under
    /// <see cref="GroundingSelectionExtensionKey"/> in the component's state extensions.
    /// </summary>
    private sealed class GroundingSelectionExtension
    {
        /// <summary>
        /// Gets or sets the included <c>(Category, SubCategory)</c> leaves.
        /// </summary>
        [JsonProperty("leaves")]
        public List<GroundingLeaf>? Leaves { get; set; }
    }

    /// <summary>
    /// One included tab/panel leaf of a <see cref="GroundingSelectionExtension"/>.
    /// </summary>
    private sealed class GroundingLeaf
    {
        /// <summary>
        /// Gets or sets the tab (category) name.
        /// </summary>
        [JsonProperty("category")]
        public string? Category { get; set; }

        /// <summary>
        /// Gets or sets the panel (sub-category) name.
        /// </summary>
        [JsonProperty("subCategory")]
        public string? SubCategory { get; set; }
    }
}
