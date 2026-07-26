// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using GHGroupObject = Grasshopper.Kernel.Special.GH_Group;

namespace Physalia.GH.Generation;

/// <summary>
/// Post-placement overlap repair for model-authored layouts.
///
/// <para>The model authors pivots and Physalia honours them, but a pivot is a POINT and the model
/// cannot know how large anything renders: Grasshopper sizes a component from its nickname text and
/// parameter count, so a Number Slider told to be called "Main Block Width" comes out over 200 units
/// wide, and a multi-line documentation Panel is well over 100 tall. No amount of prompt guidance
/// fixes that — the 2026-07-25 23:46 session spaced stages 150 apart in X (sliders overlapped the
/// next stage) and functional areas 20-40 apart in Y (each area's tall Panel pushed its group box
/// into the area above), and both readings of the guidance were reasonable.</para>
///
/// <para>So the sizes are measured here, once the objects are live, and overlaps are nudged out
/// minimally. This deliberately does NOT re-layout: each pass moves the LATER of an overlapping pair
/// along whichever axis needs the smaller push, leaving the leftmost/topmost node anchored, so the
/// model's reading order and column structure survive. Two levels, because they need different
/// clearances: components within a functional area, then whole areas against each other.</para>
/// </summary>
internal static partial class GhJsonBridge
{
    // Minimum clear space between two components inside one functional area.
    private const float ComponentClearance = 14f;

    // Minimum clear space between two functional areas' boxes. Grasshopper draws a group box a few
    // units outside its members, so this is measured member-union to member-union and has to exceed
    // that margin on both sides to read as a gutter.
    private const float AreaClearance = 55f;

    // Margin Grasshopper leaves around a group's members when it draws the box.
    private const float GroupBoxMargin = 10f;

    // Safety bound on the separation sweeps. Each pass strictly reduces overlap, and real graphs
    // settle in a handful; the cap only stops a pathological arrangement from spinning.
    private const int MaxSeparationPasses = 24;

    /// <summary>
    /// A movable body in the separation model: its current rectangle and the objects that move with
    /// it. One body is a single component at the within-area level and a whole functional area at the
    /// area level.
    /// </summary>
    private sealed class LayoutBody
    {
        public LayoutBody(RectangleF rect, IReadOnlyList<Guid> members)
        {
            this.Rect = rect;
            this.Members = members;
        }

        public RectangleF Rect { get; private set; }

        public IReadOnlyList<Guid> Members { get; }

        public float Dx { get; private set; }

        public float Dy { get; private set; }

        public void Nudge(float dx, float dy)
        {
            this.Dx += dx;
            this.Dy += dy;
            this.Rect = new RectangleF(this.Rect.X + dx, this.Rect.Y + dy, this.Rect.Width, this.Rect.Height);
        }
    }

    /// <summary>
    /// A placed object's measured geometry, captured once before anything moves: its live pivot, the
    /// size Grasshopper rendered it at, and where its bounds sit relative to the pivot (mid-left for
    /// an ordinary component, but not for every object type). Rectangles are reconstructed from these
    /// plus an accumulated offset, so the whole separation runs on measurements taken while the
    /// layout was valid — reading <c>Attributes.Bounds</c> again after an <c>ExpireLayout</c> would
    /// return whatever Grasshopper has not recomputed yet.
    /// </summary>
    /// <param name="Obj">The live object.</param>
    /// <param name="Pivot">Its pivot when measured.</param>
    /// <param name="Offset">Bounds top-left minus pivot.</param>
    /// <param name="Size">Its rendered size.</param>
    private sealed record PlacedGeometry(IGH_DocumentObject Obj, PointF Pivot, SizeF Offset, SizeF Size);

