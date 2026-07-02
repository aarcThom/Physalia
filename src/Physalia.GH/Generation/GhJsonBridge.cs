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
using Physalia.Core.Grounding.Clusters;
using Physalia.Core.Grounding.Components;
using Physalia.GH.Components;
using Physalia.GH.Components.Utility;
using GHClusterObject = Grasshopper.Kernel.Special.GH_Cluster;

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

    // componentState.extensions key marking a component as a Grasshopper cluster reference rather
    // than an installed component. The Resolver stamps it (with the cluster's name) when a generated
    // node name matches a cluster in Files/CLUSTERS; placement then instantiates the cluster from its
    // file instead of asking the GhJSON library to create it by componentGuid. GhJSON treats
    // extensions as an opaque pass-through, so this round-trips untouched.
    private const string ClusterExtensionKey = "physalia.cluster";

    // componentState.extensions key marking a node as a reference to an input already on the canvas
    // (a Rhino-referenced parameter placed by the Rhino Geometry tool) rather than a component to
    // create. The node is lifted out before Put — like a cluster — and the graph's wires that touched
    // it are re-established against the live parameter, so an existing input is reused instead of
    // duplicated. Value is the input's name. GhJSON treats extensions as opaque pass-through.
    private const string ReferenceExtensionKey = "physalia.reference";

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

            // A model-supplied nickName is the canvas label (e.g. a slider named "Radius"); carry it
            // through so the placed component reads clearly. Blank leaves the component's default label.
            if (!string.IsNullOrWhiteSpace(component.NickName))
            {
                ghComponent.NickName = component.NickName;
            }

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
    /// library can create it exactly. An incoming <c>componentGuid</c> is trusted only when it
    /// names a catalogued (installed, non-obsolete) component; a stale or deprecated GUID — the
    /// kind a model reproduces from old files, e.g. the obsolete colour "Multiplication" — is
    /// discarded and the component re-resolved by name. Names that cannot be matched confidently
    /// are returned for feedback rather than guessed.
    /// </summary>
    /// <param name="json">The GhJSON document as a string.</param>
    /// <param name="catalog">The installed-component catalog to resolve against.</param>
    /// <param name="clusterCatalog">The available-cluster catalog; a node whose name exactly matches a
    /// cluster is stamped as a cluster reference (handled by placement) rather than resolved to an
    /// installed component. Pass an empty catalog to disable cluster resolution.</param>
    /// <returns>The resolved GhJSON and the list of names that could not be resolved.</returns>
    internal static (string Json, IReadOnlyList<string> Unresolved) ResolveComponentNames(string json, ComponentCatalog catalog, ClusterCatalog clusterCatalog)
    {
        GhJsonDocument doc = GhJson.FromJson(json);
        var unresolved = new List<string>();

        if (doc.Components is not null)
        {
            foreach (GhJsonComponent component in doc.Components)
            {
                if (component.ComponentGuid is Guid incoming && incoming != Guid.Empty)
                {
                    // Trust an incoming GUID only if it points at a catalogued (installed,
                    // non-obsolete) component. A stale/deprecated GUID — the obsolete colour
                    // "Multiplication" a model reproduces from old files — is dropped so the
                    // name-match below re-resolves it to the current component.
                    if (catalog.ContainsGuid(incoming))
                    {
                        continue;
                    }

                    component.ComponentGuid = null;
                }

                string proposed = component.Name ?? string.Empty;

                // A node whose name exactly matches a cluster is a cluster reference — stamp it and
                // let placement instantiate it from its file. Checked before the fuzzy component match
                // so an exact cluster name is never mis-resolved to a similarly-named component.
                ClusterEntry? cluster = clusterCatalog?.Find(proposed);
                if (cluster is not null)
                {
                    component.Name = cluster.Name;
                    StampClusterReference(component, cluster.Name);
                    continue;
                }

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

    // Marks a component as a cluster reference by name. Placement reads this and loads the cluster
    // from Files/CLUSTERS instead of instantiating an installed component by componentGuid.
    private static void StampClusterReference(GhJsonComponent component, string clusterName)
    {
        component.ComponentState ??= new GhJsonComponentState();
        component.ComponentState.Extensions ??= new Dictionary<string, object>();
        component.ComponentState.Extensions[ClusterExtensionKey] = new ClusterRefExtension { Name = clusterName };
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
        GhJsonDocument doc = GhJson.FromJson(json);
        RelayoutLlmGraph(doc);
        return PlaceDocument(doc, targetOrigin);
    }

    /// <summary>
    /// Recomputes a clean left-to-right hierarchical layout for an LLM-authored GhJSON graph,
    /// overwriting the model's own (typically tight and overlapping) pivots. Nodes are layered by
    /// data-flow depth and stacked vertically within each layer, so sources such as sliders sit in
    /// their own column instead of colliding with the components they feed. Layer 0's top node lands
    /// at the layout origin, so the bounding-box corner PlaceDocument anchors to the transmitter is a
    /// real component. Applied only on the LLM placement path; preset/Deserializer placements (which
    /// carry intended pivots) go through PlaceDocument directly and keep their authored layout.
    /// </summary>
    /// <param name="doc">The parsed GhJSON document to relayout in place.</param>
    private static void RelayoutLlmGraph(GhJsonDocument doc)
    {
        if (doc.Components is null || doc.Components.Count == 0)
        {
            return;
        }

        var nodeIds = new List<int>();
        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Id is int id)
            {
                nodeIds.Add(id);
            }
        }

        if (nodeIds.Count == 0)
        {
            return;
        }

        var edges = new List<(int From, int To)>();
        foreach (GhJsonConnection connection in doc.Connections ?? Enumerable.Empty<GhJsonConnection>())
        {
            if (connection.From?.Id is int from && connection.To?.Id is int to)
            {
                edges.Add((from, to));
            }
        }

        IReadOnlyDictionary<int, PointF> positions = HierarchicalLayout.ComputePositions(nodeIds, edges);

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Id is int id && positions.TryGetValue(id, out PointF p))
            {
                if (component.Pivot is null)
                {
                    component.Pivot = new GhJsonPivot(p.X, p.Y);
                }
                else
                {
                    component.Pivot.X = p.X;
                    component.Pivot.Y = p.Y;
                }
            }
        }
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

        // The offset aligns the content's top-left pivot to the target. Computed from the FULL layout
        // (clusters included) so relative positions hold once clusters are lifted out. On the LLM path
        // the graph has been relaid out so a real component (layer 0's top node) sits at this corner.
        PointF offset = ComputeOffset(doc.Components, targetOrigin);

        // Cluster nodes (stamped by the Resolver) cannot be created by the GhJSON library, so lift
        // them out before Fix/Put: Physalia instantiates each from its file and rewires its wires.
        ClusterPlan? clusterPlan = ExtractClusters(ref doc, offset);

        if (doc.Components is null || doc.Components.Count == 0)
        {
            // Graph is clusters only — there is nothing for the library Put to do; place directly.
            return clusterPlan is null ? EmptyDocumentResult() : PlaceClustersOnly(clusterPlan);
        }

        // Reference nodes: inputs already on the canvas (Rhino Geometry tool params) the model asked to
        // wire into rather than recreate. Lift them out before Put — like clusters — and splice the
        // graph onto the live parameters afterward. Unresolved references (a name no longer on the
        // canvas) are surfaced so the model can correct.
        var referenceIssues = new List<string>();
        ReferencePlan? referencePlan = ExtractReferences(
            ref doc,
            RhinoGeometryTool.CollectReferenceableInputs(Grasshopper.Instances.ActiveCanvas?.Document),
            referenceIssues);

        if (doc.Components is null || doc.Components.Count == 0)
        {
            // Nothing left to place (graph was only references / clusters). Clusters, if any, still place.
            return clusterPlan is null ? EmptyDocumentResult() : PlaceClustersOnly(clusterPlan);
        }

        // Stamp a validated, non-obsolete component GUID on every node before Put. The GhJSON library
        // creates GUID-first and falls back to an UNFILTERED name lookup (CreateByName returns the first
        // proxy matching a name, obsolete or not — e.g. the colour "Multiplication" twin). Stamping here
        // makes placement self-sufficient for pipelines with no Resolver and guarantees creation goes
        // through the correct GUID rather than that fallback.
        StampComponentGuids(doc);

        // Normalise AI-authored JSON (missing ids, stray fields, near-miss structure) before placing.
        // The GhJSON library's fixer is the single source of repair; anything it cannot fix is surfaced
        // to the caller so it can be routed back to the model as feedback.
        var fixResult = GhJson.Fix(doc);
        doc = fixResult.Document;
        IReadOnlyList<string> unfixedIssues = (fixResult.UnfixedIssues ?? Enumerable.Empty<string>())
            .Concat(referenceIssues)
            .ToList();

        var options = BuildPutOptions(offset);
        return ExecutePut(doc, options, unfixedIssues, clusterPlan: clusterPlan, referencePlan: referencePlan);
    }

    // Ensures every component carries a validated, non-obsolete component-type GUID before Put, so the
    // GhJSON library instantiates it via CreateByGuid rather than its unfiltered CreateByName fallback
    // (which returns the FIRST proxy matching a name — obsolete twins included, e.g. colour
    // "Multiplication"). A node already carrying a live, non-obsolete GUID is left untouched, so user
    // and preset graphs are unaffected; a missing or obsolete GUID is re-resolved by name against the
    // obsolete-free catalog. Cluster nodes are skipped (they are lifted out and placed separately).
    private static void StampComponentGuids(GhJsonDocument doc)
    {
        if (doc.Components is null || doc.Components.Count == 0)
        {
            return;
        }

        ComponentCatalog catalog = ComponentCatalogProvider.BuildFromServer();
        if (catalog.Count == 0)
        {
            return;
        }

        GH_ComponentServer server = Grasshopper.Instances.ComponentServer;
        foreach (GhJsonComponent component in doc.Components)
        {
            if (IsClusterNode(component))
            {
                continue;
            }

            // Keep an incoming GUID only when it names a live, non-obsolete component.
            if (component.ComponentGuid is Guid guid && guid != Guid.Empty)
            {
                IGH_ObjectProxy proxy = server.EmitObjectProxy(guid);
                if (proxy is not null && !proxy.Obsolete)
                {
                    continue;
                }
            }

            ComponentMatcher.MatchResult match = ComponentMatcher.Match(component.Name ?? string.Empty, catalog);
            if (match.IsConfident && match.Entry is not null)
            {
                component.Name = match.Entry.Name;
                component.ComponentGuid = match.Entry.ComponentGuid;
            }
        }
    }

    // The canvas offset that maps the content's top-left pivot onto targetOrigin. Components with no
    // pivot are ignored; an all-pivotless set yields a zero origin (offset == targetOrigin).
    private static PointF ComputeOffset(IReadOnlyList<GhJsonComponent> components, PointF targetOrigin)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        foreach (GhJsonComponent component in components)
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

        return new PointF(targetOrigin.X - minX, targetOrigin.Y - minY);
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

    // Instance GUIDs (post-placement, after RegenerateInstanceGuids) of placed objects whose source
    // document carried an explicit nickName. These are labels a producer set on purpose (most often a
    // Number Slider named for what it drives), so nickname-display expansion must leave them intact.
    private static ISet<Guid> ExplicitlyNickNamedGuids(GhJsonDocument doc, PutResult result)
    {
        var guids = new HashSet<Guid>();
        if (doc.Components is null)
        {
            return guids;
        }

        foreach (GhJsonComponent component in doc.Components)
        {
            if (string.IsNullOrWhiteSpace(component.NickName))
            {
                continue;
            }

            if (component.Id is int id && result.IdToGuidMapping.TryGetValue(id, out Guid guid))
            {
                guids.Add(guid);
            }
        }

        return guids;
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
    // nickname display, restores wireless feedback links, runs the optional post-placement step
    // (used to splice an anchor component into a placeholder slot), and places/rewires any clusters
    // lifted out before Put. Shapes the PlaceResult, folding cluster guids into the placed set.
    private static PlaceResult ExecutePut(GhJsonDocument doc, PutOptions options, IReadOnlyList<string> unfixedIssues, Action<PutResult>? afterPlace = null, ClusterPlan? clusterPlan = null, ReferencePlan? referencePlan = null)
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

        var clusterGuids = new List<Guid>();
        var clusterWarnings = new List<string>();

        if (result.Success)
        {
            ComponentHelpers.ApplyNickNameDisplay(result.PlacedObjects, ExplicitlyNickNamedGuids(doc, result));

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

            // Place clusters lifted out before Put and rewire their connections to the placed graph.
            GH_Document? hostDoc = result.PlacedObjects.FirstOrDefault()?.OnPingDocument()
                ?? Grasshopper.Instances.ActiveCanvas?.Document;
            bool clustersPlaced = clusterPlan is not null
                && PlaceClusters(clusterPlan, hostDoc, result, clusterGuids, clusterWarnings);

            // Splice the placed graph onto the live canvas inputs the model referenced (lifted out
            // before Put), reusing them instead of the duplicates the model would otherwise author.
            bool referencesWired = referencePlan is not null && RewireReferences(referencePlan, result);

            // Params and wires were changed after Put's own solution, so re-solve the now-expired
            // components (and their downstream) to bring recreated variable params and restored
            // Picker/grounding selections (and placed clusters / referenced inputs) live.
            if (reconciled.Count > 0 || pickersRestored || groundingRestored || clustersPlaced || referencesWired)
            {
                (result.PlacedObjects.FirstOrDefault()?.OnPingDocument() ?? hostDoc)?.NewSolution(false);
            }
        }

        var placedGuids = result.PlacedObjects.Select(o => o.InstanceGuid).Concat(clusterGuids).ToList();
        var warnings = result.Warnings.Concat(clusterWarnings).ToList();

        return result.Success
            ? new PlaceResult(true, result.ComponentsPlaced + clusterGuids.Count, result.ConnectionsCreated, warnings.Count, null, placedGuids, warnings, unfixedIssues)
            : new PlaceResult(false, 0, 0, 0, result.ErrorMessage, Array.Empty<Guid>(), Array.Empty<string>(), unfixedIssues);
    }

    // True when a component was stamped as a cluster reference by the Resolver (see StampClusterReference).
    private static bool IsClusterNode(GhJsonComponent component) =>
        component.ComponentState?.Extensions is { } ext && ext.ContainsKey(ClusterExtensionKey);

    // Reads the cluster name from a cluster-reference node's extension, or null when absent/malformed.
    private static string? ReadClusterName(GhJsonComponent component)
    {
        if (component.ComponentState?.Extensions is not { } ext
            || !ext.TryGetValue(ClusterExtensionKey, out object? raw))
        {
            return null;
        }

        try
        {
            // The extension round-trips as a Newtonsoft token; re-serialise to read it back typed.
            return JsonConvert.DeserializeObject<ClusterRefExtension>(JsonConvert.SerializeObject(raw))?.Name;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    // Reads the referenced input name from a node stamped with physalia.reference, or null when absent/malformed.
    private static string? ReadReferenceName(GhJsonComponent component)
    {
        if (component.ComponentState?.Extensions is not { } ext
            || !ext.TryGetValue(ReferenceExtensionKey, out object? raw))
        {
            return null;
        }

        try
        {
            // The extension round-trips as a Newtonsoft token; re-serialise to read it back typed.
            return JsonConvert.DeserializeObject<ReferenceRefExtension>(JsonConvert.SerializeObject(raw))?.Name;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    // Lifts reference nodes — existing canvas inputs the model asked to reuse — out of the document so
    // the GhJSON library never creates them, returning a plan that maps each reference node's id to its
    // live parameter plus the connections that touched it (rewired against the placed graph after Put),
    // and rewriting <paramref name="doc"/> in place to keep only the remaining components and wires.
    //
    // A node is a reference when it carries the physalia.reference extension (the explicit contract), OR
    // — the forgiving path — when its nickName matches a live input name. An explicit reference whose
    // name is not (or no longer) a live input is reported in <paramref name="unresolved"/> and dropped so
    // the model can correct. Returns null (leaving the document untouched) when there are no references.
    private static ReferencePlan? ExtractReferences(ref GhJsonDocument doc, IReadOnlyList<ReferenceableInput> inputs, List<string> unresolved)
    {
        if (doc.Components is null || doc.Components.Count == 0)
        {
            return null;
        }

        var byName = new Dictionary<string, IGH_Param>(StringComparer.OrdinalIgnoreCase);
        foreach (ReferenceableInput input in inputs)
        {
            if (!string.IsNullOrWhiteSpace(input.Name))
            {
                byName[input.Name] = input.LiveOutput;
            }
        }

        var liveById = new Dictionary<int, IGH_Param>();
        var droppedIds = new HashSet<int>();
        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Id is not int id)
            {
                continue;
            }

            string? explicitName = ReadReferenceName(component);
            if (explicitName is not null)
            {
                if (byName.TryGetValue(explicitName, out IGH_Param? live))
                {
                    liveById[id] = live;
                }
                else
                {
                    unresolved.Add(UnresolvedReferenceMessage(explicitName, byName.Keys));
                    droppedIds.Add(id);
                }
            }
            else if (!string.IsNullOrWhiteSpace(component.NickName) && byName.TryGetValue(component.NickName, out IGH_Param? implied))
            {
                liveById[id] = implied;
            }
        }

        if (liveById.Count == 0 && droppedIds.Count == 0)
        {
            return null;
        }

        var refIds = new HashSet<int>(liveById.Keys);
        refIds.UnionWith(droppedIds);

        var refConnections = new List<GhJsonConnection>();
        var keptConnections = new List<GhJsonConnection>();
        foreach (GhJsonConnection conn in doc.Connections ?? Enumerable.Empty<GhJsonConnection>())
        {
            bool touchesResolved = (conn.From is { } rf && liveById.ContainsKey(rf.Id))
                || (conn.To is { } rt && liveById.ContainsKey(rt.Id));
            bool touchesDropped = (conn.From is { } df && droppedIds.Contains(df.Id))
                || (conn.To is { } dt && droppedIds.Contains(dt.Id));

            if (touchesResolved && !touchesDropped)
            {
                refConnections.Add(conn);
            }
            else if (!touchesResolved && !touchesDropped)
            {
                keptConnections.Add(conn);
            }

            // A wire touching a dropped (unresolved) reference is discarded — there is nothing to wire to.
        }

        var keptComponents = doc.Components
            .Where(c => c.Id is not int cid || !refIds.Contains(cid))
            .ToList();
        doc = new GhJsonDocument(doc.Schema, doc.Metadata, keptComponents, keptConnections, doc.Groups);

        return new ReferencePlan(liveById, refConnections);
    }

    // Formats the model-facing message for a reference whose name is not (or no longer) a live input.
    private static string UnresolvedReferenceMessage(string name, IEnumerable<string> available)
    {
        var names = available.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        string list = names.Count > 0 ? string.Join(", ", names) : "(none)";
        return $"Referenced canvas input '{name}' was not found. Available inputs: {list}.";
    }

    // Lifts cluster nodes out of the document so the GhJSON library never sees them: returns a plan
    // (each cluster's resolved file, pivot, and the connections that touched it) and rewrites
    // <paramref name="doc"/> in place to keep only the non-cluster components and the wires among them.
    // Returns null (and leaves the document untouched) when there are no cluster nodes.
    //
    // A node is a cluster when the Resolver stamped it (physalia.cluster), OR — for pipelines with no
    // Resolver — when it carries no real component guid and its name matches a cluster on disk. The
    // latter makes placement self-sufficient: without it, an unstamped cluster node reaches Put, which
    // cannot create it and reports its wires as failures.
    private static ClusterPlan? ExtractClusters(ref GhJsonDocument doc, PointF offset)
    {
        if (doc.Components is null)
        {
            return null;
        }

        ClusterCatalog catalog = ClusterCatalogProvider.GetCatalog();

        ClusterEntry? EntryFor(GhJsonComponent c)
        {
            bool hasRealGuid = c.ComponentGuid is Guid g && g != Guid.Empty;
            if (hasRealGuid && !IsClusterNode(c))
            {
                return null; // a resolved installed component — never hijack it as a cluster
            }

            string name = ReadClusterName(c) ?? c.Name ?? string.Empty;
            return catalog.Find(name);
        }

        var clusterComponents = doc.Components.Where(c => EntryFor(c) is not null).ToList();
        if (clusterComponents.Count == 0)
        {
            return null;
        }

        var clusterIds = new HashSet<int>(clusterComponents
            .Where(c => c.Id is int)
            .Select(c => c.Id!.Value));

        var placements = new List<ClusterPlacement>();
        foreach (GhJsonComponent component in clusterComponents)
        {
            ClusterEntry entry = EntryFor(component)!;
            PointF pivot = component.Pivot is not null
                ? new PointF((float)component.Pivot.X, (float)component.Pivot.Y)
                : PointF.Empty;
            placements.Add(new ClusterPlacement(component.Id, entry.Name, entry.FilePath, pivot));
        }

        var clusterConnections = new List<GhJsonConnection>();
        var keptConnections = new List<GhJsonConnection>();
        foreach (GhJsonConnection conn in doc.Connections ?? Enumerable.Empty<GhJsonConnection>())
        {
            bool touchesCluster = (conn.From is { } f && clusterIds.Contains(f.Id))
                || (conn.To is { } t && clusterIds.Contains(t.Id));
            (touchesCluster ? clusterConnections : keptConnections).Add(conn);
        }

        var keptComponents = doc.Components.Where(c => EntryFor(c) is null).ToList();
        doc = new GhJsonDocument(doc.Schema, doc.Metadata, keptComponents, keptConnections, doc.Groups);

        return new ClusterPlan(placements, clusterConnections, offset);
    }

    // Places each planned cluster into the host document and rewires the connections that touched a
    // cluster, against both the library-placed components and the freshly-added clusters. Cluster guids
    // and any per-cluster failures are accumulated into the caller's lists. Returns whether any cluster
    // was added (so the caller can trigger a re-solve).
    private static bool PlaceClusters(ClusterPlan plan, GH_Document? host, PutResult result, List<Guid> placedGuids, List<string> warnings)
    {
        if (plan.Placements.Count == 0)
        {
            return false;
        }

        if (host is null)
        {
            warnings.Add("Clusters could not be placed: no active Grasshopper document.");
            return false;
        }

        // GhJSON id -> placed object, covering both library-placed components and the clusters we add.
        var byId = new Dictionary<int, IGH_DocumentObject>();
        foreach (KeyValuePair<int, Guid> pair in result.IdToGuidMapping)
        {
            IGH_DocumentObject? obj = result.PlacedObjects.FirstOrDefault(o => o.InstanceGuid == pair.Value);
            if (obj is not null)
            {
                byId[pair.Key] = obj;
            }
        }

        bool any = AddClusters(plan, host, byId, placedGuids, warnings);
        RewireClusterConnections(plan.Connections, byId, ClusterIds(plan));
        return any;
    }

    // Placement path for a graph that is clusters only (the GhJSON library Put had nothing to do).
    private static PlaceResult PlaceClustersOnly(ClusterPlan plan)
    {
        GH_Document? host = Grasshopper.Instances.ActiveCanvas?.Document;
        if (host is null)
        {
            return new PlaceResult(false, 0, 0, 0, "No active Grasshopper document to place clusters into.", Array.Empty<Guid>(), Array.Empty<string>(), Array.Empty<string>());
        }

        var placedGuids = new List<Guid>();
        var warnings = new List<string>();
        var byId = new Dictionary<int, IGH_DocumentObject>();

        AddClusters(plan, host, byId, placedGuids, warnings);
        RewireClusterConnections(plan.Connections, byId, ClusterIds(plan));

        if (placedGuids.Count > 0)
        {
            host.NewSolution(false);
        }

        return new PlaceResult(true, placedGuids.Count, 0, warnings.Count, null, placedGuids, warnings, Array.Empty<string>());
    }

    // Adds each planned cluster to the document at its offset-adjusted pivot, recording the placed guid
    // and id->object mapping. A missing file or load failure is reported as a warning and skipped.
    private static bool AddClusters(ClusterPlan plan, GH_Document host, IDictionary<int, IGH_DocumentObject> byId, List<Guid> placedGuids, List<string> warnings)
    {
        bool any = false;
        foreach (ClusterPlacement placement in plan.Placements)
        {
            if (string.IsNullOrEmpty(placement.FilePath) || !System.IO.File.Exists(placement.FilePath))
            {
                warnings.Add($"Cluster '{placement.Name}' could not be placed: its file is not present in Files/CLUSTERS.");
                continue;
            }

            GHClusterObject? cluster = TryLoadCluster(placement.FilePath!);
            if (cluster is null)
            {
                warnings.Add($"Cluster '{placement.Name}' could not be loaded.");
                continue;
            }

            host.AddObject(cluster, false);
            if (cluster.Attributes is null)
            {
                cluster.CreateAttributes();
            }

            if (cluster.Attributes is not null)
            {
                cluster.Attributes.Pivot = new PointF(placement.Pivot.X + plan.Offset.X, placement.Pivot.Y + plan.Offset.Y);
                cluster.Attributes.Selected = false;
                cluster.Attributes.ExpireLayout();
            }

            placedGuids.Add(cluster.InstanceGuid);
            if (placement.Id is int id)
            {
                byId[id] = cluster;
            }

            any = true;
        }

        return any;
    }

    // Rewires the connections that touched a cluster against the combined map of library-placed
    // components and freshly-added clusters. The cluster-side endpoint is resolved by paramIndex first
    // (a cluster routinely exposes several inputs/outputs sharing one name — e.g. two "Curve" inputs —
    // which a name-only lookup cannot tell apart); the other endpoint stays name-first. Endpoints that
    // do not resolve are skipped.
    private static void RewireClusterConnections(IReadOnlyList<GhJsonConnection> connections, IReadOnlyDictionary<int, IGH_DocumentObject> byId, IReadOnlyCollection<int> clusterIds)
    {
        foreach (GhJsonConnection conn in connections)
        {
            if (conn.From is not { } from || conn.To is not { } to
                || !byId.TryGetValue(from.Id, out IGH_DocumentObject? fromObj)
                || !byId.TryGetValue(to.Id, out IGH_DocumentObject? toObj))
            {
                continue;
            }

            IGH_Param? source = FindEndpointParam(fromObj, from, output: true, preferIndex: clusterIds.Contains(from.Id));
            IGH_Param? sink = FindEndpointParam(toObj, to, output: false, preferIndex: clusterIds.Contains(to.Id));
            if (source is null || sink is null || sink.Sources.Contains(source))
            {
                continue;
            }

            sink.AddSource(source);
        }
    }

    // Splices the placed graph onto the live parameters the reference nodes stood in for, WITHOUT moving
    // them — the input was placed next to the transmitter's arrow tip when it was created and stays put.
    // Reuses the cluster rewiring: a combined id->object map (library-placed objects plus each reference
    // id mapped to its live parameter) is resolved by the same endpoint logic — the floating live param
    // resolves to itself, the graph side by name then paramIndex. Returns whether any wire was processed.
    private static bool RewireReferences(ReferencePlan plan, PutResult result)
    {
        if (plan.Connections.Count == 0)
        {
            return false;
        }

        var byId = new Dictionary<int, IGH_DocumentObject>();
        foreach (KeyValuePair<int, Guid> pair in result.IdToGuidMapping)
        {
            IGH_DocumentObject? obj = result.PlacedObjects.FirstOrDefault(o => o.InstanceGuid == pair.Value);
            if (obj is not null)
            {
                byId[pair.Key] = obj;
            }
        }

        // Reference ids resolve to their live floating parameter (which is its own output source).
        foreach (KeyValuePair<int, IGH_Param> pair in plan.LiveById)
        {
            byId[pair.Key] = pair.Value;
        }

        RewireClusterConnections(plan.Connections, byId, new HashSet<int>(plan.LiveById.Keys));
        return true;
    }

    // Resolves a connection endpoint to a live parameter. When preferIndex is set (the cluster side),
    // paramIndex wins so same-named ports are addressable; otherwise the full Name is matched, with
    // paramIndex as a fallback. Floating params resolve to the object itself.
    private static IGH_Param? FindEndpointParam(IGH_DocumentObject obj, GhJsonConnectionEndpoint endpoint, bool output, bool preferIndex)
    {
        if (obj is not IGH_Component component)
        {
            return obj as IGH_Param;
        }

        IList<IGH_Param> list = output ? component.Params.Output : component.Params.Input;
        bool IndexInRange(out int i)
        {
            i = endpoint.ParamIndex ?? -1;
            return i >= 0 && i < list.Count;
        }

        if (preferIndex && IndexInRange(out int idx))
        {
            return list[idx];
        }

        IGH_Param? byName = list.FirstOrDefault(p => p.Name == endpoint.ParamName);
        if (byName is not null)
        {
            return byName;
        }

        // A cluster's port name in the grounding (and thus the model's wire) is the hook nickname,
        // which is the param's NickName here, not its Name — match that before giving up.
        if (preferIndex)
        {
            IGH_Param? byNickName = list.FirstOrDefault(p => p.NickName == endpoint.ParamName);
            if (byNickName is not null)
            {
                return byNickName;
            }
        }

        return IndexInRange(out int fallback) ? list[fallback] : null;
    }

    // The GhJSON ids of the planned clusters — the endpoints that must resolve by paramIndex.
    private static HashSet<int> ClusterIds(ClusterPlan plan) =>
        new HashSet<int>(plan.Placements.Where(p => p.Id is int).Select(p => p.Id!.Value));

    private static GHClusterObject? TryLoadCluster(string path)
    {
        try
        {
            var cluster = new GHClusterObject();
            cluster.CreateFromFilePath(path);
            return cluster;
        }
        catch
        {
            return null;
        }
    }

    // A planned cluster placement: its GhJSON id (for rewiring), display name, resolved file path
    // (null when the named cluster is no longer in Files/CLUSTERS), and its file-space pivot.
    private readonly record struct ClusterPlacement(int? Id, string Name, string? FilePath, PointF Pivot);

    // The clusters lifted out of a document before Put, the connections that touched them, and the
    // canvas offset to apply (the same offset Put applies to the non-cluster components).
    private sealed record ClusterPlan(
        IReadOnlyList<ClusterPlacement> Placements,
        IReadOnlyList<GhJsonConnection> Connections,
        PointF Offset);

    // Serialised shape of a cluster reference, stored under ClusterExtensionKey in a node's extensions.
    private sealed class ClusterRefExtension
    {
        /// <summary>
        /// Gets or sets the cluster's name (resolved to a file under Files/CLUSTERS at placement time).
        /// </summary>
        [JsonProperty("name")]
        public string? Name { get; set; }
    }

    // A reference plan: each reference node's GhJSON id mapped to the live parameter it stands for, and
    // the connections that touched a reference node (rewired against the placed graph after Put).
    private sealed record ReferencePlan(
        IReadOnlyDictionary<int, IGH_Param> LiveById,
        IReadOnlyList<GhJsonConnection> Connections);

    // Serialised shape of a canvas-input reference, stored under ReferenceExtensionKey in a node's extensions.
    private sealed class ReferenceRefExtension
    {
        /// <summary>
        /// Gets or sets the referenced input's name (matched to a live canvas parameter at placement time).
        /// </summary>
        [JsonProperty("name")]
        public string? Name { get; set; }
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
