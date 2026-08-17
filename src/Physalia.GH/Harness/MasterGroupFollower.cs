// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Physalia.GH.Components;
using Physalia.GH.Generation;
using GHGroupObject = Grasshopper.Kernel.Special.GH_Group;

namespace Physalia.GH.Harness;

/// <summary>
/// Keeps a harness proxy and the master "Physalia &lt;id&gt;" group its pipeline writes into moving as
/// one thing on the canvas, in BOTH directions: drag the group and the proxy follows, drag the proxy
/// and the group follows. Each harness is paired with ITS OWN group — that is what the id in the name
/// is for — so a canvas carrying several group-grounded pipelines moves each pair independently.
///
/// <para>The proxy never becomes a member of the group. Membership is the obvious way to get this and
/// the wrong one, because the group is the model's frame: its members are what the group-scoped
/// grounding exports and what the model may target with a group op, so the pipeline has to stay
/// outside it while still reading as attached. The proxy's outlet wires and the placement point they
/// end on need no work of their own: the point is stored as an offset from the proxy.</para>
///
/// <para>Grasshopper raises no event for an object being moved — <see cref="GH_Document"/> announces
/// added, deleted and solved, never moved — and a group is moved by moving its MEMBERS, since a
/// group's own bounds are merely their union. So movement is noticed by re-measuring pivots on a
/// throttled idle pass. A group counts as having moved only when EVERY member still present moved by
/// the SAME non-zero delta: that is a rigid drag, where one member moving on its own (or a placement
/// landing, which adds members without moving any) is not.</para>
///
/// <para>Each direction skips anything that has already travelled the same delta, and anything that is
/// <see cref="IGH_Attributes.Selected"/> — Grasshopper is dragging the whole selection, so following as
/// well would double the delta and send the object twice as far as the thing it is following.</para>
///
/// <para><b>What stops the two directions feeding each other</b> is that a pair which was carried is
/// re-measured before its anchors are stored. Anchors left as measured BEFORE the carry would make the
/// side just moved look like a fresh user drag on the next pass, the opposite direction would answer
/// it, and the pair would accelerate off the canvas together. Selection alone does not save it: the
/// loop closes as soon as the user deselects.</para>
///
/// <para>Undo needs no special handling, for the same reason: Grasshopper's record covers only the side
/// the user dragged, but restoring it IS a rigid move, so the pass after the undo carries the other side
/// back.</para>
/// </summary>
internal static class MasterGroupFollower
{
    // Pivots are floats and a rigid drag translates them all by the same amount, but only up to
    // rounding: two members of one drag can disagree in the last fraction of a unit.
    private const float MoveEpsilon = 0.5f;

    // How often pivots are re-measured. A drag emits mouse-move messages continuously and Rhino goes
    // idle between them, so this is the granularity at which the followed side tracks a drag in
    // progress: one frame at 60Hz, which is as fine as the canvas can show. Anything longer reads as
    // the followed side lagging behind the cursor; anything shorter is work between two paints of the
    // same picture. It is a ceiling on the poll rate, not a timer — one pass is a single sweep of the
    // document plus a walk of each group's members, and idle can fire far more often than this.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(16);

    // Transmitters that want the watch, by InstanceGuid. Held by id rather than by reference so
    // nothing here can keep a removed component alive, and so re-adding the same one (a document
    // being handed around, a harness re-opened) cannot count twice.
    private static readonly HashSet<Guid> Followers = new();

    // Where each attached pair was when it was last measured, keyed by the harness's instance id:
    // every harness has its OWN master group ("Physalia <id>"), so anchors cannot be shared across a
    // document. Session only; nothing about following is persisted.
    private static readonly Dictionary<Guid, Anchors> PairAnchors = new();

    // Which document the anchors were measured on. Weak, so a document the user closed mid-gesture is
    // collected normally rather than pinned by this watch.
    private static WeakReference<GH_Document>? _anchorDocument;

    private static DateTime _lastCheckUtc = DateTime.MinValue;
    private static bool _hooked;

    /// <summary>
    /// Starts watching for a master group or an attached proxy being dragged, on behalf of a
    /// transmitter that could be attached to one. Idempotent — the watch is one process-wide idle
    /// handler however many transmitters ask for it.
    /// </summary>
    /// <param name="transmitter">The transmitter that wants the watch.</param>
    internal static void Follow(ComponentTransmitter transmitter)
    {
        Followers.Add(transmitter.InstanceGuid);

        if (!_hooked)
        {
            _hooked = true;
            Rhino.RhinoApp.Idle += OnIdle;
        }
    }

    /// <summary>
    /// Drops a transmitter's interest in the watch, releasing the idle handler once the last one is
    /// gone.
    /// </summary>
    /// <param name="transmitter">The transmitter being removed from its document.</param>
    internal static void Unfollow(ComponentTransmitter transmitter)
    {
        Followers.Remove(transmitter.InstanceGuid);

        if (Followers.Count == 0 && _hooked)
        {
            _hooked = false;
            Rhino.RhinoApp.Idle -= OnIdle;
            Forget();
        }
    }

