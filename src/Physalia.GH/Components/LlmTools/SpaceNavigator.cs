// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// The vertical band a candidate position sits in relative to the position being moved from.
/// </summary>
public enum LevelBand
{
    /// <summary>Same height, within tolerance.</summary>
    Same,

    /// <summary>Higher than the current position.</summary>
    Up,

    /// <summary>Lower than the current position.</summary>
    Down,
}

/// <summary>
/// One move the model may make from its current position: the token it names in the tool call, the
/// human label the option is listed under, and where the move lands.
/// </summary>
/// <param name="Token">The direction value the model passes to take this move.</param>
/// <param name="Band">Which vertical band the target sits in.</param>
/// <param name="DirectionLabel">Human wording for the in-plane part (forwards, straight up).</param>
/// <param name="Target">The position this move lands on.</param>
/// <param name="Distance">Straight-line distance from the current position to the target.</param>
public readonly record struct MoveOption(
    string Token,
    LevelBand Band,
    string DirectionLabel,
    Point3d Target,
    double Distance);

/// <summary>
/// Pure movement reasoning for the Move In Space tool: given a current position and the set of
/// positions the model is allowed to occupy, works out which of them count as ONE STEP away and
/// names each step with a direction token.
///
/// <para><b>Frame of reference is the world, not a heading.</b> Forwards is +Y, backwards −Y, right
/// +X, left −X, up +Z. The tool deliberately has no notion of which way the model is facing: a
/// heading would be undefined on the first move, would have to be tracked by the model across
/// turns, and would flip the meaning of left and right as it turned — all of which models handle
/// badly. Fixed world directions stay true no matter how long the walk gets, and every reported
/// option carries its absolute coordinates so the model can reason spatially either way.</para>
///
/// <para><b>Adjacency is derived, never configured.</b> Each candidate is bucketed by its vertical
/// band (same / up / down, by tolerance) and by its in-plane bearing, which falls into one of eight
/// 45° cones — full coverage with no gaps, so no reachable neighbour is ever hidden and no candidate
/// has to be axis-aligned to be offered. Within a bucket only the CLOSEST candidate is offered,
/// which is what makes the step a step: positions further along the same bearing become reachable
/// one move later, and "up a level" resolves to the next level up rather than to the top of the
/// building — without ever having to cluster the positions into levels.</para>
/// </summary>
public static class SpaceNavigator
{
    /// <summary>
    /// The eight in-plane cones, ordered by bearing from +Y (forwards) turning toward +X (right),
    /// 45° apart: the token fragment and the human wording for each.
    /// </summary>
    private static readonly (string Token, string Label)[] Compass =
    {
        ("forward", "forwards"),
        ("forward_right", "forwards-right"),
        ("right", "right"),
        ("back_right", "backwards-right"),
        ("back", "backwards"),
        ("back_left", "backwards-left"),
        ("left", "left"),
        ("forward_left", "forwards-left"),
    };

    // Token prefix and human wording per vertical band. Same-level moves carry no prefix, so their
    // tokens read as the bare direction ("forward") — the common case, and the shortest to emit.
    private static readonly (LevelBand Band, string Prefix, string Heading)[] Bands =
    {
        (LevelBand.Same, string.Empty, "Current level"),
        (LevelBand.Up, "up_", "Up a level"),
        (LevelBand.Down, "down_", "Down a level"),
    };

    /// <summary>
    /// Gets every direction token the tool accepts, in the order they are advertised: the eight
    /// same-level moves, then the nine up moves (a straight vertical plus the eight bearings), then
    /// the nine down moves.
    /// </summary>
    public static IReadOnlyList<string> AllTokens { get; } = BuildAllTokens();

    /// <summary>
    /// Finds every position reachable in one step from the given position.
    /// </summary>
    /// <param name="from">The position being moved from.</param>
    /// <param name="positions">Every position the model is allowed to occupy.</param>
    /// <param name="tolerance">
    /// Distance below which two coordinates count as the same; also the threshold that separates a
    /// same-level candidate from an up or down one.
    /// </param>
    /// <returns>The available moves, ordered the way AllTokens orders them.</returns>
    public static IReadOnlyList<MoveOption> Options(Point3d from, IEnumerable<Point3d> positions, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(positions);

        // Closest candidate per bucket. The bucket key IS the direction token, so a token can never
        // resolve to two positions and the choice the model makes is always unambiguous.
        var best = new Dictionary<string, MoveOption>(StringComparer.Ordinal);

        foreach (Point3d candidate in positions)
        {
            double distance = from.DistanceTo(candidate);
            if (distance <= tolerance)
            {
                // The position the model is already standing on, or a duplicate of it.
                continue;
            }

            double dz = candidate.Z - from.Z;
            LevelBand band = Math.Abs(dz) <= tolerance ? LevelBand.Same
                : dz > 0 ? LevelBand.Up
                : LevelBand.Down;

            double dx = candidate.X - from.X;
            double dy = candidate.Y - from.Y;
            bool vertical = Math.Sqrt((dx * dx) + (dy * dy)) <= tolerance;

            if (vertical && band == LevelBand.Same)
            {
                // Displacement too small to name in any axis, yet outside the coincidence test
                // above. Nothing meaningful to offer.
                continue;
            }

            string token;
            string label;
            if (vertical)
            {
                token = band == LevelBand.Up ? "up" : "down";
                label = band == LevelBand.Up ? "straight up" : "straight down";
            }
            else
            {
                (string cone, string coneLabel) = Compass[ConeIndex(dx, dy)];
                token = Bands.First(b => b.Band == band).Prefix + cone;
                label = coneLabel;
            }

            var option = new MoveOption(token, band, label, candidate, distance);
            if (!best.TryGetValue(token, out MoveOption existing) || option.Distance < existing.Distance)
            {
                best[token] = option;
            }
        }

        // Advertised order, so the same situation always reads the same way to the model.
        return AllTokens.Where(best.ContainsKey).Select(token => best[token]).ToList();
    }