    /// <summary>
    /// Nudges apart every overlapping pair among the components this session's model placed, using
    /// their real rendered bounds. Components inside one group are separated first, then the groups
    /// themselves (and any ungrouped model components) are separated as rigid bodies, so an area's
    /// internal spacing is fixed before the areas are packed apart. Components the user placed are
    /// never moved.
    /// </summary>
    /// <param name="doc">The document to repair; null is a no-op.</param>
    /// <param name="gainedMembers">
    /// Objects a patch has just added to existing areas. Any area containing one is rebuilt from its
    /// wires whether or not it currently overlaps: inserting a node into a data flow changes which
    /// column everything downstream belongs in, and only a rebuild can shift those existing
    /// components right to make room. Null (the full-graph path) leaves a clean authored layout alone.
    /// </param>
    /// <returns>How many objects ended up moved.</returns>
    internal static int SeparatePlacedOverlaps(
        GH_Document? doc,
        IReadOnlyCollection<Guid>? gainedMembers = null)
    {
        if (doc is null)
        {
            return 0;
        }

        // Measure everything once, while the layout Grasshopper computed is still valid.
        var measured = new Dictionary<Guid, PlacedGeometry>();
        foreach (Guid guid in ModelPlacedGuids(doc))
        {
            if (doc.FindObject(guid, false) is { } obj && HasUsableBounds(obj))
            {
                RectangleF bounds = obj.Attributes.Bounds;
                PointF pivot = obj.Attributes.Pivot;
                measured[guid] = new PlacedGeometry(
                    obj,
                    pivot,
                    new SizeF(bounds.X - pivot.X, bounds.Y - pivot.Y),
                    bounds.Size);
            }
        }

        if (measured.Count == 0)
        {
            return 0;
        }

        // Accumulated translation per object; every rectangle below is derived from the measurements
        // plus this, so no stale bounds can enter the model.
        var offsets = new Dictionary<Guid, PointF>();
        RectangleF RectOf(Guid guid)
        {
            PlacedGeometry g = measured[guid];
            PointF d = offsets.TryGetValue(guid, out PointF o) ? o : default;
            return new RectangleF(
                g.Pivot.X + g.Offset.Width + d.X,
                g.Pivot.Y + g.Offset.Height + d.Y,
                g.Size.Width,
                g.Size.Height);
        }

        void Accumulate(IEnumerable<LayoutBody> bodies)
        {
            foreach (LayoutBody body in bodies)
            {
                if (Math.Abs(body.Dx) < 0.5f && Math.Abs(body.Dy) < 0.5f)
                {
                    continue;
                }

                foreach (Guid guid in body.Members)
                {
                    PointF current = offsets.TryGetValue(guid, out PointF o) ? o : default;
                    offsets[guid] = new PointF(current.X + body.Dx, current.Y + body.Dy);
                }
            }
        }

        // Group membership drives both levels. A component in several groups is treated as belonging
        // to the first, so it is never moved twice by the area-level pass.
        var byGroup = new List<List<Guid>>();
        var grouped = new HashSet<Guid>();
        foreach (GHGroupObject group in doc.Objects.OfType<GHGroupObject>())
        {
            var members = new List<Guid>();
            foreach (Guid guid in group.ObjectIDs ?? Enumerable.Empty<Guid>())
            {
                if (measured.ContainsKey(guid) && grouped.Add(guid))
                {
                    members.Add(guid);
                }
            }

            if (members.Count > 0)
            {
                byGroup.Add(members);
            }
        }

        // ---- Level 1: components within each functional area.
        //
        // A pivot-nudge can only ever reach "not overlapping" — it cannot produce the shape a
        // Grasshopper user expects (one column per data-flow stage, sources left, terminal right).
        // So an area whose authored layout actually collides is REBUILT from its wires at measured
        // sizes; an area the model laid out cleanly is left exactly as authored, and only nudged if
        // something marginal remains. Same safety-net principle as the whole-graph relayout, applied
        // per area.
        var rebuiltAreas = new List<string>();
        foreach (List<Guid> members in byGroup)
        {
            // An area that just gained a component is rebuilt unconditionally. A new node belongs in
            // the data-flow COLUMN its wires put it in, which means everything downstream has to move
            // right to make room — and the staged row below the area (all a positioning pass can do
            // before the wires exist) is not that. Leaving the new nodes there is what produced the
            // detached blocks inside the Roof and Portico areas in the 2026-07-26 00:44 session.
            bool gained = gainedMembers is { Count: > 0 } && members.Any(gainedMembers.Contains);
            bool overlapping = !gained && AreaHasOverlap(members, RectOf);
            bool fragmented = !gained && !overlapping && AreaIsFragmented(members, RectOf);
            if (gained || overlapping || fragmented)
            {
                string why = gained ? "gained members" : overlapping ? "overlap" : "fragmented";
                rebuiltAreas.Add($"{members.Count} member(s) [{why}]");
                RelayoutArea(members, measured, doc, RectOf, offsets);
            }

            List<LayoutBody> bodies = members
                .Select(m => new LayoutBody(RectOf(m), new[] { m }))
                .ToList();
            Separate(bodies, ComponentClearance);
            Accumulate(bodies);
        }

        // ---- Level 2: whole areas, plus each ungrouped model component as its own body.
        var areaBodies = new List<LayoutBody>();
        foreach (List<Guid> members in byGroup)
        {
            RectangleF union = RectangleF.Empty;
            foreach (Guid guid in members)
            {
                RectangleF r = RectOf(guid);
                union = union.IsEmpty ? r : RectangleF.Union(union, r);
            }

            if (!union.IsEmpty)
            {
                // Grow by the margin Grasshopper draws the box with, so the gutter is between the
                // visible boxes rather than between the members inside them.
                areaBodies.Add(new LayoutBody(
                    RectangleF.Inflate(union, GroupBoxMargin, GroupBoxMargin),
                    members));
            }
        }

        foreach (Guid guid in measured.Keys.Where(g => !grouped.Contains(g)))
        {
            areaBodies.Add(new LayoutBody(RectOf(guid), new[] { guid }));
        }

        Separate(areaBodies, AreaClearance);
        Accumulate(areaBodies);

        // ---- Write the accumulated translations onto the live pivots, once.
        var moved = new HashSet<Guid>();
        foreach ((Guid guid, PointF delta) in offsets)
        {
            if ((Math.Abs(delta.X) < 0.5f && Math.Abs(delta.Y) < 0.5f)
                || measured[guid].Obj.Attributes is not { } attr)
            {
                continue;
            }

            attr.Pivot = new PointF(
                MathF.Round(measured[guid].Pivot.X + delta.X),
                MathF.Round(measured[guid].Pivot.Y + delta.Y));
            attr.ExpireLayout();
            moved.Add(guid);
        }

        if (moved.Count == 0)
        {
            return 0;
        }

        // Group boxes track their members, so expiring the groups too keeps the drawn boxes honest.
        foreach (GHGroupObject group in doc.Objects.OfType<GHGroupObject>())
        {
            group.ExpireCaches();
            group.Attributes?.ExpireLayout();
        }

        doc.DestroyAttributeCache();
        Grasshopper.Instances.ActiveCanvas?.Refresh();

        // Layout is the one part of a placement whose result is invisible after the fact: neither the
        // chat transcript nor the signal trace records a pivot, so three rounds of layout regressions
        // had to be diagnosed by measuring screenshots. Say what this pass decided, so the next one
        // can be read instead of inferred.
        float maxShift = offsets.Values.Count == 0
            ? 0f
            : offsets.Values.Max(d => Math.Max(Math.Abs(d.X), Math.Abs(d.Y)));
        Rhino.RhinoApp.WriteLine(
            $"[Physalia] Layout repair: {moved.Count} of {measured.Count} placed object(s) moved, "
            + $"largest shift {maxShift:F0} units; {rebuiltAreas.Count} of {byGroup.Count} area(s) rebuilt from wires"
            + (rebuiltAreas.Count > 0 ? " — " + string.Join(", ", rebuiltAreas) : string.Empty) + ".");

        return moved.Count;
    }