    // Re-measures every harness-and-group pair on the canvas and carries whichever side of a pair did
    // not move. Cheap in the common case: a canvas with no harness costs one type-filtered sweep, and
    // the attachment test (which reads the pipeline inside a harness) runs only once something in that
    // pair has actually moved.
    private static void OnIdle(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastCheckUtc < CheckInterval)
        {
            return;
        }

        _lastCheckUtc = now;

        GH_Document? host = PhyDocuments.ActiveHost();
        if (host is null)
        {
            Forget();
            return;
        }

        // One sweep of the canvas: resolving a guid through GH_Document.FindObject walks the object
        // list, so doing that per group member per pass would be quadratic in canvas size — which is
        // what made a per-frame poll look too expensive. Group membership is expanded against this
        // index instead.
        var index = new Dictionary<Guid, IGH_DocumentObject>();
        var harnesses = new List<HarnessComponent>();
        foreach (IGH_DocumentObject obj in host.Objects)
        {
            if (obj is null)
            {
                continue;
            }

            index[obj.InstanceGuid] = obj;
            if (obj is HarnessComponent harness)
            {
                harnesses.Add(harness);
            }
        }

        bool sameDocument = IsAnchorDocument(host);
        var live = new HashSet<Guid>();

        foreach (HarnessComponent harness in harnesses)
        {
            // Each harness answers only to the group carrying its own id; a harness with none is not
            // a pair at all, and its stale anchors go with it.
            if (GhJsonBridge.FindMasterGroup(host, harness) is not { } master)
            {
                continue;
            }

            live.Add(harness.InstanceGuid);
            Pair pair = Measure(harness, master, index);

            if (sameDocument && PairAnchors.TryGetValue(harness.InstanceGuid, out Anchors? anchors))
            {
                if (Carry(pair, anchors, index))
                {
                    pair = Measure(harness, master, index);
                }
            }

            PairAnchors[harness.InstanceGuid] = new Anchors(pair.Members, pair.Proxy);
        }

        // Anchors for pairs that are gone (harness deleted, group deleted or renamed away) would
        // otherwise sit here for the session and, worse, be compared against if one came back.
        foreach (Guid stale in PairAnchors.Keys.Where(id => !live.Contains(id)).ToList())
        {
            PairAnchors.Remove(stale);
        }

