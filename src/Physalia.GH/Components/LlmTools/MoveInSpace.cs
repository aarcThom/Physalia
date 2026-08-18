// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Rhino;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that gives the model a POSITION in space and lets it walk, one step at a
/// time, through a lattice of positions the user supplies. It advertises a <c>move_in_space</c> tool
/// (wire its Tool output into the LLM Call Tools input, its Signal input to a Router, and its Result
/// output through a Feedback component into a Feedback Collector and back to the Router Results
/// input). Each call reports where the model now stands and which single steps are available from
/// there, named as direction tokens — <c>forward</c>, <c>up_forward</c>, <c>down_left</c> — so the
/// model navigates by naming a direction rather than by inventing coordinates.
///
/// <para>The walk itself is published on the Traversed Points output, in visiting order, starting
/// with the Start Point. That output is the reason the tool exists: it turns the reasoning the model
/// does about a space into geometry the definition can build on (a polyline through the route, a set
/// of positions to populate, a circulation diagram).</para>
///
/// <para><b>Which positions count as one step is derived, not configured</b> — see
/// <see cref="SpaceNavigator"/>, which also fixes forwards to +Y and right to +X. Everything the
/// tool decides is pure geometry over the wired Positions; the component owns only the current
/// position and the history, both of which are session state that never persists (the walk restarts
/// from the Start Point whenever the file is reopened, the Start Point moves, or the outputs are
/// cleared). Walking is pure CPU work with no document access, so the tool runs synchronously inside
/// the dispatch solve.</para>
/// </summary>
public class MoveInSpace : LlmToolComponentBase
{
    private const int InStartPoint = 1;
    private const int InPositions = 2;
    private const int InPositionNotes = 3;

    // Fallback for the same-level / coincidence threshold when no Rhino document is reachable.
    private const double FallbackTolerance = 1e-6;

    private static readonly LlmToolDefinition ToolDef = new(
        "move_in_space",
        "Move yourself one step through a set of fixed positions in the Grasshopper document, and see "
        + "where you are. Call it with no arguments to report your current position and the steps "
        + "available from it without moving; call it with a direction to take that step. You may only "
        + "move to a position adjacent to the one you are on, and only in a direction the previous "
        + "result listed as available — every result ends with the list of tokens legal from where you "
        + "then stand, so read it before choosing the next move. Directions are FIXED WORLD "
        + "directions, not relative to any facing: forward is +Y, back is -Y, right is +X, left is -X, "
        + "up is +Z. A token combines an optional level change with an in-plane direction: \"forward\" "
        + "stays on the current level, \"up_forward\" goes one level up and forwards, \"down\" goes one "
        + "level straight down. Some positions carry a note describing what is there; when one does, it "
        + "is reported with the position. Use this to explore or route through a space — every "
        + "position you visit is recorded in order and handed back to the definition as geometry.",
        BuildSchema());

    // Session state: where the model stands, and every position it has stood on in order. Never
    // serialized — a reopened file restarts the walk at the Start Point, like the rest of the
    // signal lifecycle.
    private readonly List<Point3d> _traversed = new();

    private Point3d _start = Point3d.Origin;
    private bool _startRead;
    private List<Point3d> _positions = new();
    private List<string> _notes = new();
    private double _tolerance = FallbackTolerance;

    /// <summary>
    /// Initializes a new instance of the <see cref="MoveInSpace"/> class.
    /// </summary>
    public MoveInSpace()
        : base("Move In Space", "Move", "A tool the model calls to walk step by step through a set of positions, reporting where it is and where it can go next.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A4E17C63-8D25-4B9F-91A0-6C3E5D2B7F48");

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    // The walk, after the base Tool and Result outputs.
    private static int OutTraversed => FirstAdditionalOutputIndex;

    // Where the model stands, published on its own so nothing downstream has to index into the walk.
    private static int OutCurrentPosition => FirstAdditionalOutputIndex + 1;

