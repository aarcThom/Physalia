// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Physalia.Core.Common;
using Physalia.Core.Grounding;
using Physalia.Core.Signals;
using Physalia.GH.Generation;
using Rhino;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// On receiving a signal, scans Grasshopper components for runtime problems and routes the
/// outcome: a clean scan passes the incoming payload forward on the Success Signal; any errors,
/// warnings, or dead components (ones that produced no output) are gathered into a report routed
/// back on the Fail Signal.
///
/// <para>The scan is scoped by the GUIDs this component has been pointed at. A Component
/// Transmitter forwards the GUIDs of the components it placed (newline-separated) as its Success
/// payload — but on a patch turn that payload is only the DELTA (added and modified components),
/// while the patch's changes can break components placed in earlier turns. So the Runtime Health
/// Check ACCUMULATES every GUID that has ever arrived and scans the whole watched graph each turn,
/// pruning GUIDs whose components have since been removed. The watch list is session-only, like
/// all lifecycle state — the component reopens empty. When nothing is watched and the payload
/// carries no GUIDs it falls back to scanning the whole document (errors/warnings only),
/// preserving use as a standalone canvas probe. Wiring it after the Component Transmitter (and its
/// Fail Signal back through Feedback to the Conversation Log) turns the place → read → correct loop into a
/// visible cycle on the canvas.</para>
/// </summary>
public class RuntimeHealthCheck : RoutingComponentBase<string>
{
    // Fail on Warnings dial, exposed as a context-menu toggle (ConversationLog's menu-flag
    // pattern) rather than an input: an extra input before the base-appended Signal shifts the
    // param layout of every previously saved document, silently landing the old signal wire on
    // the new input. Persisted via Write/Read; default true preserves the original behavior.
    private bool _failOnWarnings = true;

    // Sampling caps: the data-flow section quotes live values, and an unbounded tree (or a
    // monster string riding a text port) must never blow up the prompt. Each overflow is
    // labelled "… (+N more)" so the model knows the sample is partial, not the whole story.
    private const int MaxSampleItems = 5;
    private const int MaxSampleBranches = 3;
    private const int MaxItemsPerBranch = 3;
    private const int MaxItemChars = 48;
    private const int MaxPortSampleChars = 240;
    private const int MaxDistinctScan = 64;
    private const int MaxDataFlowComponents = 12;

    // Every component GUID ever received on a consumed signal, minus those since removed from
    // the document (pruned at scan time). Session-only; never serialized.
    private readonly HashSet<Guid> _watchedGuids = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RuntimeHealthCheck"/> class.
    /// </summary>
    public RuntimeHealthCheck()
        : base("Runtime Health Check", "Runtime Health Check", "Scans the placed graph (or whole document) for errors, warnings, and dead components and routes a report back on the Fail Signal; a clean scan passes the signal through.", "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F4B0D63C-8E57-4A4B-9C3D-5B2A7F1E04D8");