        _anchorDocument = new WeakReference<GH_Document>(host);
    }

    // Carries one side of a pair, and says whether anything was moved.
    private static bool Carry(Pair pair, Anchors anchors, Dictionary<Guid, IGH_DocumentObject> index)
    {
        if (RigidDelta(pair.Members, anchors.Members) is { } groupDelta)
        {
            return CarryProxy(pair, anchors, groupDelta);
        }

        var proxyDelta = new SizeF(pair.Proxy.X - anchors.Proxy.X, pair.Proxy.Y - anchors.Proxy.Y);
        return HasMoved(proxyDelta) && CarryGroup(pair, anchors, index, proxyDelta);
    }

    // Moves the proxy by the delta its group just travelled.
    private static bool CarryProxy(Pair pair, Anchors anchors, SizeF delta)
    {
        // Already travelled this delta = it moved with the same gesture; Selected = Grasshopper is
        // dragging it as part of the selection. Following either would double the move.
        if (SameDelta(new SizeF(pair.Proxy.X - anchors.Proxy.X, pair.Proxy.Y - anchors.Proxy.Y), delta)
            || pair.Harness.Attributes is not { Selected: false } attributes
            || !WritesIntoGroup(pair.Harness))
        {
            return false;
        }

        Translate(attributes, delta);
        Redraw();
        return true;
    }

    // Moves the group by moving its members, which is the only way a group moves — its box is their
    // union. The group objects themselves (the master and any nested group) are re-laid out rather
    // than moved, so the boxes close back around the members in their new place.
    private static bool CarryGroup(
        Pair pair, Anchors anchors, Dictionary<Guid, IGH_DocumentObject> index, SizeF delta)
    {
        if (!WritesIntoGroup(pair.Harness))
        {
            return false;
        }

        bool moved = false;
        foreach (KeyValuePair<Guid, PointF> member in pair.Members)
        {
            if (AlreadyMoved(member.Key, pair.Members, anchors.Members, delta)
                || !index.TryGetValue(member.Key, out IGH_DocumentObject? obj)
                || obj.Attributes is not { Selected: false } attributes)
            {
                continue;
            }

            Translate(attributes, delta);
            moved = true;
        }

        if (!moved)
        {
            return false;
        }

        foreach (GHGroupObject group in pair.Groups)
        {
            group.ExpireCaches();
            group.Attributes?.ExpireLayout();
        }

        Redraw();
        return true;
    }

    // Measures one pair: the pivots of the group's members (nested groups expanded), the group objects
    // whose boxes have to be re-laid out when those members move, and the proxy's own pivot.
    //
    // The group objects are collected rather than measured, because a group's pivot is derived from its
    // contents and would report movement its members never made.
    private static Pair Measure(
        HarnessComponent harness, GHGroupObject master, Dictionary<Guid, IGH_DocumentObject> index)
    {
        var members = new Dictionary<Guid, PointF>();
        var groups = new List<GHGroupObject> { master };
        var queue = new Queue<Guid>(master.ObjectIDs ?? Enumerable.Empty<Guid>());
        var seen = new HashSet<Guid>();

        while (queue.Count > 0)
        {
            Guid guid = queue.Dequeue();
            if (!seen.Add(guid) || !index.TryGetValue(guid, out IGH_DocumentObject? member))
            {
                continue;
            }

            if (member is GHGroupObject nested)
            {
                groups.Add(nested);
                foreach (Guid inner in nested.ObjectIDs ?? Enumerable.Empty<Guid>())
                {
                    queue.Enqueue(inner);
                }

                continue;
            }

            if (member.Attributes is { } attributes)
            {
                members[guid] = attributes.Pivot;
            }
        }

        return new Pair(harness, harness.Attributes?.Pivot ?? default, members, groups);
    }

    // The one delta every measured object moved by since the last pass, or null when this was not a
    // rigid move of all of them: nothing moved, only some moved, or the ones that can be compared do
    // not agree. Objects added or removed in between are ignored rather than disqualifying — a
    // placement landing inside the group must not read as the group moving.
    private static SizeF? RigidDelta(
        Dictionary<Guid, PointF> pivots, Dictionary<Guid, PointF> anchors)
    {
        SizeF? shared = null;

        foreach (KeyValuePair<Guid, PointF> pivot in pivots)
        {
            if (!anchors.TryGetValue(pivot.Key, out PointF was))
            {
                continue;
            }

            var delta = new SizeF(pivot.Value.X - was.X, pivot.Value.Y - was.Y);
            if (shared is not { } first)
            {
                shared = delta;
                continue;
            }

            if (!SameDelta(delta, first))
            {
                return null;
            }
        }

        return shared is { } moved && HasMoved(moved) ? moved : null;
    }

    // True when this object has itself moved by the delta being followed — it travelled with the same
    // gesture (the user dragged it as part of the selection), so carrying it would double the move.
    private static bool AlreadyMoved(
        Guid guid,
        Dictionary<Guid, PointF> pivots,
        Dictionary<Guid, PointF> anchors,
        SizeF delta) =>
        pivots.TryGetValue(guid, out PointF now)
        && anchors.TryGetValue(guid, out PointF was)
        && SameDelta(new SizeF(now.X - was.X, now.Y - was.Y), delta);

    // True when the harness holds a Component Transmitter whose pipeline reads the canvas through its
    // master group — the same condition that creates the group when the transmitter's arrow is
    // dropped. A harness grounded on the whole canvas has no particular relationship to any group and
    // neither moves one nor follows one.
    private static bool WritesIntoGroup(HarnessComponent harness) =>
        harness.Outlets.OfType<ComponentTransmitter>().Any(t => t.UsesGroupScopedGrounding());

    private static void Translate(IGH_Attributes attributes, SizeF delta)
    {
        attributes.Pivot = new PointF(attributes.Pivot.X + delta.Width, attributes.Pivot.Y + delta.Height);
        attributes.ExpireLayout();
        attributes.PerformLayout();
    }

    private static void Redraw() =>

        // A repaint, and nothing else: the SET of attributes has not changed, so the document's
        // attribute cache is still valid — dropping it mid-drag would only make Grasshopper rebuild
        // the hit-test list on every one of these passes.
        Instances.ActiveCanvas?.Refresh();

    private static bool SameDelta(SizeF a, SizeF b) =>
        Math.Abs(a.Width - b.Width) <= MoveEpsilon && Math.Abs(a.Height - b.Height) <= MoveEpsilon;

    private static bool HasMoved(SizeF delta) =>
        Math.Abs(delta.Width) > MoveEpsilon || Math.Abs(delta.Height) > MoveEpsilon;

    // True when the anchors currently held were measured on this document.
    private static bool IsAnchorDocument(GH_Document host) =>
        _anchorDocument is { } weak
        && weak.TryGetTarget(out GH_Document? anchored)
        && ReferenceEquals(anchored, host);

    private static void Forget()
    {
        PairAnchors.Clear();
        _anchorDocument = null;
    }

    /// <summary>
    /// One pass's measurement of a harness and its group.
    /// </summary>
    /// <param name="Harness">The harness proxy, one half of the pair.</param>
    /// <param name="Proxy">The proxy's pivot.</param>
    /// <param name="Members">Pivots of the group's members, nested groups expanded.</param>
    /// <param name="Groups">The master group and any group nested inside it.</param>
    private sealed record Pair(
        HarnessComponent Harness,
        PointF Proxy,
        Dictionary<Guid, PointF> Members,
        List<GHGroupObject> Groups);

    /// <summary>
    /// Where a pair was when it was last measured.
    /// </summary>
    /// <param name="Members">Member pivots at that point.</param>
    /// <param name="Proxy">The proxy's pivot at that point.</param>
    private sealed record Anchors(Dictionary<Guid, PointF> Members, PointF Proxy);
}