    // Horizontal gap between one data-flow stage's column and the next, and vertical gap between two
    // components stacked within a column, when an area is rebuilt from its wires.
    private const float StageGap = 70f;
    private const float RowGap = 30f;

    // Gap beyond which a member counts as detached from the rest of its area. Comfortably above the
    // widest legitimate stage gap, well below the distance a stranded component ends up at.
    private const float FragmentationGap = 400f;

    /// <summary>
    /// True when an area's members form more than one spatial cluster — some member sits detached
    /// from the rest rather than in the block its group box is meant to enclose.
    /// <para>This is the case a nudge can never repair: positioning happens when a component is
    /// ADDED, so a <c>groups.modify</c> arriving in a LATER patch changes membership without moving
    /// anything. The component keeps whatever position the general anchor gave it while it was
    /// homeless, and the group box simply stretches out to swallow it — which is exactly what the
    /// 2026-07-26 00:26 session ended up with after its first groups.modify failed and the retry
    /// landed a round later. Rebuilding the area from its wires pulls the stray into its column.</para>
    /// </summary>
    /// <param name="members">The area's members.</param>
    /// <param name="rectOf">Current rectangle of a member.</param>
    /// <returns>True when the area is not spatially contiguous.</returns>
    private static bool AreaIsFragmented(IReadOnlyList<Guid> members, Func<Guid, RectangleF> rectOf)
    {
        if (members.Count < 2)
        {
            return false;
        }

        // Single-linkage grow from the first member: anything within the gap joins the cluster.
        var cluster = new HashSet<Guid> { members[0] };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (Guid candidate in members)
            {
                if (cluster.Contains(candidate))
                {
                    continue;
                }

                RectangleF probe = RectangleF.Inflate(rectOf(candidate), FragmentationGap, FragmentationGap);
                if (cluster.Any(inside => probe.IntersectsWith(rectOf(inside))))
                {
                    cluster.Add(candidate);
                    grew = true;
                }
            }
        }

