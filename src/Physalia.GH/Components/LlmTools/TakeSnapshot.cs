// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Generation;
using Rhino;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that lets the model LOOK. Standing at the wired Current Location, it
/// poses a Rhino camera with a human lens length, points it in any direction over the full sphere,
/// captures the frame, and hands the image back to the model in the same turn as the tool result —
/// so the model sees the space it is reasoning about instead of inferring it from coordinates. Pair it
/// with Move In Space: that tool walks, this one looks.
///
/// <para>Every direction snapped is published on the Snapshot Directions output as a TREE, one branch
/// per visit to a Current Location, so several looks from one spot land on one branch and the branch
/// order follows the walk. Wire the last of Move In Space's Traversed Points into Current Location and
/// the two outputs line up.</para>
///
/// <para><b>Why this tool is asynchronous</b> even though a capture is fast: posing the camera mutates
/// viewport state, which cannot be done inside a Grasshopper solution, and must happen on the UI
/// thread. So the base's async path lets the dispatch solve finish, and the capture is marshalled onto
/// <c>RhinoApp.Idle</c> — the same deferral Geometry Observation uses — with the result awaited from
/// the background batch. The user's own viewport is borrowed and put back exactly
/// (<see cref="ViewportSnapshot.TryCaptureFromCamera"/>).</para>
///
/// <para><b>How the image reaches the model.</b> A tool result is text on every provider, so the image
/// cannot travel inside it. It travels as an ATTACHMENT: the call returns the text result plus an
/// <see cref="ImageContent"/> block, the Router forwards attachments after the tool results, and the
/// Conversation Log records them in that one answering user turn. Anthropic renders tool_result and
/// image blocks side by side, the OpenAI protocol splits them into role:tool plus role:user messages,
/// and Gemini emits functionResponse and inlineData parts together.</para>
/// </summary>
public class TakeSnapshot : LlmToolComponentBase
{
    private const int InCurrentLocation = 1;

    /// <summary>
    /// 35mm-equivalent lens length, in millimetres. A 35mm lens spans roughly 54° horizontally, which
    /// is about the angle a person takes in without turning their head, and it is the standard choice
    /// for interior and architectural photography. A 50mm "normal" lens is the other common answer to
    /// "human", but indoors it crops so tightly that the model would see a wall rather than a room.
    /// The tool description states the resulting field of view, because knowing what is OUTSIDE the
    /// frame is what stops the model concluding a thing is absent when it is merely off-camera.
    /// </summary>
    private const double HumanLensMm = 35.0;

    /// <summary>
    /// How long to wait for the idle capture before giving up. Generous, because it is only reached if
    /// Rhino never goes idle; the model gets a plain error rather than a tool round that hangs forever
    /// with its id unanswered.
    /// </summary>
    private const int CaptureTimeoutMs = 30_000;

    private static readonly LlmToolDefinition ToolDef = new(
        "take_snapshot",
        "Look at the Rhino model from where you are standing, and get back a photograph of what you "
        + "see. The camera stands at your current location and you choose which way it faces, over the "
        + "full 360°. Directions match the movement tool: azimuth 0 = forwards (+Y), 90 = right (+X), "
        + "180 = backwards (-Y), 270 = left (-X), increasing clockwise seen from above. Elevation tilts "
        + "the camera up or down, from -90 (straight down at the floor) through 0 (level, the default) "
        + "to +90 (straight up). The lens is a 35mm-equivalent — about a 54° horizontal field of view, "
        + "roughly what a person takes in without turning their head — so anything outside that cone is "
        + "NOT in the picture: if you expected something and cannot see it, turn and look again before "
        + "concluding it is missing. Call this repeatedly to look around from one spot.",
        "{\"type\":\"object\",\"properties\":{"
        + "\"azimuth\":{\"type\":\"number\",\"minimum\":0,\"maximum\":360,\"description\":\"Compass direction to face, in degrees: 0 = forwards (+Y), 90 = right (+X), 180 = backwards (-Y), 270 = left (-X).\"},"
        + "\"elevation\":{\"type\":\"number\",\"minimum\":-90,\"maximum\":90,\"description\":\"Tilt in degrees: 0 = level (default), positive looks up, negative looks down. -90 is straight down, +90 straight up.\"}"
        + "},\"required\":[\"azimuth\"]}");

    // One entry per VISIT to a location, in visit order — the branches of the output tree. A visit is
    // a contiguous run of solves with the same Current Location, so returning to a point later opens a
    // NEW branch: that keeps branch order equal to walk order, which a caller can always collapse by
    // point downstream but could never recover if it were merged here.
    private readonly List<Visit> _visits = new();

