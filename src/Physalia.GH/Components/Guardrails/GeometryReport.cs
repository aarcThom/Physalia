// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.Planning;
using Physalia.Core.Signals;
using Physalia.GH.Generation;
using Rhino;
using Rhino.Geometry;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// On receiving a signal, measures the geometry the watched graph currently produces and
/// emits a text digest: per-component bounding boxes, types, counts and closedness, the
/// whole-model bounding box, disjoint-cluster detection with gap distances, and neutral
/// containment facts. This is the deterministic, text-only stand-in for visual feedback: a graph
/// that solves cleanly can still be semantically wrong — wings floating away from the mass they
/// should touch, columns buried inside a solid — and nothing errors, so no feedback loop fires.
/// The report turns those spatial facts into text the model can weigh against its intent.
///
/// <para>Scoping matches the Runtime Health Check, not the per-turn Geometry Observation: GUIDs
/// arriving on consumed signals ACCUMULATE (a patch turn's payload is only the delta, but spatial
/// correctness — do the parts meet? — is a whole-model property), pruned when components are
/// removed, session-only. When nothing is watched and the payload carries no GUIDs the whole
/// document is measured, preserving use as a standalone probe.</para>
///
/// <para>The result is a success signal on the single Signal output, payload = the report text,
/// routable forward to a Conversation Log or back through the feedback path. The report
/// instructs the model: geometry matches intent → reply in prose (the Detect JSON gate parks the
/// loop); mismatch → corrective ghpatch (the report carries a fresh base checksum for exactly
/// that turn). Wire it after the Runtime Health Check's Success Signal so it only measures
/// healthy graphs.</para>
///
/// <para>That instruction is right for a definition generated in one shot and wrong for one built
/// in stages, where a correct first slice measures exactly as clean as a finished definition and
/// the offer of prose ends the build early. So when the Message input carries a Build Plan
/// tracker's progress digest, the digest's staged instruction replaces it — continue while stages
/// remain, prose only on the last one — and rides at the top of the report, ahead of the
/// measurements it is asking the model to judge. Detection is by the digest's own marker, so no
/// mode has to be set anywhere: wire the tracker in and the report adapts; leave it out and the
/// single-shot wording stands.</para>
/// </summary>
public class GeometryReport : RoutingComponentBase<string>
{
    private const int MessageInputIndex = 0;

    // Report caps: the digest rides a prompt, so its size is bounded regardless of graph size.
    // Every overflow is labelled, so a truncated report never reads as a complete one.
    private const int MaxGeometryLines = 40;
    private const int MaxContainmentLines = 8;
    private const int MaxClusterMembers = 12;
    private const int MaxReportChars = 8000;

    // Two component boxes closer than this fraction of the world diagonal count as touching for
    // cluster detection (floored by the model absolute tolerance).
    private const double ClusterTouchFraction = 0.005;

    // Every component GUID ever received on a consumed signal, minus those since removed from
    // the document (pruned at scan time). Session-only; never serialized.
    private readonly HashSet<Guid> _watchedGuids = new();

    // Last report's per-output measurement lines, keyed by instanceGuid#port. The whole
    // point of the delta: a line identical to the one already sent is named as unchanged
    // instead of re-rendered. Session-only, like every other lifecycle state here — a
    // reopened document reports everything in full once, then deltas from there.
    private Dictionary<string, string> _previousLines = new(StringComparer.Ordinal);

