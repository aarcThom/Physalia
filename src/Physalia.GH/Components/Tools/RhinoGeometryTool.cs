// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Common;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that builds a piece of geometry in the active Rhino document and
/// references it into Grasshopper through a floating geometry parameter (a Point or Curve param).
/// It lets the model supply a real Rhino input to the definition — a curve to feed a Python Script
/// component, say — instead of reaching for a slider or hand-drawn geometry. Wire it into a Router
/// (Signal input) like any tool; when the model calls it, the node bakes the geometry into Rhino and
/// drops a parameter that references it beside this node, then reports the result on the Result output.
///
/// <para>Baking a Rhino object and adding a Grasshopper object both mutate documents and trigger their
/// own solutions, so — like the Component Transmitter — the placement is deferred to
/// <c>RhinoApp.Idle</c> and runs after the dispatch solution settles. The tool result the model sees is
/// a plain confirmation of what was created; the geometry and its referencing parameter appear on the
/// canvas a moment later.</para>
/// </summary>
public class RhinoGeometryTool : ToolComponentBase
{
    // Placement layout. Referencing params land just left of the Component Transmitter's arrow tip —
    // the point where the transmitter drops the generated graph — so they read as the graph's leftmost
    // input sources and feed it left-to-right without the graph overlapping them. Multiple params stack
    // downward. When no transmitter is present they fall back to sitting beside this tool node.
    private const float TipLeftGap = 150f;
    private const float PlacementGapX = 220f;
    private const float PlacementGapY = 70f;

    private static readonly ToolDefinition ToolDef = new(
        "create_rhino_geometry",
        "Create a piece of geometry in the active Rhino document and reference it into Grasshopper via a "
        + "parameter node (a Point or Curve parameter). Use this to hand the definition a real Rhino input "
        + "— for example a curve to feed a Python Script component, or a point/curve you would otherwise "
        + "build from sliders. Coordinates are in the document's unit system. Points are [x, y, z] arrays "
        + "(z defaults to 0 if omitted).",
        "{\"type\":\"object\",\"properties\":{"
        + "\"geometryType\":{\"type\":\"string\",\"enum\":[\"point\",\"line\",\"polyline\",\"curve\",\"circle\"],\"description\":\"The kind of geometry to create.\"},"
        + "\"points\":{\"type\":\"array\",\"description\":\"Control/through points as [x,y,z] arrays. point: 1 point; line: 2 points; polyline/curve: 2+ points.\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":2,\"maxItems\":3}},"
        + "\"degree\":{\"type\":\"integer\",\"description\":\"Curve degree for 'curve' (default 3).\",\"default\":3},"
        + "\"closed\":{\"type\":\"boolean\",\"description\":\"Close the polyline/curve (default false).\",\"default\":false},"
        + "\"center\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"description\":\"[x,y,z] centre for 'circle' (defaults to the first point, else the origin).\"},"
        + "\"radius\":{\"type\":\"number\",\"description\":\"Radius for 'circle'.\"},"
        + "\"name\":{\"type\":\"string\",\"description\":\"Name for the Rhino object and the Grasshopper parameter's nickname. This is the handle a later Node Graph uses to reference this input instead of recreating it, so choose a clear, distinct name (e.g. 'baseCurve'). If omitted or already taken, a unique name is assigned and returned.\"}"
        + "},\"required\":[\"geometryType\"]}");

    // Geometry queued by the dispatch solve, placed on the next RhinoApp.Idle (document mutation must
    // not run inside SolveInstance). Access is single-threaded: enqueued on the solve thread, drained
    // on the UI thread's Idle — both the same thread, never concurrently.
    private readonly List<Placement> _pending = new();
    private bool _idleHooked;

