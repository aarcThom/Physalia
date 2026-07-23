// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.Signals;
using Physalia.GH.Generation;
using Rhino;
using Rhino.Geometry;

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
            "Optional operator note folded into the report (e.g. what the user asked for), so the model weighs the measured facts against that framing.",
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
        GH_Document? doc = OnPingDocument();
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
        GH_Document? doc = OnPingDocument();
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
        string? checksum = GhJsonBridge.TryExportCanvasState(doc)?.Checksum;
        string report = BuildReport(message ?? string.Empty, items, UnitsLabel(), checksum, _pendingAppliedOps);
        _pendingAppliedOps.Clear();

        return RoutingResult.Ok(report, message: $"Measured {items.Count} geometry-producing component(s).", level: GH_RuntimeMessageLevel.Remark);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _watchedGuids.Clear();
        _pendingAppliedOps.Clear();
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
    /// </summary>
    /// <param name="doc">The active document.</param>
    /// <returns>The live watched objects.</returns>
    private List<IGH_DocumentObject> ResolveWatchedObjects(GH_Document doc)
    {
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

            if (ClassifyGoo(goo) is not { } classified)
            {
                continue;
            }

            geometryItems++;
            kinds[classified.Kind] = kinds.TryGetValue(classified.Kind, out int n) ? n + 1 : 1;
            union.Union(classified.Box);
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

    // Classifies one goo as placed geometry (kind label + accurate bbox), or null for construction
    // data — vectors, planes, intervals, numbers — which spatial reasoning must not treat as parts.
    private static (string Kind, BoundingBox Box)? ClassifyGoo(IGH_Goo goo)
    {
        switch (goo.ScriptVariable())
        {
            case Point3d point:
                return ("point", new BoundingBox(point, point));
            case Line line:
                var lineBox = new BoundingBox(line.From, line.To);
                return ("line", lineBox);
            case Curve curve:
                string closure = curve.IsClosed ? "closed" : "open";
                string planarity = curve.IsPlanar() ? "planar" : "non-planar";
                return ($"{closure} {planarity} curve", curve.GetBoundingBox(true));
            case Extrusion extrusion:
                return (extrusion.IsSolid ? "closed extrusion" : "open extrusion", extrusion.GetBoundingBox(true));
            case Surface surface:
                return ("surface", surface.GetBoundingBox(true));
            case Brep brep:
                return (brep.IsSolid ? "closed brep" : "open brep", brep.GetBoundingBox(true));
            case Mesh mesh:
                return (mesh.IsClosed ? "closed mesh" : "open mesh", mesh.GetBoundingBox(true));
            case Box box:
                return ("box", box.BoundingBox);
            case GeometryBase geometry:
                return (goo.TypeName.ToLowerInvariant(), geometry.GetBoundingBox(true));
            default:
                return null;
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

    private static string BuildReport(string message, IReadOnlyList<ComponentGeometry> items, string units, string? baseChecksum, IReadOnlyList<string> appliedOps)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "GEOMETRY REPORT — measured facts about the geometry your graph currently produces on "
            + "the canvas. The graph solved cleanly; nothing below is an error. Compare these facts "
            + "against your design intent: positions, sizes, proportions, and whether parts connect, "
            + "float apart, or sit inside one another. Each entry names its component by nickname, "
            + "canvas id, and instanceGuid — the same identities the canvas state uses, so in a patch "
            + "you can match the component by instanceGuid and reference its connection endpoints by "
            + $"the id, without cross-referencing the canvas state. Units: {units}; "
            + "coordinates are world XYZ; bbox is the axis-aligned bounding box.");
        sb.AppendLine();
        sb.AppendLine(
            "If the geometry matches your intent, reply in plain prose (no JSON) briefly confirming "
            + "what was built. If anything is wrong — a part in the wrong place or at the wrong size, "
            + "elements floating apart that should touch, or elements buried inside others that "
            + "should not be — reply with a corrective ghpatch.");

        if (appliedOps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                "Patch confirmation — your previous patch applied cleanly. These operations DID land "
                + "(do not re-send them; if the geometry below still looks wrong, the fix itself was "
                + "insufficient, not dropped):");
            foreach (string op in appliedOps)
            {
                sb.AppendLine("  - " + op);
            }
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            sb.AppendLine();
            sb.AppendLine("Operator note: " + message.Trim());
        }

        if (items.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                "No geometry-producing components were found in the scanned scope. The graph solved "
                + "but produced no geometry items — every output holds construction data (numbers, "
                + "vectors, planes) or nothing at all.");
        }
        else
        {
            AppendGeometrySection(sb, items);
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

    private static void AppendGeometrySection(System.Text.StringBuilder sb, IReadOnlyList<ComponentGeometry> items)
    {
        sb.AppendLine();
        sb.AppendLine("Geometry by component:");

        var lines = new List<string>();
        foreach (ComponentGeometry item in items)
        {
            foreach (OutputGeometry output in item.Outputs)
            {
                lines.Add("  - " + FormatComponentLine(output, item.Owner, namePort: item.Outputs.Count > 1));
            }

            if (item.InputModifiers is not null)
            {
                lines.Add($"      input tree modifiers: {item.InputModifiers}");
            }
        }

        foreach (string line in lines.Take(MaxGeometryLines))
        {
            sb.AppendLine(line);
        }

        if (lines.Count > MaxGeometryLines)
        {
            sb.AppendLine($"  … (+{lines.Count - MaxGeometryLines} more geometry outputs)");
        }

        BoundingBox world = BoundingBox.Empty;
        foreach (ComponentGeometry item in items)
        {
            world.Union(item.Union);
        }

        sb.AppendLine();
        sb.AppendLine($"Whole model: bbox {FormatBox(world)}, size {FormatSize(world)}.");
    }

    private static void AppendSpatialSection(System.Text.StringBuilder sb, IReadOnlyList<ComponentGeometry> items)
    {
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