    // The visit list is read on the solve thread and appended to from the idle capture. Those cannot
    // overlap in practice (Grasshopper solves on the UI thread, and Idle only fires between solves),
    // but the tool is the only place that invariant would be load-bearing, so it is not relied on.
    private readonly object _gate = new();

    private Point3d _location = Point3d.Origin;
    private bool _locationRead;

    /// <summary>
    /// Initializes a new instance of the <see cref="TakeSnapshot"/> class.
    /// </summary>
    public TakeSnapshot()
        : base("Take Snapshot", "Snap", "A tool the model calls to photograph the Rhino model from its current location, facing any direction it chooses.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6E2B94A7-51D3-4C68-8F1A-2D7C0E5B9A34");

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    /// <remarks>
    /// Posing a camera mutates viewport state, which is illegal inside a solution and must happen on
    /// the UI thread — so the capture is deferred to <c>RhinoApp.Idle</c> and awaited off the solve.
    /// </remarks>
    protected override bool RunsAsync => true;

    // The directions snapped, after the base Tool and Result outputs.
    private static int OutDirections => FirstAdditionalOutputIndex;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("Current Location", "CL", "Where the camera stands when the model looks. Wire the model's current position — the last of Move In Space's Traversed Points — so looking and walking agree. Each new value opens a new branch on Snapshot Directions.", GH_ParamAccess.item, Point3d.Origin);
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddVectorParameter("Snapshot Directions", "SD", "Unit view direction of every snapshot taken, as a tree: one branch per visit to a Current Location, in visit order, each branch holding that visit's looks in the order they were taken. Session-only.", GH_ParamAccess.tree);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the camera position once per solve. A changed position opens a new branch rather than
    /// resetting: the tree is the record of the whole walk, not of the current stop.
    /// </remarks>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        Point3d location = Point3d.Origin;
        da.GetData(InCurrentLocation, ref location);

        double tolerance = RhinoDoc.ActiveDoc is { } doc && doc.ModelAbsoluteTolerance > 0
            ? doc.ModelAbsoluteTolerance
            : RhinoMath.ZeroTolerance;

        if (!_locationRead || location.DistanceTo(_location) > tolerance)
        {
            _locationRead = true;
            _location = location;
        }
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        double azimuth;
        double elevation;
        try
        {
            if (!TryReadAim(call.InputJson, out azimuth, out elevation, out string aimError))
            {
                return ToolCallResult.Error(aimError);
            }
        }
        catch (JsonException ex)
        {
            return ToolCallResult.Error($"take_snapshot received invalid JSON arguments: {ex.Message}");
        }