    // Registry of the input parameters this node has placed on the canvas: each param's InstanceGuid,
    // the unique name the model references it by (its nickname), and its geometry type. Persisted so
    // references survive save/load. The Canvas Inputs grounder and the placement layer read this (via
    // CollectReferenceableInputs) so a generated graph can wire onto an existing input rather than
    // duplicate it. Stale entries (params the user deleted) are pruned each solve.
    private readonly List<PlacedInput> _placed = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RhinoGeometryTool"/> class.
    /// </summary>
    public RhinoGeometryTool()
        : base("Rhino Geometry", "RhinoGeo", "A tool the model calls to create Rhino geometry and reference it into Grasshopper via a parameter node.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7D3F1A94-2C6B-4E58-9A1F-3B0C7E5D8A21");

    /// <inheritdoc/>
    protected override ToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    /// <remarks>
    /// Builds the geometry synchronously (pure RhinoCommon, no document access) and queues it; the
    /// document-mutating bake + parameter placement runs on <c>RhinoApp.Idle</c>.
    /// </remarks>
    protected override ToolCallResult ExecuteCall(ToolCallContent call)
    {
        Placement placement;
        try
        {
            if (!TryBuild(call.InputJson, out placement, out string error))
            {
                return ToolCallResult.Error(error);
            }
        }
        catch (JsonException ex)
        {
            return ToolCallResult.Error($"create_rhino_geometry received invalid JSON arguments: {ex.Message}");
        }

        // Settle on the final, unique reference name now (on the solve thread, where the document is
        // reachable) so the tool result can tell the model the exact handle to reference — placement
        // itself is deferred to Idle.
        string what = placement.IsPoint ? "point" : placement.Kind;
        string finalName = UniqueInputName(placement.Name, placement.IsPoint ? "point" : placement.Kind);
        placement = placement with { Name = finalName };

        _pending.Add(placement);
        HookIdle();

        string param = placement.IsPoint ? "Point" : "Curve";
        return ToolCallResult.Ok(
            $"Created a {what} named \"{finalName}\" in the Rhino document and placed a {param} parameter on the canvas referencing it. "
            + $"To use it in a Node Graph, reference it by its exact name \"{finalName}\" (do not recreate it) and it will be wired onto this existing input.");
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        UnhookIdle();
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _pending.Clear();
    }

    private void HookIdle()
    {
        if (!_idleHooked)
        {
            _idleHooked = true;
            RhinoApp.Idle += OnIdlePlace;
        }
    }

    private void UnhookIdle()
    {
        if (_idleHooked)
        {
            _idleHooked = false;
            RhinoApp.Idle -= OnIdlePlace;
        }
    }

    // Bakes the queued geometry into Rhino and drops a referencing parameter beside this node, once the
    // dispatch solution has settled. One-shot: unhooks itself immediately so it fires exactly once.
    private void OnIdlePlace(object? sender, EventArgs e)
    {
        UnhookIdle();

        List<Placement> batch = _pending.ToList();
        _pending.Clear();
        if (batch.Count == 0)
        {
            return;
        }

        RhinoDoc? rhinoDoc = RhinoDoc.ActiveDoc;
        GH_Document? ghDoc = OnPingDocument();
        if (rhinoDoc is null || ghDoc is null)
        {
            return;
        }

        PruneMissing(ghDoc);

        PointF origin = PlacementOrigin(ghDoc);
        int row = _placed.Count;
        foreach (Placement placement in batch)
        {
            IGH_Param? param = BakeAndReference(placement, rhinoDoc);
            if (param is null)
            {
                continue;
            }

            param.NickName = placement.Name;
            param.CreateAttributes();
            param.Attributes.Pivot = new PointF(origin.X, origin.Y + (row * PlacementGapY));
            ghDoc.AddObject(param, false);

            // Register the placed input so it can be referenced (not recreated) by a later graph.
            _placed.Add(new PlacedInput(param.InstanceGuid, placement.Name, placement.IsPoint ? "Point" : "Curve"));
            row++;
        }

        rhinoDoc.Views.Redraw();
        ghDoc.NewSolution(false);
    }