    // Applied-op confirmations from THIS turn's consumed signals (the Component Transmitter
    // appends them to its Success payload after the GUIDs). Folded into the next report and
    // cleared — the confirmation is about the patch that just ran, not history.
    private readonly List<string> _pendingAppliedOps = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryReport"/> class.
    /// </summary>
    public GeometryReport()
        : base("Geometry Report", "Geometry Report", "Measures the geometry the watched graph produces — per-component bounding boxes, counts, closedness, disjoint groups, containments — and emits the digest as text on its single Signal output, so the model can compare realization against intent without images.", "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("8C2E5A17-4B9D-4E63-A0F8-D51B7C39E624");

    /// <inheritdoc/>
    /// <remarks>
    /// A single Signal output: the report routed forward and the report routed back were the
    /// same signal, so the separate Fail output only ever duplicated it. A genuine failure
    /// (no document to measure) rides the same wire, keeping the loop alive.
    /// </remarks>
    protected override bool HasFailOutput => false;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Message",
            "M",
            "Optional operator note folded into the report (e.g. what the user asked for), so the model weighs the measured facts against that framing. A Build Plan tracker's Progress digest wired here also switches the report's closing instruction to its staged form.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[MessageInputIndex].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Always runs: a blank payload with an empty watch list still yields a useful whole-document
    /// report, and a remove-only patch legitimately carries no GUIDs.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload ?? string.Empty;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Accumulates the incoming GUIDs into the watch list, so the report covers the whole
    /// LLM-built graph rather than just this turn's delta.
    /// </remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        foreach (Guid guid in ParseGuids(data))
        {
            _watchedGuids.Add(guid);
        }

        foreach (string op in ParseAppliedOps(data))
        {
            _pendingAppliedOps.Add(op);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Defers the measurement until every scoped component has finished solving, so the geometry
    /// read in <see cref="ReadSolve"/> reflects the settled graph rather than a still-expired one.
    /// </remarks>
    protected override bool IsReadReady(string data)
    {
        GH_Document? doc = PhyDocuments.Host(this);
        if (doc is null)
        {
            return true;
        }

        foreach (IGH_DocumentObject obj in ScanScope(doc).Objects)
        {
            // A locked component never solves, so waiting on it would jam the settle gate.
            if (obj is IGH_ActiveObject { Locked: false } ao &&
                (ao.Phase == GH_SolutionPhase.Blank || ao.Phase == GH_SolutionPhase.Computing))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        GH_Document? doc = PhyDocuments.Host(this);
        if (doc is null)
        {
            return RoutingResult.Fail(
                "The live Grasshopper document is unavailable, so no geometry report could be produced.",
                "No document to measure.",
                GH_RuntimeMessageLevel.Error);
        }

        string message = string.Empty;
        da.GetData(MessageInputIndex, ref message);

        (IReadOnlyList<IGH_DocumentObject> scope, _) = ScanScope(doc);
        List<ComponentGeometry> items = HarvestGeometry(scope);

        // The report is the turn where the model may emit a corrective patch, and the placement
        // that triggered this report changed the canvas — carry the fresh checksum so that patch
        // cannot mismatch. IsReadReady settled the graph, so the export is stable here.
        string? checksum = GhJsonBridge.CurrentBaseChecksum(doc, PhyDocuments.Harness(this));
        var currentLines = new Dictionary<string, string>(StringComparer.Ordinal);
        string report = BuildReport(
            message ?? string.Empty, items, UnitsLabel(), checksum, _pendingAppliedOps, _previousLines, currentLines);
        _pendingAppliedOps.Clear();
        _previousLines = currentLines;

        return RoutingResult.Ok(report, message: $"Measured {items.Count} geometry-producing component(s).", level: GH_RuntimeMessageLevel.Remark);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _watchedGuids.Clear();
        _pendingAppliedOps.Clear();
        _previousLines.Clear();
    }

    // ---- Scope (mirrors Runtime Health Check; per-component code by convention) ----------------

    /// <summary>
    /// The objects to measure: every watched component still alive on the document (the
    /// accumulated LLM-built graph), or every object on the document (except this component)
    /// when nothing is watched — the standalone-probe fallback.
    /// </summary>
    /// <param name="doc">The active document.</param>
    /// <returns>The objects in scope, and whether the scan is scoped to the watched graph.</returns>
    private (IReadOnlyList<IGH_DocumentObject> Objects, bool Scoped) ScanScope(GH_Document doc)
    {
        List<IGH_DocumentObject> watched = ResolveWatchedObjects(doc);
        return _watchedGuids.Count > 0
            ? (watched, true)
            : (doc.Objects.Where(o => o.InstanceGuid != InstanceGuid).ToList(), false);
    }

    /// <summary>
    /// Resolves the watch list against the live document, pruning GUIDs whose components have
    /// been removed (by a patch, an undo, or the user) so the list tracks the graph as it exists
    /// now. Locked components stay watched but are excluded — they never solve, so their volatile
    /// geometry is stale.
    /// <para>The authored-placement ledger is folded in first, because signal payloads alone leave
    /// permanent holes: this component sits downstream of the Runtime Health Check's SUCCESS output,
    /// so a turn whose graph was unhealthy never delivers its GUIDs here and they are never
    /// recovered. That is what made the 2026-07-25 23:19 session report "no geometry-producing
    /// components" for a model full of boxes — the 48-component placement failed the health check,
    /// so the only GUIDs ever accumulated were the next patch's delta (two number components). The
    /// ledger records everything the model has placed regardless of which guardrails ran.</para>
    /// </summary>
    /// <param name="doc">The active document.</param>
    /// <returns>The live watched objects.</returns>
    private List<IGH_DocumentObject> ResolveWatchedObjects(GH_Document doc)
    {
        foreach (Guid guid in GhJsonBridge.ModelPlacedGuids(doc))
        {
            _watchedGuids.Add(guid);
        }

        var resolved = new List<IGH_DocumentObject>();
        List<Guid>? stale = null;

        foreach (Guid guid in _watchedGuids)
        {
            if (doc.FindObject(guid, false) is IGH_DocumentObject obj)
            {
                if (obj is not IGH_ActiveObject { Locked: true })
                {
                    resolved.Add(obj);
                }
            }
            else
            {
                (stale ??= new List<Guid>()).Add(guid);
            }
        }

        if (stale is not null)
        {
            foreach (Guid guid in stale)
            {
                _watchedGuids.Remove(guid);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Parses newline-separated GUIDs from the payload, skipping any line that is not a GUID.
    /// </summary>
    /// <param name="payload">The incoming signal payload.</param>
    /// <returns>The GUIDs found in the payload.</returns>
    private static IEnumerable<Guid> ParseGuids(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        foreach (string line in payload.Split('\n'))
        {
            if (Guid.TryParse(line.Trim(), out Guid guid))
            {
                yield return guid;
            }
        }
    }

    /// <summary>
    /// Parses the per-operation patch confirmations the Component Transmitter appends to its
    /// Success payload (see <see cref="GhJsonBridge.AppliedOpLinePrefix"/>).
    /// </summary>
    /// <param name="payload">The incoming signal payload.</param>
    /// <returns>The applied-op descriptions, prefix stripped.</returns>
    private static IEnumerable<string> ParseAppliedOps(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        foreach (string line in payload.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(GhJsonBridge.AppliedOpLinePrefix, StringComparison.Ordinal))
            {
                yield return trimmed[GhJsonBridge.AppliedOpLinePrefix.Length..];
            }
        }
    }

    // ---- Harvest -------------------------------------------------------------------------------

    // One geometry-bearing output: its kind tallies, item/null counts, the union bbox of its
    // items, and the raw data-tree shape (per-branch item counts, geometry or not). The tree
    // shape is what makes cross-product bugs visible: "169 breps" reads plausible until the
    // report says they sit in 13 branches of 13. A component is reported iff it has at least
    // one of these.
    private sealed record OutputGeometry(string PortName, int ItemCount, int NullCount, IReadOnlyList<(string Kind, int Count)> Kinds, BoundingBox Union, IReadOnlyList<int> BranchCounts);

    private sealed record ComponentGeometry(IGH_DocumentObject Owner, IReadOnlyList<OutputGeometry> Outputs, BoundingBox Union, string? InputModifiers);

    private static List<ComponentGeometry> HarvestGeometry(IEnumerable<IGH_DocumentObject> scope)
    {
        var items = new List<ComponentGeometry>();
        foreach (IGH_DocumentObject obj in scope)
        {
            // Floating params (a placed Point or Geometry parameter) hold geometry too — treat
            // the param itself as the single output.
            IEnumerable<IGH_Param> ports = obj switch
            {
                IGH_Component component => component.Params.Output,
                IGH_Param param => new[] { param },
                _ => Enumerable.Empty<IGH_Param>(),
            };

            var outputs = new List<OutputGeometry>();
            foreach (IGH_Param port in ports)
            {
                if (TryDescribeOutput(port) is { } described)
                {
                    outputs.Add(described);
                }
            }

            if (outputs.Count == 0)
            {
                continue;
            }

            BoundingBox union = BoundingBox.Empty;
            foreach (OutputGeometry output in outputs)
            {
                union.Union(output.Union);
            }

            items.Add(new ComponentGeometry(obj, outputs, union, DescribeInputModifiers(obj)));
        }

        return items;
    }

    // Describes one output when it holds placed geometry; null for data-only or empty ports.
    private static OutputGeometry? TryDescribeOutput(IGH_Param port)
    {
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        BoundingBox union = BoundingBox.Empty;
        int geometryItems = 0;
        int nulls = 0;

        foreach (IGH_Goo? goo in port.VolatileData.AllData(false))
        {
            if (goo is null)
            {
                nulls++;
                continue;
            }

            // One goo can yield several items: a Python Script output often arrives as a single
            // opaque wrapper around a whole collection. Counting the contents means ItemCount can
            // exceed the tree's item total, and that mismatch is a finding rather than a defect in
            // the report — "12x closed brep, tree 1 branch × 1 item" is precisely how a collection
            // crammed into one goo looks. Do not "fix" it by counting wrappers instead.
            foreach ((string kind, BoundingBox box) in ClassifyGoo(goo))
            {
                geometryItems++;
                kinds[kind] = kinds.TryGetValue(kind, out int n) ? n + 1 : 1;
                union.Union(box);
            }
        }

        if (geometryItems == 0)
        {
            return null;
        }

        var branchCounts = new List<int>(port.VolatileData.PathCount);
        for (int i = 0; i < port.VolatileData.PathCount; i++)
        {
            branchCounts.Add(port.VolatileData.get_Branch(i)?.Count ?? 0);
        }

        return new OutputGeometry(
            string.IsNullOrWhiteSpace(port.NickName) ? port.Name ?? string.Empty : port.NickName,
            geometryItems,
            nulls,
            kinds.OrderByDescending(k => k.Value).Select(k => (k.Key, k.Value)).ToList(),
            union,
            branchCounts);
    }

    /// <summary>
    /// Summarises the input-side data-tree modifiers (graft/flatten, simplify, reverse) on a
    /// component, or null when every input is unmodified. The model can only verify that a
    /// dataMapping patch actually landed if the report states the live setting.
    /// </summary>
    /// <param name="obj">The measured object.</param>
    /// <returns>For example "Curve A: graft, Curve B: graft", or null.</returns>
    private static string? DescribeInputModifiers(IGH_DocumentObject obj)
    {
        if (obj is not IGH_Component component)
        {
            return null;
        }

        List<string>? parts = null;
        foreach (IGH_Param input in component.Params.Input)
        {
            List<string>? mods = null;
            if (input.DataMapping != GH_DataMapping.None)
            {
                (mods ??= new List<string>()).Add(input.DataMapping.ToString().ToLowerInvariant());
            }

            if (input.Simplify)
            {
                (mods ??= new List<string>()).Add("simplify");
            }

            if (input.Reverse)
            {
                (mods ??= new List<string>()).Add("reverse");
            }

            if (mods is not null)
            {
                (parts ??= new List<string>()).Add($"{input.Name}: {string.Join("+", mods)}");
            }
        }

        return parts is null ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// Formats a data-tree shape for the report, e.g. "1 branch × 13 items",
    /// "13 branches × 1 item", or "13 branches (1–3 items)" when branch sizes vary.
    /// </summary>
    /// <param name="branchCounts">Per-branch item counts.</param>
    /// <returns>The tree-shape label.</returns>
    private static string DescribeTree(IReadOnlyList<int> branchCounts)
    {
        if (branchCounts.Count == 1)
        {
            return $"1 branch × {branchCounts[0]} item{(branchCounts[0] == 1 ? string.Empty : "s")}";
        }

        int min = branchCounts.Min();
        int max = branchCounts.Max();
        return min == max
            ? $"{branchCounts.Count} branches × {min} item{(min == 1 ? string.Empty : "s")}"
            : $"{branchCounts.Count} branches ({min}–{max} items)";
    }

    /// <summary>
    /// Maximum nesting depth followed into wrapped collections. A Python list of lists is normal;
    /// anything deeper is not worth measuring, and the cap also stops a self-referencing collection
    /// from spinning here forever.
    /// </summary>
    private const int MaxCollectionDepth = 4;

    /// <summary>
    /// Classifies one goo as placed geometry — kind label plus accurate bbox, one entry per
    /// geometric item found. Yields nothing for construction data (vectors, planes, intervals,
    /// numbers), which spatial reasoning must not treat as parts.
    ///
    /// <para>A goo can hold a whole collection rather than one item. A GH Python Script output is
    /// the case that matters: when the engine wraps a Python list as a single opaque
    /// <c>GH_ObjectWrapper</c> — which it still does whenever the access clobber wins — the value
    /// behind the goo is the list itself, not geometry. Classifying only the wrapper made every
    /// such output invisible, so a Py Transmitter graph that drew a dozen breps reported "NO
    /// GEOMETRY WAS PRODUCED" and invited the model to "fix" working code.</para>
    /// </summary>
    /// <param name="goo">The item to classify.</param>
    /// <returns>One entry per geometric item the goo holds; empty when it holds none.</returns>
    private static IEnumerable<(string Kind, BoundingBox Box)> ClassifyGoo(IGH_Goo goo) =>
        ClassifyValue(goo.ScriptVariable(), 0);

    /// <summary>
    /// Classifies one raw value, recursing into collections.
    /// </summary>
    /// <param name="value">The value behind a goo, or an element of a collection.</param>
    /// <param name="depth">Current nesting depth, capped by <see cref="MaxCollectionDepth"/>.</param>
    /// <returns>One entry per geometric item found.</returns>
    private static IEnumerable<(string Kind, BoundingBox Box)> ClassifyValue(object? value, int depth)
    {
        switch (value)
        {
            case null:
                break;
            case Point3d point:
                yield return ("point", new BoundingBox(point, point));
                break;
            case Line line:
                yield return ("line", new BoundingBox(line.From, line.To));
                break;
            case Curve curve:
                string closure = curve.IsClosed ? "closed" : "open";
                string planarity = curve.IsPlanar() ? "planar" : "non-planar";
                yield return ($"{closure} {planarity} curve", curve.GetBoundingBox(true));
                break;
            case Extrusion extrusion:
                yield return (extrusion.IsSolid ? "closed extrusion" : "open extrusion", extrusion.GetBoundingBox(true));
                break;
            case Surface surface:
                yield return ("surface", surface.GetBoundingBox(true));
                break;
            case Brep brep:
                yield return (brep.IsSolid ? "closed brep" : "open brep", brep.GetBoundingBox(true));
                break;
            case Mesh mesh:
                yield return (mesh.IsClosed ? "closed mesh" : "open mesh", mesh.GetBoundingBox(true));
                break;
            case Box box:
                yield return ("box", box.BoundingBox);
                break;
            case GeometryBase geometry:
                yield return (geometry.GetType().Name.ToLowerInvariant(), geometry.GetBoundingBox(true));
                break;

            // A string is an enumerable of chars and never geometry — keep it out of the recursion.
            case System.Collections.IEnumerable collection and not string when depth < MaxCollectionDepth:
                foreach (object? element in collection)
                {
                    // Elements can themselves be goo (a wrapped list of GH_Brep), so unwrap again.
                    object? inner = element is IGH_Goo elementGoo ? elementGoo.ScriptVariable() : element;
                    foreach ((string Kind, BoundingBox Box) classified in ClassifyValue(inner, depth + 1))
                    {
                        yield return classified;
                    }
                }

                break;
        }
    }

    // ---- Spatial summary -----------------------------------------------------------------------

    // Union-find over component union boxes: two components join when their boxes, inflated by the
    // touch tolerance, intersect. Returns member-index groups, largest first.
    private static List<List<int>> ClusterByBoxTouch(IReadOnlyList<ComponentGeometry> items, double touchTolerance)
    {
        int[] parent = Enumerable.Range(0, items.Count).ToArray();

        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Join(int a, int b) => parent[Find(a)] = Find(b);

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                if (BoxGap(items[i].Union, items[j].Union) <= touchTolerance)
                {
                    Join(i, j);
                }
            }
        }

        return Enumerable.Range(0, items.Count)
            .GroupBy(Find)
            .Select(g => g.ToList())
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => UnionOf(items, g).Diagonal.Length)
            .ToList();
    }

    // Axis-aligned box-to-box distance: per-axis interval gap, combined Euclidean. Zero when the
    // boxes touch or overlap.
    private static double BoxGap(BoundingBox a, BoundingBox b)
    {
        double dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        double dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        double dz = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static BoundingBox UnionOf(IReadOnlyList<ComponentGeometry> items, IEnumerable<int> indices)
    {
        BoundingBox union = BoundingBox.Empty;
        foreach (int index in indices)
        {
            union.Union(items[index].Union);
        }

        return union;
    }

    /// <summary>
    /// The components the spatial analysis treats as PARTS. Components whose every output is
    /// points are construction scaffolding — the corners a polyline is built from, the centres a
    /// column array is arrayed on — not things that can meet, float apart, or be buried.
    ///
    /// <para>Leaving them in wrecked the section they were meant to inform. A point has a
    /// zero-size box, so it lies inside every solid in the model, and each construction point
    /// emitted one containment line per solid: in a measured session 42 of 54 reports ended at the
    /// containment cap with nothing but "'Base A' bbox lies entirely inside 'Tower Mass' bbox" and
    /// seven more of the same. Buried geometry is one of the two things this report exists to
    /// catch, and for most of that session it could not have reported one — the budget was spent
    /// before a real finding could reach it.</para>
    ///
    /// <para>They stay in the per-component listing above, where their coordinates are genuinely
    /// useful (a model verifying that its portico centre landed at 25000,0,0 reads it there). Only
    /// the spatial section drops them, and only when there is other geometry to reason about —
    /// a definition whose whole output IS points keeps them, since excluding everything would
    /// leave nothing to relate.</para>
    /// </summary>
    /// <param name="items">Every measured component.</param>
    /// <returns>The components to relate spatially.</returns>
    private static IReadOnlyList<ComponentGeometry> SpatialParts(IReadOnlyList<ComponentGeometry> items)
    {
        var parts = items.Where(item => !IsPointsOnly(item)).ToList();
        return parts.Count > 0 ? parts : items;
    }

    private static bool IsPointsOnly(ComponentGeometry item) =>
        item.Outputs.All(output => output.Kinds.All(k => string.Equals(k.Kind, "point", StringComparison.Ordinal)));

    // Strict bbox-inside-bbox pairs between different components — a neutral fact (containment is
    // sometimes intentional; the preamble makes the model the judge). Catches buried geometry.
    private static IEnumerable<(int Inner, int Outer)> FindContainments(IReadOnlyList<ComponentGeometry> items, double tolerance)
    {
        for (int inner = 0; inner < items.Count; inner++)
        {
            for (int outer = 0; outer < items.Count; outer++)
            {
                if (inner == outer)
                {
                    continue;
                }

                BoundingBox a = items[inner].Union;
                BoundingBox b = items[outer].Union;
                bool inside =
                    a.Min.X >= b.Min.X - tolerance && a.Max.X <= b.Max.X + tolerance &&
                    a.Min.Y >= b.Min.Y - tolerance && a.Max.Y <= b.Max.Y + tolerance &&
                    a.Min.Z >= b.Min.Z - tolerance && a.Max.Z <= b.Max.Z + tolerance;

                // Require the inner box to be genuinely smaller, so two coincident boxes do not
                // report each other.
                if (inside && a.Diagonal.Length < b.Diagonal.Length - tolerance)
                {
                    yield return (inner, outer);
                }
            }
        }
    }

    // ---- Formatting ----------------------------------------------------------------------------

    private static string BuildReport(
        string message,
        IReadOnlyList<ComponentGeometry> items,
        string units,
        string? baseChecksum,
        IReadOnlyList<string> appliedOps,
        IReadOnlyDictionary<string, string> previousLines,
        Dictionary<string, string> currentLines)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "GEOMETRY REPORT — measured facts about the geometry your graph currently produces. The "
            + "graph solved cleanly; nothing below is an error. Compare these facts against your "
            + "design intent: positions, sizes, proportions, and whether parts connect, float apart, "
            + "or sit inside one another. Entries use the same nickname/id/instanceGuid identities "
            + $"as the canvas state. Units: {units}; coordinates are world XYZ; bbox is axis-aligned.");

        // A progress digest is an instruction as well as a note, and it contradicts the single-shot
        // wording below — so it replaces it outright rather than sitting alongside it. It leads the
        // report because everything after it is the evidence it asks the model to weigh.
        bool incremental = message.Contains(BuildPlanParser.DigestMarker, StringComparison.Ordinal);

        sb.AppendLine();
        if (incremental)
        {
            sb.AppendLine(message.Trim());
        }
        else
        {
            sb.AppendLine(
                "If the geometry matches your intent, reply in plain prose (no JSON) briefly confirming "
                + "what was built. If anything is wrong — a part in the wrong place or at the wrong size, "
                + "elements floating apart that should touch, or elements buried inside others that "
                + "should not be — reply with a corrective ghpatch.");
        }

        if (appliedOps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                "Patch confirmation — your previous patch applied cleanly. These operations DID land "
                + "(do not re-send them; if the geometry below still looks wrong, the fix itself was "
                + "insufficient, not dropped):");
            foreach (string op in CollapseAppliedOps(appliedOps))
            {
                sb.AppendLine("  - " + op);
            }
        }

        if (!incremental && !string.IsNullOrWhiteSpace(message))
        {
            sb.AppendLine();
            sb.AppendLine("Operator note: " + message.Trim());
        }

        if (items.Count == 0)
        {
            // A zero-geometry result is a DEFECT report, not a clean bill of health, and the header
            // above has already offered "reply in prose if it matches your intent" — which a model
            // will take, ending the loop on a model that renders nothing. Say plainly that prose is
            // not an available answer here. (In the 2026-07-25 23:19 session a model did exactly
            // that, and justified it by citing an earlier geometry report that never existed.)
            sb.AppendLine();
            sb.AppendLine(
                "NO GEOMETRY WAS PRODUCED. Not one component in the scanned scope output a geometric "
                + "item — every output holds construction data (numbers, vectors, planes) or nothing "
                + "at all. The request was for geometry, so this is a DEFECT, not a result to "
                + "confirm: do NOT reply in prose and do NOT report the model as built. Trace the "
                + "chain from your geometry-producing components (Domain Box, Extrude, Cylinder, "
                + "Boundary Surfaces and the like) back to their inputs, find where the data stops, "
                + "and reply with a corrective ghpatch. If you believe geometry does exist, trust "
                + "THIS measurement over any earlier turn — it was taken just now, against the live "
                + "canvas.");
        }
        else
        {
            AppendGeometrySection(sb, items, previousLines, currentLines);
            AppendSpatialSection(sb, items);
        }

        // Cap the body BEFORE the checksum line so truncation can never eat the checksum.
        string body = sb.ToString().TrimEnd();
        if (body.Length > MaxReportChars)
        {
            body = body[..MaxReportChars] + Environment.NewLine + "… (report truncated)";
        }

        if (!string.IsNullOrEmpty(baseChecksum))
        {
            body += Environment.NewLine + Environment.NewLine
                + "Current base checksum — copy this verbatim into patch.base.checksum: " + baseChecksum;
        }

        return body;
    }

    /// <summary>
    /// Groups applied operations that say the same thing about different components onto one line.
    ///
    /// <para>A bulk edit reports per component, so hiding twenty components produced twenty lines
    /// of "modified 'X' (guid): hidden (in place)" — identical but for the name, and all of it
    /// competing with the measurements for the report's character budget. The verb and effect are
    /// stated once and the components listed after it.</para>
    /// </summary>
    /// <param name="appliedOps">The per-operation lines from the transmitter.</param>
    /// <returns>The collapsed lines, in first-seen order of effect.</returns>
    private static IEnumerable<string> CollapseAppliedOps(IReadOnlyList<string> appliedOps)
    {
        // "modified 'Ridge Start' (guid): hidden (in place)" splits into the subject (up to the
        // closing paren of the guid) and the effect (after the colon). Ops that do not match the
        // shape pass through untouched rather than being mangled into a group.
        var groups = new List<(string Effect, List<string> Subjects)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string op in appliedOps)
        {
            int split = op.IndexOf("): ", StringComparison.Ordinal);
            int quote = op.IndexOf('\'');
            if (split < 0 || quote < 0 || quote > split)
            {
                groups.Add((op, new List<string>()));
                continue;
            }

            string effect = op[(split + 3)..].Trim();
            string verb = op[..quote].Trim();
            string subject = op[quote..(split + 1)].Trim();
            string key = verb + "|" + effect;

            if (!index.TryGetValue(key, out int at))
            {
                index[key] = groups.Count;
                groups.Add(($"{verb} — {effect}", new List<string> { subject }));
            }
            else
            {
                groups[at].Subjects.Add(subject);
            }
        }

        foreach ((string effect, List<string> subjects) in groups)
        {
            yield return subjects.Count switch
            {
                0 => effect,
                1 => $"{effect}: {subjects[0]}",
                _ => $"{effect} ({subjects.Count}): {string.Join(", ", subjects)}",
            };
        }
    }