    /// <inheritdoc/>
    /// <remarks>
    /// GH warnings are frequently benign (a collapsed zero-length segment, a data conversion
    /// note), and a warnings-only report can drive the feedback loop through rounds the model
    /// cannot fix. The toggle lets a rig treat warnings as informational; errors, dead
    /// components, and null producers always fail regardless. A menu item, not an input — see
    /// the field note on <see cref="_failOnWarnings"/>.
    /// </remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(
            menu,
            "Fail on Warnings",
            (_, _) => _failOnWarnings = !_failOnWarnings,
            enabled: true,
            @checked: _failOnWarnings);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer)
    {
        writer.SetBoolean("FailOnWarnings", _failOnWarnings);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        _failOnWarnings = !reader.ItemExists("FailOnWarnings") || reader.GetBoolean("FailOnWarnings");
        return base.Read(reader);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The payload (the placed components' GUIDs from a Component Transmitter) is passed through
    /// unchanged when the scan is clean. A blank payload is accepted once GUIDs are being watched:
    /// a remove-only patch legitimately adds and modifies nothing, but the remaining watched graph
    /// still needs scanning.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload ?? string.Empty;
        return StringHelpers.IsNonBlank(data) || _watchedGuids.Count > 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Accumulates the incoming GUIDs into the watch list, so the read pass scans the whole
    /// LLM-built graph rather than just this turn's delta — a patch reports only its added and
    /// modified components, but its changes can break components placed in earlier turns.
    /// </remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        foreach (Guid guid in ParseGuids(data))
        {
            _watchedGuids.Add(guid);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Defers the scan until every scoped component has finished solving, so the runtime state
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
        var errors = new List<string>();
        var warnings = new List<string>();
        var dead = new List<string>();
        var nullProducers = new List<string>();
        var nullCascades = new List<string>();
        var signatures = new List<string>();
        var dataFlow = new List<string>();
        var signatureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool scopedScan = false;

        GH_Document? doc = OnPingDocument();
        if (doc is not null)
        {
            (IReadOnlyList<IGH_DocumentObject> scope, scopedScan) = ScanScope(doc);
            foreach (IGH_DocumentObject obj in scope)
            {
                if (obj is not IGH_ActiveObject ao)
                {
                    continue;
                }

                var objErrors = ao.RuntimeMessages(GH_RuntimeMessageLevel.Error);
                foreach (string message in objErrors)
                {
                    errors.Add($"{Label(ao)}: {message}");
                }

                var objWarnings = ao.RuntimeMessages(GH_RuntimeMessageLevel.Warning);
                foreach (string message in objWarnings)
                {
                    warnings.Add($"{Label(ao)}: {message}");
                }

                // GH runtime messages name the component but not the offending input (e.g. "Data
                // conversion failed from Number to Vector"), so pair every problem-reporting
                // component with its live input signature — the model then sees which slot expects
                // which type. Dead components are excluded: their problem is missing data, not types.
                if ((objErrors.Count > 0 || objWarnings.Count > 0) &&
                    obj is IGH_Component problemComp &&
                    signatureNames.Add(problemComp.Name))
                {
                    string inputs = string.Join(", ", ComponentSignatureProvider
                        .ReadPorts(problemComp.Params.Input, inputSide: true)
                        .Select(p => SignatureFormat.Port(p.Name, p.TypeHint, p.Required)));
                    if (!string.IsNullOrWhiteSpace(inputs))
                    {
                        signatures.Add($"{problemComp.Name} inputs: {inputs}");
                    }
                }

                // A component that produced no data on any output is "dead". VolatileData.IsEmpty
                // is the WRONG test: a solved output routinely holds a branch with zero items,
                // which reports non-empty — DataCount counts the items that actually exist. Skip a
                // component that already reported an error — its empty output is a cascade
                // symptom, not the root cause.
                bool isDead = objErrors.Count == 0 &&
                    obj is IGH_Component deadComp &&
                    deadComp.Params.Output.Count > 0 &&
                    deadComp.Params.Output.All(p => p.VolatileData.DataCount == 0);
                if (isDead)
                {
                    dead.Add(Label(obj));
                }

                // Nulls are ITEMS, so DataCount alone misses them: a component can solve with no
                // runtime message at all and still emit a null (an unwired input, an invalid
                // construction) that poisons everything downstream. Only ROOT producers are worth
                // reporting: a component whose null arrived on a wired input is a cascade symptom
                // — one root can poison dozens, and listing them all buries the fix.
                bool producesNulls = objErrors.Count == 0 &&
                    obj is IGH_Component nullComp &&
                    nullComp.Params.Output.Any(p => NullCount(p) > 0);
                bool isRootNull = producesNulls &&
                    obj is IGH_Component rootComp &&
                    !rootComp.Params.Input.Any(p => p.SourceCount > 0 && NullCount(p) > 0);
                if (isRootNull)
                {
                    nullProducers.Add(Label(obj));
                }
                else if (producesNulls)
                {
                    nullCascades.Add(Label(obj));
                }

                // Problem, dead, and root-null components additionally report their live data
                // flow — how many items (and nulls) each input collected and each output produced —
                // so the model sees WHERE data stops instead of blindly swapping strategies.
                if ((objErrors.Count > 0 || objWarnings.Count > 0 || isDead || isRootNull) && obj is IGH_Component flowComp)
                {
                    dataFlow.Add(FormatDataFlow(flowComp));
                }
            }
        }

        // A scoped scan can see cascades whose root lies outside the watch list (or on an erroring
        // component); when no root was found the cascades are the best signal available.
        if (nullProducers.Count == 0 && nullCascades.Count > 0)
        {
            nullProducers.AddRange(nullCascades);
        }
        else if (nullCascades.Count > 0)
        {
            nullProducers.Add($"…plus {nullCascades.Count} downstream component(s) that received these nulls — fix the roots above first.");
        }

        int hard = errors.Count + dead.Count + nullProducers.Count;
        if (hard == 0 && warnings.Count == 0)
        {
            return RoutingResult.Ok(data);
        }

        if (hard == 0 && !_failOnWarnings)
        {
            // Warnings only, and the rig opted out of failing on them: pass the payload through
            // untouched (downstream scoping depends on it) and surface the warnings as a remark.
            return RoutingResult.Ok(data, message: $"{warnings.Count} warning(s) noted, not failed (Fail on Warnings is off).", level: GH_RuntimeMessageLevel.Remark);
        }

        // With values in play a long problem list gets heavy; cap the data-flow lines and say so.
        if (dataFlow.Count > MaxDataFlowComponents)
        {
            int dropped = dataFlow.Count - MaxDataFlowComponents;
            dataFlow.RemoveRange(MaxDataFlowComponents, dropped);
            dataFlow.Add($"… (+{dropped} more problem components)");
        }

        // The last patch APPLIED, so the model's remembered base checksum is stale; carry the fresh
        // one in the feedback so the corrective patch cannot mismatch. Payload text only — carrier
        // discipline holds. IsReadReady settled the graph, so the export is stable here.
        string? checksum = doc is null ? null : GhJsonBridge.TryExportCanvasState(doc)?.Checksum;
        return RoutingResult.Fail(BuildFeedback(errors, warnings, dead, nullProducers, signatures, dataFlow, scopedScan, checksum), $"{hard + warnings.Count} problem(s) found in the scanned graph.", GH_RuntimeMessageLevel.Warning);
    }

    /// <summary>
    /// Renders a problem component's live data flow: items collected per input and items produced
    /// per output, with null counts and sampled values, e.g.
    /// <c>PolyLine 'Gable' (guid): inputs [Vertices=6 (only 3 distinct): {0, 0, 2800}, …; Closed=1: true] -> outputs [Polyline=1: open planar polyline, 5 segment(s)]</c>.
    /// Counts alone proved insufficient in practice — the model hypothesizes blindly about data
    /// it cannot see; the samples let it diagnose in one read.
    /// </summary>
    /// <param name="comp">The component to report.</param>
    /// <returns>The data-flow line.</returns>
    private static string FormatDataFlow(IGH_Component comp)
    {
        // Ports join on "; " because a port's samples themselves join on ", ".
        string ins = string.Join("; ", comp.Params.Input.Select(FormatPortFlow));
        string outs = string.Join("; ", comp.Params.Output.Select(FormatPortFlow));
        return comp.Params.Input.Count == 0
            ? $"{Label(comp)}: outputs [{outs}]"
            : $"{Label(comp)}: inputs [{ins}] -> outputs [{outs}]";
    }

    private static string FormatPortFlow(IGH_Param param)
    {
        int nulls = NullCount(param);
        string count = $"{PortLabel(param)}={param.VolatileData.DataCount}";
        if (DistinctPointCount(param) is int distinct && distinct < param.VolatileData.DataCount)
        {
            count += $" (only {distinct} distinct)";
        }

        if (nulls > 0)
        {
            count += $" ({nulls} null)";
        }

        string samples = FormatPortSamples(param);
        return samples.Length == 0 ? count : $"{count}: {samples}";
    }

    /// <summary>
    /// Samples the actual values a port holds, so the model reasons about the data that exists
    /// instead of hypothesizing from item counts alone. Flat data renders as a capped item list;
    /// treed data shows branch paths (a graft/flatten mistake is invisible in a flat list).
    /// </summary>
    /// <param name="param">The port to sample.</param>
    /// <returns>The rendered samples, or an empty string when the port holds no data.</returns>
    private static string FormatPortSamples(IGH_Param param)
    {
        IGH_Structure tree = param.VolatileData;
        if (tree.DataCount == 0 || tree.PathCount == 0)
        {
            return string.Empty;
        }

        string samples;
        if (tree.PathCount == 1)
        {
            samples = FormatBranchSamples(tree.get_Branch(tree.Paths[0]), MaxSampleItems);
        }
        else
        {
            var parts = new List<string>();
            int shown = Math.Min(tree.PathCount, MaxSampleBranches);
            for (int i = 0; i < shown; i++)
            {
                GH_Path path = tree.Paths[i];
                IList branch = tree.get_Branch(path);
                parts.Add($"{path} ({branch.Count}): {FormatBranchSamples(branch, MaxItemsPerBranch)}");
            }

            if (tree.PathCount > shown)
            {
                parts.Add($"… (+{tree.PathCount - shown} more branches)");
            }

            samples = $"{tree.PathCount} branches [{string.Join("; ", parts)}]";
        }

        return Truncate(samples, MaxPortSampleChars);
    }

    private static string FormatBranchSamples(IList branch, int cap)
    {
        var rendered = new List<string>();
        int shown = Math.Min(branch.Count, cap);
        for (int i = 0; i < shown; i++)
        {
            rendered.Add(FormatGooSample(branch[i] as IGH_Goo));
        }

        if (branch.Count > shown)
        {
            rendered.Add($"… (+{branch.Count - shown} more)");
        }

        return string.Join(", ", rendered);
    }

    // One item, rendered for the model: geometry gets the facts a downstream failure hinges on
    // (closed/open, planar, counts); everything else falls back to the goo's own ToString.
    private static string FormatGooSample(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return "null";
        }

        return goo.ScriptVariable() switch
        {
            Point3d point => FormatPoint(point),
            Line line => $"line {FormatPoint(line.From)} -> {FormatPoint(line.To)}",
            Curve curve => DescribeCurve(curve),
            Brep brep => $"{(brep.IsSolid ? "closed" : "open")} brep, {brep.Faces.Count} face(s)",
            Mesh mesh => $"{(mesh.IsClosed ? "closed" : "open")} mesh, {mesh.Vertices.Count} vertices, {mesh.Faces.Count} faces",
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            int integer => integer.ToString(CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            _ => Truncate(goo.ToString() ?? goo.TypeName, MaxItemChars),
        };
    }

    private static string DescribeCurve(Curve curve)
    {
        string closure = curve.IsClosed ? "closed" : "open";
        string planarity = curve.IsPlanar() ? "planar" : "non-planar";
        return curve.TryGetPolyline(out Polyline polyline)
            ? $"{closure} {planarity} polyline, {polyline.SegmentCount} segment(s)"
            : $"{closure} {planarity} curve, {curve.SpanCount} span(s)";
    }

    private static string FormatPoint(Point3d point)
    {
        return string.Format(CultureInfo.InvariantCulture, "{{{0:0.###}, {1:0.###}, {2:0.###}}}", point.X, point.Y, point.Z);
    }

    /// <summary>
    /// Counts tolerance-distinct points on an all-point port. Duplicate points are the classic
    /// symptom of two wires collecting into one item-access input (every downstream item doubles),
    /// and the duplication is invisible in both the item count and a casual read of the samples —
    /// "6 (only 3 distinct)" names the disease directly.
    /// </summary>
    /// <param name="param">The port to scan.</param>
    /// <returns>The distinct count, or null when the port is not all-points or is too large to scan.</returns>
    private static int? DistinctPointCount(IGH_Param param)
    {
        IGH_Structure tree = param.VolatileData;
        if (tree.DataCount < 2 || tree.DataCount > MaxDistinctScan)
        {
            return null;
        }

        var points = new List<Point3d>(tree.DataCount);
        foreach (IGH_Goo? goo in tree.AllData(false))
        {
            if (goo?.ScriptVariable() is not Point3d point)
            {
                return null;
            }

            points.Add(point);
        }

        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
        var distinct = new List<Point3d>();
        foreach (Point3d point in points)
        {
            if (!distinct.Any(seen => seen.DistanceTo(point) <= tolerance))
            {
                distinct.Add(point);
            }
        }

        return distinct.Count;
    }

    private static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..(maxChars - 1)] + "…";

    // Null items inside the param's volatile data. Nulls count toward DataCount, so they are
    // invisible to the dead-component check — this is the complementary test.
    private static int NullCount(IGH_Param param) =>
        param.VolatileData.AllData(false).Count(goo => goo is null);

    private static string PortLabel(IGH_Param param) =>
        string.IsNullOrWhiteSpace(param.NickName) ? param.Name ?? string.Empty : param.NickName;

    /// <summary>
    /// Labels a scanned object for the feedback report: name, nickname (when it differs from the
    /// name), the session-stable canvas id when the object has one (the id a patch's connection
    /// endpoints use), and instanceGuid — the same identities the canvas-state grounding exports,
    /// so the model can address the exact component in a patch instead of guessing among
    /// same-named ones.
    /// </summary>
    /// <param name="obj">The scanned object.</param>
    /// <returns>The feedback label.</returns>
    private static string Label(IGH_DocumentObject obj)
    {
        string id = Generation.GhJsonBridge.TryGetStableId(obj.OnPingDocument(), obj.InstanceGuid, out int stable)
            ? $"id {stable}, "
            : string.Empty;

        return !string.IsNullOrWhiteSpace(obj.NickName) && !string.Equals(obj.NickName, obj.Name, StringComparison.Ordinal)
            ? $"{obj.Name} '{obj.NickName}' ({id}{obj.InstanceGuid})"
            : $"{obj.Name} ({id}{obj.InstanceGuid})";
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _watchedGuids.Clear();
    }

    /// <summary>
    /// The objects to scan: every watched component still alive on the document (the accumulated
    /// LLM-built graph), or every object on the document (except this Runtime Health Check) when
    /// nothing is watched — the standalone-probe fallback.
    /// </summary>
    /// <param name="doc">The active document.</param>
    /// <returns>The objects in scope, and whether the scan is scoped to the watched graph.</returns>
    private (IReadOnlyList<IGH_DocumentObject> Objects, bool Scoped) ScanScope(GH_Document doc)
    {
        // ResolveWatchedObjects prunes the watch list, so a non-empty list after it means the
        // scan is scoped — even if every watched component is currently locked (excluded from
        // the resolved objects), the scope must not silently widen to the whole document.
        List<IGH_DocumentObject> watched = ResolveWatchedObjects(doc);
        return _watchedGuids.Count > 0
            ? (watched, true)
            : (doc.Objects.Where(o => o.InstanceGuid != InstanceGuid).ToList(), false);
    }

    /// <summary>
    /// Resolves the watch list against the live document, pruning GUIDs whose components have
    /// been removed (by a patch, an undo, or the user) so the list tracks the graph as it exists
    /// now. Locked components stay watched but are excluded from the scan — they never solve, so
    /// their state is stale and waiting on them would jam the settle gate.
    /// <para>The authored-placement ledger is folded in first: signal payloads only arrive on turns
    /// where every upstream guardrail passed, so a turn that failed earlier in the chain would
    /// otherwise leave its components permanently unwatched. The ledger records everything the model
    /// has placed regardless of which guardrails ran — see the Geometry Report, where the same hole
    /// produced a "no geometry" verdict on a model full of boxes.</para>
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

    private static string BuildFeedback(IReadOnlyList<string> errors, IReadOnlyList<string> warnings, IReadOnlyList<string> dead, IReadOnlyList<string> nullProducers, IReadOnlyList<string> signatures, IReadOnlyList<string> dataFlow, bool scopedScan, string? baseChecksum)
    {
        var sb = new StringBuilder();
        sb.AppendLine(scopedScan
            ? "The graph from your last response was placed on the canvas, and it reported the problems below. Each problem names its component by nickname and instanceGuid so you can address the exact component. Correct the definition and resubmit."
            : "The scanned document reported problems. Please correct the definition and resubmit.");

        AppendSection(sb, "Errors:", errors);
        AppendSection(sb, "Warnings:", warnings);
        AppendSection(sb, "Input signatures of the components that reported problems (match your data types to these):", signatures);
        AppendSection(sb, "Components that produced no output (check their inputs and upstream wiring):", dead);
        AppendSection(sb, "Components that produced NULL values (a null usually means an unwired required input or an invalid construction upstream — trace the data flow below and wire or internalize the missing value):", nullProducers);
        AppendSection(sb, "Data flow of the problem components (items collected per input -> items produced per output, with sampled values; an input at 0 received nothing from upstream; nulls are counted in parentheses; '(only N distinct)' means the port holds duplicate items — usually two wires collecting into one input):", dataFlow);

        if (!string.IsNullOrEmpty(baseChecksum))
        {
            sb.AppendLine();
            sb.AppendLine("Current base checksum — copy this verbatim into patch.base.checksum: " + baseChecksum);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine(heading);
        foreach (string item in items)
        {
            sb.AppendLine($"  - {item}");
        }
    }
}