    /// <summary>
    /// Gets the position the model currently occupies — the last position walked to, or the Start
    /// Point when nothing has moved yet.
    /// </summary>
    private Point3d Current => _traversed.Count > 0 ? _traversed[_traversed.Count - 1] : _start;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("Start Point", "SP", "The position the model starts from. It is the first entry of Traversed Points, and need not be one of the Positions. Moving it restarts the walk.", GH_ParamAccess.item, Point3d.Origin);
        pManager.AddPointParameter("Positions", "P", "Every position the model is allowed to occupy — the lattice it walks through. One call moves it to an adjacent position in this set; a position further along the same direction is reached by a later step.", GH_ParamAccess.list);
        pManager[InPositions].Optional = true;
        pManager.AddTextParameter("Position Notes", "PN", "Optional note per position, describing what is at it — reported to the model whenever it stands there. Paired with Positions by LONGEST-LIST matching: one note per position pairs 1:1, and a shorter list has its last note reused for the remaining positions. Leave unwired for no notes at all.", GH_ParamAccess.list);
        pManager[InPositionNotes].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Traversed Points", "TP", "Every position the model has occupied, in visiting order, starting with the Start Point. Session-only: cleared when the walk restarts.", GH_ParamAccess.list);
        pManager.AddPointParameter("Current Position", "CP", "Where the model stands right now — the last position walked to, or the Start Point before it has moved. The same as the last of Traversed Points, published on its own so a camera or a report can be wired straight to it.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the walk context once per solve so each dispatched call in the batch reuses it, and
    /// restarts the walk when the Start Point moves — the recorded route began at the old one, so
    /// keeping it would splice two unrelated walks together.
    /// </remarks>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        Point3d start = Point3d.Origin;
        da.GetData(InStartPoint, ref start);

        var positions = new List<Point3d>();
        da.GetDataList(InPositions, positions);
        _positions = positions;

        var notes = new List<string>();
        da.GetDataList(InPositionNotes, notes);
        _notes = notes;

        _tolerance = RhinoDoc.ActiveDoc is { } doc && doc.ModelAbsoluteTolerance > 0
            ? doc.ModelAbsoluteTolerance
            : FallbackTolerance;

        if (!_startRead || start.DistanceTo(_start) > _tolerance)
        {
            _startRead = true;
            _start = start;
            _traversed.Clear();
        }

        if (_traversed.Count == 0)
        {
            _traversed.Add(_start);
        }
    }

    /// <inheritdoc/>
    protected override ToolCallResult ExecuteCall(ToolCallContent call)
    {
        if (_positions.Count == 0)
        {
            return ToolCallResult.Error(
                "No positions are wired into the movement tool — the Positions input of the Move In Space "
                + "component is empty, so there is nowhere to move. Report this rather than retrying.");
        }

        string direction;
        try
        {
            direction = ReadDirection(call.InputJson);
        }
        catch (JsonException ex)
        {
            return ToolCallResult.Error($"move_in_space received invalid JSON arguments: {ex.Message}");
        }

        IReadOnlyList<MoveOption> options = SpaceNavigator.Options(Current, _positions, _tolerance);

        // No direction asked for: report the current position and what is reachable, without moving.
        if (direction.Length == 0)
        {
            return ToolCallResult.Ok(Report(options, moved: null));
        }

        if (!SpaceNavigator.AllTokens.Contains(direction))
        {
            return ToolCallResult.Error(
                $"\"{direction}\" is not a direction token. {Environment.NewLine}"
                + SpaceNavigator.DescribeOptions(options, _traversed, _tolerance));
        }

        MoveOption? chosen = null;
        foreach (MoveOption option in options)
        {
            if (string.Equals(option.Token, direction, StringComparison.Ordinal))
            {
                chosen = option;
                break;
            }
        }

        if (chosen is null)
        {
            // A real token, but nothing lies that way from here. An error so the model self-corrects
            // against the list rather than assuming the move happened.
            return ToolCallResult.Error(
                $"You cannot move {direction} from {SpaceNavigator.Format(Current)} — no position in the set "
                + $"lies that way. You have NOT moved.{Environment.NewLine}"
                + SpaceNavigator.DescribeOptions(options, _traversed, _tolerance));
        }

        MoveOption move = chosen.Value;
        _traversed.Add(move.Target);

        // Recomputed from the position just reached, so the result always ends with the moves legal
        // from where the model now stands rather than from where it was.
        IReadOnlyList<MoveOption> next = SpaceNavigator.Options(Current, _positions, _tolerance);
        return ToolCallResult.Ok(Report(next, move));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Published after the dispatched calls have run, so a move made this solve is on the wire in the
    /// same solve — and re-published on idle solves so the walk stays on the wire between moves.
    /// </remarks>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        da.SetDataList(OutTraversed, _traversed);
        da.SetData(OutCurrentPosition, Current);
    }