    /// <summary>
    /// Writes the per-component measurements, rendering in full only what changed since the last
    /// report and collapsing everything identical into one line.
    ///
    /// <para>Every round used to re-list every component's bounding box verbatim. Past the first
    /// couple of stages almost all of it is byte-identical to the previous round, and it is not
    /// merely wasted tokens: the section is capped at <see cref="MaxReportChars"/>, so the
    /// unchanged bulk was pushing the spatial-relations section — the containments and gaps that
    /// actually reveal a misplacement — past the cap and off the end of the report. Measured live:
    /// 231,000 characters of replayed report across one session, with the relations section
    /// truncated away on the rounds that most needed it.</para>
    ///
    /// <para>Unchanged entries are named, not silently dropped: a model that cannot see a component
    /// listed will assume it stopped producing and set about "fixing" it.</para>
    /// </summary>
    /// <param name="sb">The report under construction.</param>
    /// <param name="items">The harvested geometry.</param>
    /// <param name="previous">Last round's lines by identity; empty on the first report.</param>
    /// <param name="current">Receives this round's lines by identity, for the next comparison.</param>
    private static void AppendGeometrySection(
        System.Text.StringBuilder sb,
        IReadOnlyList<ComponentGeometry> items,
        IReadOnlyDictionary<string, string> previous,
        Dictionary<string, string> current)
    {
        sb.AppendLine();

        var changed = new List<string>();
        var unchanged = new List<string>();

        foreach (ComponentGeometry item in items)
        {
            foreach (OutputGeometry output in item.Outputs)
            {
                string line = FormatComponentLine(output, item.Owner, namePort: item.Outputs.Count > 1);
                string key = item.Owner.InstanceGuid.ToString() + "#" + output.PortName;
                current[key] = line;

                if (previous.TryGetValue(key, out string? was) && string.Equals(was, line, StringComparison.Ordinal))
                {
                    unchanged.Add(Label(item.Owner));
                }
                else
                {
                    changed.Add("  - " + line);
                }
            }

            // Tree modifiers belong to whichever state the component's own line is in — reporting
            // them against a component collapsed as unchanged would be noise without its measurements.
            if (item.InputModifiers is not null && changed.Count > 0)
            {
                changed.Add($"      input tree modifiers: {item.InputModifiers}");
            }
        }

        bool delta = previous.Count > 0;
        sb.AppendLine(delta && unchanged.Count > 0
            ? "Geometry by component — CHANGED since the last report:"
            : "Geometry by component:");

        if (changed.Count == 0)
        {
            sb.AppendLine("  (nothing changed — every measurement below is exactly as last reported)");
        }

        foreach (string line in changed.Take(MaxGeometryLines))
        {
            sb.AppendLine(line);
        }

        if (changed.Count > MaxGeometryLines)
        {
            sb.AppendLine($"  … (+{changed.Count - MaxGeometryLines} more geometry outputs)");
        }

        if (delta && unchanged.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"Unchanged since the last report ({unchanged.Count}) — still present, still producing "
                + "exactly the measurements you were last given, not re-listed: "
                + string.Join(", ", unchanged.Distinct(StringComparer.Ordinal)));
        }