        return cluster.Count < members.Count;
    }

    // True when any two members of an area collide at the component clearance — the trigger for
    // rebuilding that area rather than nudging it.
    private static bool AreaHasOverlap(IReadOnlyList<Guid> members, Func<Guid, RectangleF> rectOf)
    {
        for (int i = 0; i < members.Count; i++)
        {
            for (int j = i + 1; j < members.Count; j++)
            {
                RectangleF a = RectangleF.Inflate(rectOf(members[i]), ComponentClearance / 2f, ComponentClearance / 2f);
                if (a.IntersectsWith(rectOf(members[j])))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuilds one functional area's internal layout from the wires between its members, using the
    /// sizes Grasshopper actually rendered: one column per data-flow stage (a component's stage is
    /// the longest wire path reaching it, so sources share the leftmost column and the terminal sits
    /// last), each column only as wide as its widest member, and members stacked down a column in the
    /// vertical order the model gave them — so its row intent survives even though the coordinates do
    /// not. Unwired members (a documentation Panel) are lifted above the area as captions. The area
    /// keeps its existing top-left corner; packing areas apart is the caller's next step.
    /// </summary>
    /// <param name="members">The area's members.</param>
    /// <param name="measured">Measured geometry for every placed object.</param>
    /// <param name="doc">The live document, read for the wiring between members.</param>
    /// <param name="rectOf">Current rectangle of a member (measurement plus accumulated offset).</param>
    /// <param name="offsets">Accumulated translations, updated for every member moved.</param>
    private static void RelayoutArea(
        List<Guid> members,
        Dictionary<Guid, PlacedGeometry> measured,
        GH_Document doc,
        Func<Guid, RectangleF> rectOf,
        Dictionary<Guid, PointF> offsets)
    {
        var inArea = new HashSet<Guid>(members);

        // Wires between members, read off the live params: source owner -> consumer.
        var incoming = new Dictionary<Guid, List<Guid>>();
        foreach (Guid guid in members)
        {
            incoming[guid] = new List<Guid>();
        }

        foreach (Guid guid in members)
        {
            foreach (IGH_Param input in InputParamsOf(measured[guid].Obj))
            {
                foreach (IGH_Param source in input.Sources ?? Enumerable.Empty<IGH_Param>())
                {
                    if (OwnerGuidOf(source, doc) is Guid owner && owner != guid && inArea.Contains(owner))
                    {
                        incoming[guid].Add(owner);
                    }
                }
            }
        }

        // Stage = longest path from a source. Relaxed iteratively and bounded by the member count, so
        // a cycle (or a wire pattern that looks like one) cannot spin here.
        var stage = members.ToDictionary(m => m, _ => 0);
        for (int pass = 0; pass < members.Count; pass++)
        {
            bool changed = false;
            foreach (Guid guid in members)
            {
                foreach (Guid from in incoming[guid])
                {
                    if (stage[from] + 1 > stage[guid])
                    {
                        stage[guid] = stage[from] + 1;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        // A member no wire touches is an annotation, not a stage.
        bool Consumed(Guid guid) => members.Any(other => other != guid && incoming[other].Contains(guid));
        bool IsWired(Guid guid) => incoming[guid].Count > 0 || Consumed(guid);

        List<Guid> wired = members.Where(IsWired).ToList();
        List<Guid> annotations = members.Where(m => !IsWired(m)).ToList();

        // Terminals go in the LAST column. Longest-path staging alone can seat the component that IS
        // the area's result ahead of a longer side chain (a Domain Box two hops from a slider, while a
        // Division → Negative → Construct Point chain runs three), which reads as the result being
        // computed early. Anything the area does not consume is an output of the area, so it belongs
        // at the right edge.
        if (wired.Count > 0)
        {
            int lastStage = wired.Max(m => stage[m]);
            foreach (Guid guid in wired.Where(m => !Consumed(m)))
            {
                stage[guid] = lastStage;
            }
        }

        // Anchor on the area's current top-left so it stays where the model put it.
        RectangleF area = RectangleF.Empty;
        foreach (Guid guid in members)
        {
            RectangleF r = rectOf(guid);
            area = area.IsEmpty ? r : RectangleF.Union(area, r);
        }

        void MoveTo(Guid guid, float x, float y)
        {
            RectangleF now = rectOf(guid);
            PointF current = offsets.TryGetValue(guid, out PointF o) ? o : default;
            offsets[guid] = new PointF(current.X + (x - now.X), current.Y + (y - now.Y));
        }

        float cursorX = area.Left;
        foreach (IGrouping<int, Guid> column in wired
            .GroupBy(m => stage[m])
            .OrderBy(g => g.Key))
        {
            // Preserve the model's vertical ordering within the stage.
            List<Guid> ordered = column.OrderBy(m => rectOf(m).Top).ToList();
            float cursorY = area.Top;
            float widest = 0f;

            foreach (Guid guid in ordered)
            {
                RectangleF r = rectOf(guid);
                MoveTo(guid, cursorX, cursorY);
                cursorY += r.Height + RowGap;
                widest = Math.Max(widest, r.Width);
            }

            cursorX += widest + StageGap;
        }

        // Captions above the rebuilt area, at its left edge, stacked upward.
        float captionY = area.Top;
        foreach (Guid guid in annotations)
        {
            RectangleF r = rectOf(guid);
            captionY -= r.Height + RowGap;
            MoveTo(guid, area.Left, captionY);
        }
    }

    // A document object's input params, or nothing for objects that have none (sliders, panels).
    private static IEnumerable<IGH_Param> InputParamsOf(IGH_DocumentObject obj) => obj switch
    {
        IGH_Component component => component.Params.Input,
        IGH_Param param => new[] { param },
        _ => Enumerable.Empty<IGH_Param>(),
    };

    // The document object a param belongs to: its component, or the param itself when floating.
    private static Guid? OwnerGuidOf(IGH_Param param, GH_Document doc)
    {
        if (param.Attributes?.GetTopLevel?.DocObject is { } owner)
        {
            return owner.InstanceGuid;
        }

        return doc.FindObject(param.InstanceGuid, false)?.InstanceGuid;
    }

    /// <summary>
    /// Pushes overlapping bodies apart until every pair clears <paramref name="clearance"/>, or the
    /// pass cap is reached. Each fix moves only the later body of the pair — further right when the
    /// horizontal overlap is the smaller one, further down otherwise — so the arrangement drifts as
    /// little as possible from what the model authored.
    /// </summary>
    /// <param name="bodies">The bodies to separate, mutated in place.</param>
    /// <param name="clearance">Minimum clear space to leave between any two bodies.</param>
    private static void Separate(List<LayoutBody> bodies, float clearance)
    {
        if (bodies.Count < 2)
        {
            return;
        }

        for (int pass = 0; pass < MaxSeparationPasses; pass++)
        {
            bool anyMoved = false;

            // Deterministic order: left-to-right, top-to-bottom, so the same input always settles
            // the same way and the anchor of each pair is the one that reads first.
            List<LayoutBody> ordered = bodies
                .OrderBy(b => b.Rect.X)
                .ThenBy(b => b.Rect.Y)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    LayoutBody a = ordered[i];
                    LayoutBody b = ordered[j];

                    RectangleF ra = RectangleF.Inflate(a.Rect, clearance / 2f, clearance / 2f);
                    RectangleF rb = RectangleF.Inflate(b.Rect, clearance / 2f, clearance / 2f);
                    if (!ra.IntersectsWith(rb))
                    {
                        continue;
                    }

                    float overlapX = Math.Min(ra.Right, rb.Right) - Math.Max(ra.Left, rb.Left);
                    float overlapY = Math.Min(ra.Bottom, rb.Bottom) - Math.Max(ra.Top, rb.Top);
                    if (overlapX <= 0 || overlapY <= 0)
                    {
                        continue;
                    }

                    // Which axis to separate along is decided by how the model ARRANGED the pair, not
                    // by which overlap is smaller. Two components in the same row have a small
                    // vertical overlap and a large horizontal one, so "smallest push" would shove one
                    // of them below the other — and cascading that through a stage turns a wide,
                    // row-aligned graph into a tall column with wires crossing back over themselves
                    // (exactly what happened to the Main Block area's three Construct Domains).
                    // Comparing centre offsets keeps the authored topology: a pair placed side by
                    // side spreads further apart sideways, a pair stacked spreads further down.
                    float centreDx = (rb.Left + (rb.Width / 2f)) - (ra.Left + (ra.Width / 2f));
                    float centreDy = (rb.Top + (rb.Height / 2f)) - (ra.Top + (ra.Height / 2f));

                    // b reads after a in the ordering above, so b is the one that yields.
                    if (Math.Abs(centreDx) >= Math.Abs(centreDy))
                    {
                        b.Nudge(overlapX, 0f);
                    }
                    else
                    {
                        b.Nudge(0f, overlapY);
                    }

                    anyMoved = true;
                }
            }

            if (!anyMoved)
            {
                return;
            }
        }
    }

    // A group box is not a separable member, and an object whose layout has never run has no size
    // to reason about.
    private static bool HasUsableBounds(IGH_DocumentObject obj) =>
        obj is not GHGroupObject
        && obj.Attributes is { } attr
        && attr.Bounds.Width > 0
        && attr.Bounds.Height > 0;
}