    /// <inheritdoc/>
    /// <remarks>Clearing the outputs restarts the walk from the Start Point.</remarks>
    protected override void OnCleared()
    {
        base.OnCleared();
        _traversed.Clear();
    }

    // The tool result: where the model stands, how it got there, how far it has walked, and every
    // step available from here. The closing instruction is deliberately part of every result — the
    // model chooses its next move from this text and nothing else.
    private string Report(IReadOnlyList<MoveOption> options, MoveOption? moved)
    {
        var sb = new StringBuilder();

        if (moved is { } move)
        {
            sb.AppendLine(
                $"Moved {move.Token} ({move.DirectionLabel}, "
                + $"{move.Distance.ToString("0.###", CultureInfo.InvariantCulture)} units).");
        }

        sb.AppendLine($"You are at {SpaceNavigator.Format(Current)}.");

        // The note describes the POSITION, so it is reported whenever the model is standing there —
        // on arrival and on a look alike. It is the user's text, passed through verbatim.
        string note = NoteFor(Current);
        if (StringHelpers.IsNonBlank(note))
        {
            sb.AppendLine($"What is here: {note.Trim()}");
        }

        sb.AppendLine(
            $"Walk so far: {_traversed.Count} position(s) recorded, {DistinctVisited()} of them distinct, "
            + $"out of {_positions.Count} position(s) in the space.");
        sb.AppendLine();
        sb.AppendLine(SpaceNavigator.DescribeOptions(options, _traversed, _tolerance));

        if (options.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                "Call move_in_space again with one of those tokens to take the step. Call it with no "
                + "direction to see this again without moving.");
        }

        return sb.ToString().TrimEnd();
    }

    // The note for a position, or blank when there are none or this position is not in the set (the
    // Start Point need not be). Notes pair with Positions by LONGEST-LIST matching, Grasshopper's own
    // rule: a note per position pairs 1:1, and a shorter note list has its LAST entry reused for every
    // remaining position. Done here rather than by component-level data matching, because this
    // component reads both as whole lists within one solve and must not iterate.
    private string NoteFor(Point3d position)
    {
        if (_notes.Count == 0)
        {
            return string.Empty;
        }

        for (int i = 0; i < _positions.Count; i++)
        {
            if (_positions[i].DistanceTo(position) <= _tolerance)
            {
                return ListPairing.MatchLongest(_notes, i) ?? string.Empty;
            }
        }

        return string.Empty;
    }

    // How many of the walked positions are distinct, so a revisit reads as a revisit. Compared by
    // tolerance rather than by equality, matching how the navigator decides two positions are one.
    private int DistinctVisited()
    {
        var distinct = new List<Point3d>();
        foreach (Point3d point in _traversed)
        {
            if (!distinct.Any(earlier => earlier.DistanceTo(point) <= _tolerance))
            {
                distinct.Add(point);
            }
        }

        return distinct.Count;
    }

    // The direction argument, or an empty string when the model asked only to look around. Anything
    // that is not a usable string reads as "look" rather than failing: a model probing the tool with
    // no arguments should get its bearings back, not an error.
    private static string ReadDirection(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return string.Empty;
        }

        using JsonDocument doc = JsonDocument.Parse(inputJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("direction", out JsonElement element)
            || element.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return (element.GetString() ?? string.Empty).Trim().ToLowerInvariant();
    }

    // The argument schema, with the direction enum generated from the navigator so the advertised
    // tokens and the accepted tokens can never drift apart.
    private static string BuildSchema()
    {
        string tokens = string.Join(",", SpaceNavigator.AllTokens.Select(token => $"\"{token}\""));
        return "{\"type\":\"object\",\"properties\":{"
            + "\"direction\":{\"type\":\"string\",\"enum\":[" + tokens + "],"
            + "\"description\":\"The single step to take, in fixed world directions (forward=+Y, back=-Y, "
            + "right=+X, left=-X, up=+Z). An 'up_' or 'down_' prefix changes level as well; bare 'up' and "
            + "'down' move straight up or down. Must be one of the tokens the previous result listed as "
            + "available. Omit to report your position and options without moving.\"}"
            + "},\"required\":[]}";
    }
}