    /// <summary>
    /// Renders the available moves as the listing the model reads: grouped by vertical band, with the
    /// target coordinates and step length beside each token, and already-visited targets flagged so
    /// the model can tell exploring from retracing.
    /// </summary>
    /// <param name="options">The available moves, from Options.</param>
    /// <param name="visited">Positions already traversed, used only to annotate the listing.</param>
    /// <param name="tolerance">Distance below which a target counts as an already-visited position.</param>
    /// <returns>The listing, or a line saying the walk has dead-ended when there are no moves.</returns>
    public static string DescribeOptions(IReadOnlyList<MoveOption> options, IEnumerable<Point3d> visited, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(visited);

        if (options.Count == 0)
        {
            return "No moves are available from here — no position in the set is adjacent to this one. "
                + "This is a dead end: report it rather than retrying.";
        }

        var seen = visited.ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Available moves ({options.Count}) — pass one of these direction tokens:");

        foreach ((LevelBand band, _, string heading) in Bands)
        {
            var inBand = options.Where(option => option.Band == band).ToList();
            if (inBand.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"  {heading}:");
            foreach (MoveOption option in inBand)
            {
                bool wasVisited = seen.Any(point => point.DistanceTo(option.Target) <= tolerance);
                sb.AppendLine(
                    $"    {option.Token,-18} {option.DirectionLabel,-16} -> {Format(option.Target)}"
                    + $"  ({option.Distance.ToString("0.###", CultureInfo.InvariantCulture)} away)"
                    + (wasVisited ? "  [already visited]" : string.Empty));
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The unit direction for an aim given as a compass azimuth and an elevation, in degrees.
    ///
    /// <para>This lives here, beside the cone table it is the inverse of, because the Take Snapshot
    /// tool aims a camera by azimuth while this class names movement by cone — and the two only agree
    /// if they share ONE bearing convention. Keeping both against the same table means the model can
    /// carry a single mental compass between walking and looking, and that a change to the convention
    /// cannot move one tool without the other.</para>
    /// </summary>
    /// <param name="azimuthDegrees">Bearing from +Y turning toward +X: 0 forwards, 90 right.</param>
    /// <param name="elevationDegrees">Tilt: 0 level, positive up, negative down.</param>
    /// <returns>The unit direction vector.</returns>
    public static Vector3d Aim(double azimuthDegrees, double elevationDegrees)
    {
        double azimuth = azimuthDegrees * Math.PI / 180.0;
        double elevation = elevationDegrees * Math.PI / 180.0;
        double horizontal = Math.Cos(elevation);

        return new Vector3d(
            Math.Sin(azimuth) * horizontal,
            Math.Cos(azimuth) * horizontal,
            Math.Sin(elevation));
    }

    /// <summary>
    /// The nearest of the eight compass words for an azimuth, so a camera aim can be echoed back in
    /// the same vocabulary the movement options are listed in.
    /// </summary>
    /// <param name="azimuthDegrees">Bearing from +Y turning toward +X.</param>
    /// <returns>The direction label, e.g. "forwards-right".</returns>
    public static string BearingLabel(double azimuthDegrees)
    {
        double degrees = azimuthDegrees % 360.0;
        if (degrees < 0)
        {
            degrees += 360.0;
        }

        return Compass[(int)Math.Round(degrees / 45.0) % 8].Label;
    }

    /// <summary>
    /// Formats a position the way every line of the tool result reports one, so the model sees a
    /// single coordinate style throughout.
    /// </summary>
    /// <param name="point">The position to format.</param>
    /// <returns>The position as (x, y, z).</returns>
    public static string Format(Point3d point) => string.Format(
        CultureInfo.InvariantCulture,
        "({0:0.###}, {1:0.###}, {2:0.###})",
        point.X,
        point.Y,
        point.Z);

    // Which of the eight 45° cones an in-plane displacement falls into. Bearing is measured from +Y
    // (forwards) turning toward +X (right) — atan2(dx, dy), not the usual atan2(dy, dx) — so cone 0
    // is centred on forwards and the cones run clockwise in plan, matching the Compass table order.
    private static int ConeIndex(double dx, double dy)
    {
        double degrees = Math.Atan2(dx, dy) * 180.0 / Math.PI;
        if (degrees < 0)
        {
            degrees += 360.0;
        }

        return (int)Math.Round(degrees / 45.0) % 8;
    }

    private static IReadOnlyList<string> BuildAllTokens()
    {
        var tokens = new List<string>();
        foreach ((LevelBand band, string prefix, _) in Bands)
        {
            if (band != LevelBand.Same)
            {
                // The straight vertical leads its band: no in-plane component to name.
                tokens.Add(band == LevelBand.Up ? "up" : "down");
            }

            tokens.AddRange(Compass.Select(cone => prefix + cone.Token));
        }

        return tokens;
    }
}