        BoundingBox world = BoundingBox.Empty;
        foreach (ComponentGeometry item in items)
        {
            world.Union(item.Union);
        }

        sb.AppendLine();
        sb.AppendLine($"Whole model: bbox {FormatBox(world)}, size {FormatSize(world)}.");
    }

    private static void AppendSpatialSection(System.Text.StringBuilder sb, IReadOnlyList<ComponentGeometry> allItems)
    {
        IReadOnlyList<ComponentGeometry> items = SpatialParts(allItems);

        BoundingBox world = BoundingBox.Empty;
        foreach (ComponentGeometry item in items)
        {
            world.Union(item.Union);
        }

        double touchTolerance = Math.Max(
            RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001,
            ClusterTouchFraction * world.Diagonal.Length);

        List<List<int>> clusters = ClusterByBoxTouch(items, touchTolerance);
        var containments = FindContainments(items, RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001)
            .Take(MaxContainmentLines + 1)
            .ToList();

        if (clusters.Count <= 1 && containments.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Spatial relations:");

        if (clusters.Count > 1)
        {
            sb.AppendLine($"  - The geometry forms {clusters.Count} disjoint groups (parts that do not touch or overlap, touch tolerance {N(touchTolerance)}):");
            BoundingBox mainBox = UnionOf(items, clusters[0]);
            for (int i = 0; i < clusters.Count; i++)
            {
                string members = string.Join(", ", clusters[i]
                    .Take(MaxClusterMembers)
                    .Select(index => $"'{Nick(items[index].Owner)}'"));
                if (clusters[i].Count > MaxClusterMembers)
                {
                    members += $" (+{clusters[i].Count - MaxClusterMembers} more)";
                }

                if (i == 0)
                {
                    sb.AppendLine($"      1. main group: {members}");
                }
                else
                {
                    double gap = BoxGap(UnionOf(items, clusters[i]), mainBox);
                    sb.AppendLine($"      {i + 1}. {members} — gap of {N(gap)} to the main group");
                }
            }
        }

        for (int i = 0; i < containments.Count && i < MaxContainmentLines; i++)
        {
            (int inner, int outer) = containments[i];
            sb.AppendLine($"  - '{Nick(items[inner].Owner)}' bbox lies entirely inside '{Nick(items[outer].Owner)}' bbox.");
        }

        if (containments.Count > MaxContainmentLines)
        {
            sb.AppendLine("  - … (+more containments not listed)");
        }
    }

    private static string FormatComponentLine(OutputGeometry output, IGH_DocumentObject owner, bool namePort)
    {
        string label = namePort ? $"{Label(owner)}.{output.PortName}" : Label(owner);

        string kinds = output.Kinds.Count == 1 && output.Kinds[0].Count == output.ItemCount
            ? (output.ItemCount == 1 ? output.Kinds[0].Kind : $"{output.ItemCount}x {output.Kinds[0].Kind}")
            : $"{output.ItemCount} items: " + string.Join(", ", output.Kinds.Select(k => $"{k.Count}x {k.Kind}"));

        string nulls = output.NullCount > 0 ? $" ({output.NullCount} null)" : string.Empty;
        string union = output.ItemCount > 1 ? "union bbox" : "bbox";

        // Tree shape appears whenever the output holds more than one item or branch — the
        // difference between "1 branch × 13 items" and "13 branches × 1 item" is invisible in
        // the counts yet decides how downstream components pair the data.
        int totalItems = output.BranchCounts.Sum();
        string tree = output.BranchCounts.Count > 1 || totalItems > 1
            ? $", tree {DescribeTree(output.BranchCounts)}"
            : string.Empty;

        return $"{label}: {kinds}{nulls}{tree}, {union} {FormatBox(output.Union)}, size {FormatSize(output.Union)}";
    }

    private static string FormatBox(BoundingBox box) =>
        $"X[{N(box.Min.X)}..{N(box.Max.X)}] Y[{N(box.Min.Y)}..{N(box.Max.Y)}] Z[{N(box.Min.Z)}..{N(box.Max.Z)}]";

    private static string FormatSize(BoundingBox box) =>
        $"{N(box.Max.X - box.Min.X)} x {N(box.Max.Y - box.Min.Y)} x {N(box.Max.Z - box.Min.Z)}";

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string UnitsLabel()
    {
        return RhinoDoc.ActiveDoc?.ModelUnitSystem.ToString().ToLowerInvariant() ?? "model units";
    }

    private static string Nick(IGH_DocumentObject obj) =>
        string.IsNullOrWhiteSpace(obj.NickName) ? obj.Name ?? string.Empty : obj.NickName;

    /// <summary>
    /// Labels a measured object for the report: name, nickname (when it differs from the name),
    /// the session-stable canvas id (when the object has one — the id the canvas state shows and a
    /// patch's connection endpoints use), and instanceGuid (the identity a patch's match block
    /// uses). Both identity frames in one place, so the model never has to cross-reference the
    /// full canvas export to map a report entry onto patch targets.
    /// </summary>
    /// <param name="obj">The measured object.</param>
    /// <returns>The report label.</returns>
    private static string Label(IGH_DocumentObject obj)
    {
        string id = Generation.GhJsonBridge.TryGetStableId(obj.OnPingDocument(), obj.InstanceGuid, out int stable)
            ? $"id {stable}, "
            : string.Empty;

        return !string.IsNullOrWhiteSpace(obj.NickName) && !string.Equals(obj.NickName, obj.Name, StringComparison.Ordinal)
            ? $"{obj.Name} '{obj.NickName}' ({id}{obj.InstanceGuid})"
            : $"{obj.Name} ({id}{obj.InstanceGuid})";
    }
}