    // The canvas point the first placed param lands on: just left of the Component Transmitter's arrow
    // tip (its drop target, or the default spot right of the transmitter when no arrow has been dropped),
    // so the input sits as a leftmost source feeding the graph the transmitter drops at the tip. Falls
    // back to sitting beside this tool node when the document has no Component Transmitter to anchor to.
    private PointF PlacementOrigin(GH_Document doc)
    {
        ComponentTransmitter? transmitter = doc.Objects.OfType<ComponentTransmitter>().FirstOrDefault();
        if (transmitter?.Attributes is { } attr)
        {
            PointF tip = transmitter.PlacementTarget ?? new PointF(attr.Bounds.Right + 50f, attr.Bounds.Y);
            return new PointF(tip.X - TipLeftGap, tip.Y);
        }

        return new PointF(Attributes.Pivot.X + PlacementGapX, Attributes.Pivot.Y);
    }

    /// <inheritdoc/>
    /// <remarks>Drops registry entries for params the user has since deleted, so the referenceable set stays accurate.</remarks>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        base.OnSolveTick(da);
        PruneMissing(OnPingDocument());
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("PlacedInputCount", _placed.Count);
        for (int i = 0; i < _placed.Count; i++)
        {
            writer.SetGuid("PlacedInputGuid", i, _placed[i].ParamGuid);
            writer.SetString("PlacedInputName", i, _placed[i].Name);
            writer.SetString("PlacedInputType", i, _placed[i].TypeName);
        }

        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _placed.Clear();
        if (reader.ItemExists("PlacedInputCount"))
        {
            int count = reader.GetInt32("PlacedInputCount");
            for (int i = 0; i < count; i++)
            {
                Guid guid = reader.GetGuid("PlacedInputGuid", i);
                string name = reader.GetString("PlacedInputName", i);
                string type = reader.GetString("PlacedInputType", i);
                _placed.Add(new PlacedInput(guid, name, type));
            }
        }