        Point3d location = _location;
        Vector3d direction = SpaceNavigator.Aim(azimuth, elevation);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CaptureTimeoutMs);

        byte[]? imageBytes;
        string? error;
        try
        {
            (imageBytes, error) = await CaptureOnIdleAsync(location, direction, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolCallResult.Error(
                "The snapshot timed out waiting for Rhino to become idle — nothing was captured. "
                + "Rhino may be mid-command or mid-render; report this rather than retrying immediately.");
        }

        if (imageBytes is null)
        {
            return ToolCallResult.Error(error ?? "The snapshot failed for an unknown reason.");
        }

        int index = Record(location, direction);

        var attachments = new List<MessageContent>
        {
            new ImageContent(new InlineImage(imageBytes, "image/png")),
        };

        return ToolCallResult.OkWith(Report(location, direction, azimuth, elevation, index), attachments);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Published after the calls, so a snapshot taken this solve is on the wire in the same solve, and
    /// re-published on idle solves so the tree stays on the wire between looks.
    /// </remarks>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        var tree = new GH_Structure<GH_Vector>();

        lock (_gate)
        {
            for (int branch = 0; branch < _visits.Count; branch++)
            {
                var path = new GH_Path(branch);

                // An empty branch is meaningful — the model stood here and took nothing — so it is
                // published rather than skipped, keeping branch index equal to visit index.
                tree.EnsurePath(path);
                foreach (Vector3d direction in _visits[branch].Directions)
                {
                    tree.Append(new GH_Vector(direction), path);
                }
            }
        }

        da.SetDataTree(OutDirections, tree);
    }

    /// <inheritdoc/>
    /// <remarks>Clearing the outputs forgets every snapshot taken.</remarks>
    protected override void OnCleared()
    {
        base.OnCleared();
        lock (_gate)
        {
            _visits.Clear();
        }
    }

    // Records one look and returns its 1-based index within the current visit, for the report.
    private int Record(Point3d location, Vector3d direction)
    {
        lock (_gate)
        {
            if (_visits.Count == 0 || _visits[_visits.Count - 1].Location != location)
            {
                _visits.Add(new Visit(location));
            }

            List<Vector3d> directions = _visits[_visits.Count - 1].Directions;
            directions.Add(direction);
            return directions.Count;
        }
    }

    // Captures on the next RhinoApp.Idle: the UI thread, and outside any solution — both required to
    // pose a viewport. One-shot, and unhooked on every exit path so a cancelled call leaves no handler
    // behind to fire against a dead component.
    private static Task<(byte[]? Bytes, string? Error)> CaptureOnIdleAsync(
        Point3d location,
        Vector3d direction,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(byte[]? Bytes, string? Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler? handler = null;
        CancellationTokenRegistration registration = default;

        handler = (_, _) =>
        {
            RhinoApp.Idle -= handler;
            registration.Dispose();

            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }

            bool ok = ViewportSnapshot.TryCaptureFromCamera(
                location,
                direction,
                HumanLensMm,
                out byte[]? bytes,
                out string? error);

            tcs.TrySetResult(ok ? (bytes, null) : (null, error));
        };

        registration = ct.Register(() =>
        {
            RhinoApp.Idle -= handler;
            tcs.TrySetCanceled(ct);
        });

        RhinoApp.Idle += handler;
        return tcs.Task;
    }

    // The tool result text. The image itself arrives as a sibling block in this same turn, so the text
    // says where the camera stood and where it pointed — the things a picture cannot state.
    private string Report(Point3d location, Vector3d direction, double azimuth, double elevation, int index)
    {
        int visits;
        int total;
        lock (_gate)
        {
            visits = _visits.Count;
            total = 0;
            foreach (Visit visit in _visits)
            {
                total += visit.Directions.Count;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            $"Snapshot taken from {Fmt(location)} facing azimuth {Num(azimuth)}° ({SpaceNavigator.BearingLabel(azimuth)}), "
            + $"elevation {Num(elevation)}°.");
        sb.AppendLine(
            $"View direction {Fmt(direction)}; 35mm-equivalent lens, about a 54° horizontal field of view.");
        sb.AppendLine(
            $"This is look {index} from this location; {total} snapshot(s) taken across {visits} location(s) so far.");
        sb.AppendLine();
        sb.AppendLine(
            "The image follows in this same message. Anything outside the 54° cone is not in it — turn "
            + "to another azimuth and look again rather than assuming an absence.");

        return sb.ToString().TrimEnd();
    }

    private static bool TryReadAim(string inputJson, out double azimuth, out double elevation, out string error)
    {
        azimuth = 0;
        elevation = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(inputJson))
        {
            error = "take_snapshot requires an 'azimuth' in degrees (0 = forwards/+Y, 90 = right/+X, 180 = backwards, 270 = left).";
            return false;
        }

        using JsonDocument doc = JsonDocument.Parse(inputJson);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "take_snapshot arguments must be a JSON object.";
            return false;
        }

        if (!root.TryGetProperty("azimuth", out JsonElement az)
            || az.ValueKind != JsonValueKind.Number
            || !az.TryGetDouble(out azimuth))
        {
            error = "take_snapshot requires a numeric 'azimuth' in degrees (0 = forwards/+Y, 90 = right/+X, 180 = backwards, 270 = left).";
            return false;
        }

        // Wrapped rather than rejected: 450 and -90 are both unambiguous ways of saying 90 and 270,
        // and refusing them would cost a round trip to correct something we can read perfectly well.
        azimuth = ((azimuth % 360.0) + 360.0) % 360.0;

        if (root.TryGetProperty("elevation", out JsonElement el) && el.ValueKind == JsonValueKind.Number)
        {
            if (!el.TryGetDouble(out elevation))
            {
                error = "take_snapshot could not read 'elevation' as a number.";
                return false;
            }

            // Clamped, not wrapped: past vertical the camera is upside down rather than aimed
            // somewhere else, so the nearest legal aim is what the model meant.
            elevation = Math.Max(-90.0, Math.Min(90.0, elevation));
        }

        return true;
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Fmt(Point3d point) => string.Format(
        CultureInfo.InvariantCulture,
        "({0:0.###}, {1:0.###}, {2:0.###})",
        point.X,
        point.Y,
        point.Z);

    private static string Fmt(Vector3d vector) => string.Format(
        CultureInfo.InvariantCulture,
        "({0:0.###}, {1:0.###}, {2:0.###})",
        vector.X,
        vector.Y,
        vector.Z);

    // One stop on the walk and the looks taken from it.
    private sealed class Visit
    {
        public Visit(Point3d location) => Location = location;

        public Point3d Location { get; }

        public List<Vector3d> Directions { get; } = new();
    }
}