        return base.Read(reader);
    }

    /// <summary>
    /// Collects the inputs currently referenceable in <paramref name="doc"/> — every parameter placed
    /// by a <see cref="RhinoGeometryTool"/> that still exists on the canvas — as name + type + the live
    /// parameter (whose output is itself). Names are unique (first wins on a collision across tools).
    /// Read by the Canvas Inputs grounder and by the placement layer so a generated graph can wire onto
    /// an existing input instead of duplicating it.
    /// </summary>
    /// <param name="doc">The Grasshopper document to scan, or null.</param>
    /// <returns>The referenceable inputs.</returns>
    internal static IReadOnlyList<ReferenceableInput> CollectReferenceableInputs(GH_Document? doc)
    {
        var result = new List<ReferenceableInput>();
        if (doc is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RhinoGeometryTool tool in doc.Objects.OfType<RhinoGeometryTool>())
        {
            foreach (PlacedInput placed in tool._placed)
            {
                if (string.IsNullOrWhiteSpace(placed.Name) || !seen.Add(placed.Name))
                {
                    continue;
                }

                if (doc.FindObject(placed.ParamGuid, false) is IGH_Param param)
                {
                    result.Add(new ReferenceableInput(placed.Name, placed.TypeName, param));
                }
            }
        }

        return result;
    }

    // Removes registry entries whose parameter no longer exists in the document (user deleted it).
    private void PruneMissing(GH_Document? doc)
    {
        if (doc is null || _placed.Count == 0)
        {
            return;
        }

        _placed.RemoveAll(p => doc.FindObject(p.ParamGuid, false) is not IGH_Param);
    }

    // Returns a reference name not already taken by another canvas input or an earlier pending
    // placement in this batch: the requested name if free, else "<base>-2", "<base>-3", … A blank
    // request falls back to the geometry kind ("curve", "point", …).
    private string UniqueInputName(string requested, string kind)
    {
        string baseName = string.IsNullOrWhiteSpace(requested) ? kind : requested.Trim();

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ReferenceableInput input in CollectReferenceableInputs(OnPingDocument()))
        {
            taken.Add(input.Name);
        }

        foreach (Placement pending in _pending)
        {
            taken.Add(pending.Name);
        }

        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (int n = 2; ; n++)
        {
            string candidate = $"{baseName}-{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    // Adds one placement's geometry to the Rhino document and returns a floating GH parameter whose
    // persistent data references that Rhino object by id (so editing the Rhino object updates GH).
    private static IGH_Param? BakeAndReference(Placement placement, RhinoDoc rhinoDoc)
    {
        ObjectAttributes attr = rhinoDoc.CreateDefaultAttributes();
        if (!string.IsNullOrWhiteSpace(placement.Name))
        {
            attr.Name = placement.Name;
        }

        if (placement.IsPoint)
        {
            Guid id = rhinoDoc.Objects.AddPoint(placement.Point, attr);
            if (id == Guid.Empty)
            {
                return null;
            }

            var goo = new GH_Point { ReferenceID = id };
            goo.LoadGeometry(rhinoDoc);
            var param = new Param_Point();
            param.PersistentData.Append(goo, new GH_Path(0));
            return param;
        }

        if (placement.Curve is not null)
        {
            Guid id = rhinoDoc.Objects.AddCurve(placement.Curve, attr);
            if (id == Guid.Empty)
            {
                return null;
            }

            var goo = new GH_Curve { ReferenceID = id };
            goo.LoadGeometry(rhinoDoc);
            var param = new Param_Curve();
            param.PersistentData.Append(goo, new GH_Path(0));
            return param;
        }

        return null;
    }

    // Parses the tool arguments into a placement (pure — no document access). Returns false with a
    // model-facing error when the arguments are missing or inconsistent, so the model can self-correct.
    private static bool TryBuild(string inputJson, out Placement placement, out string error)
    {
        placement = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(inputJson))
        {
            error = "create_rhino_geometry requires arguments (at least a 'geometryType').";
            return false;
        }

        using JsonDocument doc = JsonDocument.Parse(inputJson);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "create_rhino_geometry arguments must be a JSON object.";
            return false;
        }

        string kind = root.TryGetProperty("geometryType", out JsonElement gt) && gt.ValueKind == JsonValueKind.String
            ? (gt.GetString() ?? string.Empty).Trim().ToLowerInvariant()
            : string.Empty;
        if (kind.Length == 0)
        {
            error = "create_rhino_geometry requires a 'geometryType' (point, line, polyline, curve, or circle).";
            return false;
        }

        string name = root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String
            ? (n.GetString() ?? string.Empty).Trim()
            : string.Empty;
        bool closed = root.TryGetProperty("closed", out JsonElement cl) && (cl.ValueKind == JsonValueKind.True);
        List<Point3d> points = ParsePoints(root);

        switch (kind)
        {
            case "point":
                if (points.Count < 1)
                {
                    error = "create_rhino_geometry 'point' requires one point in 'points'.";
                    return false;
                }

                placement = Placement.ForPoint(points[0], name);
                return true;

            case "line":
                if (points.Count < 2)
                {
                    error = "create_rhino_geometry 'line' requires two points in 'points'.";
                    return false;
                }

                placement = Placement.ForCurve("line", new LineCurve(points[0], points[1]), name);
                return true;

            case "polyline":
                if (points.Count < 2)
                {
                    error = "create_rhino_geometry 'polyline' requires at least two points in 'points'.";
                    return false;
                }

                var polyPts = new List<Point3d>(points);
                if (closed && polyPts[0].DistanceTo(polyPts[polyPts.Count - 1]) > RhinoMath.ZeroTolerance)
                {
                    polyPts.Add(polyPts[0]);
                }

                placement = Placement.ForCurve("polyline", new PolylineCurve(polyPts), name);
                return true;

            case "curve":
                if (points.Count < 2)
                {
                    error = "create_rhino_geometry 'curve' requires at least two points in 'points'.";
                    return false;
                }

                int degree = root.TryGetProperty("degree", out JsonElement d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out int deg)
                    ? Math.Max(1, deg)
                    : 3;
                var curvePts = new List<Point3d>(points);
                if (closed && curvePts[0].DistanceTo(curvePts[curvePts.Count - 1]) > RhinoMath.ZeroTolerance)
                {
                    curvePts.Add(curvePts[0]);
                }

                Curve? interpolated = Curve.CreateInterpolatedCurve(curvePts, degree);
                if (interpolated is null)
                {
                    // Fall back to a polyline through the points rather than failing the call.
                    interpolated = new PolylineCurve(curvePts);
                }

                placement = Placement.ForCurve("curve", interpolated, name);
                return true;

            case "circle":
                if (!(root.TryGetProperty("radius", out JsonElement r) && r.ValueKind == JsonValueKind.Number && r.TryGetDouble(out double radius)) || radius <= 0)
                {
                    error = "create_rhino_geometry 'circle' requires a positive 'radius'.";
                    return false;
                }

                Point3d center = ParsePoint(root, "center") ?? (points.Count > 0 ? points[0] : Point3d.Origin);
                var circle = new Circle(new Plane(center, Vector3d.ZAxis), radius);
                placement = Placement.ForCurve("circle", new ArcCurve(circle), name);
                return true;

            default:
                error = $"create_rhino_geometry does not support geometryType '{kind}'. Use point, line, polyline, curve, or circle.";
                return false;
        }
    }

    private static List<Point3d> ParsePoints(JsonElement root)
    {
        var result = new List<Point3d>();
        if (!root.TryGetProperty("points", out JsonElement pts) || pts.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement item in pts.EnumerateArray())
        {
            if (TryReadPoint(item, out Point3d p))
            {
                result.Add(p);
            }
        }

        return result;
    }

    private static Point3d? ParsePoint(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement el) && TryReadPoint(el, out Point3d p) ? p : null;

    private static bool TryReadPoint(JsonElement el, out Point3d point)
    {
        point = Point3d.Origin;
        if (el.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var coords = new List<double>(3);
        foreach (JsonElement c in el.EnumerateArray())
        {
            if (c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out double v))
            {
                coords.Add(v);
            }
        }

        if (coords.Count < 2)
        {
            return false;
        }

        point = new Point3d(coords[0], coords[1], coords.Count >= 3 ? coords[2] : 0.0);
        return true;
    }

    // One queued piece of geometry: either a point or a curve (all curve-like kinds are stored as a
    // single Curve), plus its display kind and optional name.
    private readonly record struct Placement(bool IsPoint, Point3d Point, Curve? Curve, string Kind, string Name)
    {
        public static Placement ForPoint(Point3d point, string name) =>
            new(true, point, null, "point", name);

        public static Placement ForCurve(string kind, Curve curve, string name) =>
            new(false, Point3d.Origin, curve, kind, name);
    }

    // A parameter this node placed on the canvas: its InstanceGuid, the unique reference name (its
    // nickname), and its geometry type. Persisted so references survive save/load.
    private readonly record struct PlacedInput(Guid ParamGuid, string Name, string TypeName);
}

/// <summary>
/// An input parameter already on the canvas that a generated graph can reference by name instead of
/// recreating: its unique name, geometry type, and the live parameter (whose output is the parameter
/// itself). Produced by <see cref="RhinoGeometryTool.CollectReferenceableInputs"/>.
/// </summary>
/// <param name="Name">The unique name the model references (the parameter's nickname).</param>
/// <param name="TypeName">The geometry type the input carries (e.g. <c>Curve</c>, <c>Point</c>).</param>
/// <param name="LiveOutput">The live parameter on the canvas (its own output is the geometry source).</param>
public sealed record ReferenceableInput(string Name, string TypeName, IGH_Param LiveOutput);
