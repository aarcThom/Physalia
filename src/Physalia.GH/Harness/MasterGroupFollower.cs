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
/// Keeps a harness proxy and the master "Physalia" group its pipeline writes into moving as one
/// thing on the canvas, in BOTH directions: drag the group and the proxy follows, drag the proxy and
/// the group follows. The proxy never becomes a member of that group — membership is the obvious way
/// to get this and the wrong one, because the group is the model's frame: its members are what the
/// group-scoped grounding exports and what the model may target with a group op, so the pipeline has
/// to stay outside it while still reading as attached. The proxy's outlet wires and the placement
/// point they end on need no work of their own: the point is stored as an offset from the proxy.
///
/// <para>Grasshopper raises no event for an object being moved — <see cref="GH_Document"/> announces
/// added, deleted and solved, never moved — and a group is moved by moving its MEMBERS, since a
/// group's own bounds are merely their union. So movement is noticed by re-measuring pivots on a
/// throttled idle pass. The group counts as having moved only when EVERY member still present moved
/// by the SAME non-zero delta: that is a rigid drag, where one member moving on its own (or a
/// placement landing, which adds members without moving any) is not.</para>
///
/// <para>Each direction skips anything that has already travelled the same delta, and anything that is
/// <see cref="IGH_Attributes.Selected"/> — Grasshopper is dragging the whole selection, so following as
/// well would double the delta and send the object twice as far as the thing it is following.</para>
///
/// <para><b>What stops the two directions feeding each other</b> is that a pass which carried
/// something re-measures before storing its anchors. Anchors left as measured BEFORE the carry would
/// make the side just moved look like a fresh user drag on the next pass, the opposite direction would
/// answer it, and the pair would accelerate off the canvas together. Selection alone does not save it:
/// the loop closes as soon as the user deselects.</para>
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
    // document (see <see cref="Measure"/>) and idle can fire far more often than this.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(16);

    // Transmitters that want the watch, by InstanceGuid. Held by id rather than by reference so
    // nothing here can keep a removed component alive, and so re-adding the same one (a document
    // being handed around, a harness re-opened) cannot count twice.
    private static readonly HashSet<Guid> Followers = new();

    // Where the group's members, and the document's harness proxies, were when they were last
    // measured. Session only; nothing about following is persisted.
    private static readonly Dictionary<Guid, PointF> MemberAnchors = new();
    private static readonly Dictionary<Guid, PointF> ProxyAnchors = new();

    // Which document the anchors were measured on. Weak, so a document the user closed mid-gesture is
    // collected normally rather than pinned by this watch.
    private static WeakReference<GH_Document>? _anchorDocument;

    private static DateTime _lastCheckUtc = DateTime.MinValue;
    private static bool _hooked;

    /// <summary>
    /// Starts watching for the master group or an attached proxy being dragged, on behalf of a
    /// transmitter that could be attached to the group. Idempotent — the watch is one process-wide
    /// idle handler however many transmitters ask for it.
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

    // Re-measures both sides and carries whichever one did not move. Cheap in the common case: no
    // master group on the canvas the user is looking at costs one type-filtered scan and nothing else,
    // and the attachment test (which reads the pipeline inside a harness) runs only once something has
    // actually moved.
    private static void OnIdle(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastCheckUtc < CheckInterval)
        {
            return;
        }

        _lastCheckUtc = now;

        GH_Document? host = PhyDocuments.ActiveHost();
        if (host is null || GhJsonBridge.FindMasterGroup(host) is not { } master)
        {
            Forget();
            return;
        }

        Snapshot snapshot = Measure(host, master);

        bool carried = false;
        if (IsAnchorDocument(host))
        {
            if (RigidDelta(snapshot.Members, MemberAnchors) is { } groupDelta)
            {
                carried = CarryProxies(snapshot, groupDelta);
            }
            else if (DraggedProxyDelta(snapshot) is { } proxyDelta)
            {
                carried = CarryGroup(snapshot, proxyDelta);
            }
        }

        // Re-measure after carrying, so the anchors describe where everything ACTUALLY ended up. Left
        // as measured, the side this pass just moved would look like a fresh user drag on the next one,
        // each direction would feed the other, and the pair would accelerate off the canvas.
        Rebase(host, carried ? Measure(host, master) : snapshot);
    }

    // One sweep of the canvas, measuring both sides at once.
    //
    // Nothing here calls GH_Document.FindObject: resolving a guid that way walks the document's object
    // list, so doing it per group member on every pass is quadratic in the size of the canvas — which
    // is what made a fine-grained poll look too expensive to run. The sweep indexes the objects once
    // instead, and the group's membership is expanded against that index.
    private static Snapshot Measure(GH_Document host, GHGroupObject master)
    {
        var index = new Dictionary<Guid, IGH_DocumentObject>();
        var proxies = new Dictionary<Guid, PointF>();

        foreach (IGH_DocumentObject obj in host.Objects)
        {
            if (obj is null)
            {
                continue;
            }

            index[obj.InstanceGuid] = obj;

            if (obj is HarnessComponent && obj.Attributes is { } proxyAttributes)
            {
                // Every proxy, not just the attached ones: the attachment test reads the pipeline
                // inside the harness, and that is not work for a per-frame pass. It is applied later,
                // once something has moved.
                proxies[obj.InstanceGuid] = proxyAttributes.Pivot;
            }
        }

        // Members, nested groups expanded. The group objects themselves are collected separately
        // rather than measured: a group's pivot is derived from its contents, so it would report
        // movement its members did not make.
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

        return new Snapshot(index, members, proxies, groups);
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

    // How far an attached harness proxy has just been dragged, or null when none has. The first one
    // found wins: two proxies moving different ways in one pass is not a gesture a user can make, and
    // the group can only follow one of them.
    private static SizeF? DraggedProxyDelta(Snapshot snapshot)
    {
        foreach (KeyValuePair<Guid, PointF> proxy in snapshot.Proxies)
        {
            if (!ProxyAnchors.TryGetValue(proxy.Key, out PointF was))
            {
                continue;
            }

            var delta = new SizeF(proxy.Value.X - was.X, proxy.Value.Y - was.Y);
            if (HasMoved(delta)
                && snapshot.Index.TryGetValue(proxy.Key, out IGH_DocumentObject? obj)
                && obj is HarnessComponent harness
                && WritesIntoGroup(harness))
            {
                return delta;
            }
        }

        return null;
    }

    // Translates every harness proxy attached to the group by the group's own delta.
    private static bool CarryProxies(Snapshot snapshot, SizeF delta)
    {
        bool moved = false;
        foreach (Guid guid in snapshot.Proxies.Keys)
        {
            if (AlreadyMoved(guid, snapshot.Proxies, ProxyAnchors, delta)
                || !snapshot.Index.TryGetValue(guid, out IGH_DocumentObject? obj)
                || obj is not HarnessComponent harness
                || harness.Attributes is not { Selected: false } attributes
                || !WritesIntoGroup(harness))
            {
                continue;
            }

            Translate(attributes, delta);
            moved = true;
        }

        Redraw(moved);
        return moved;
    }

    // Translates the group by translating its members, which is the only way a group moves — its box
    // is their union. The group objects themselves (the master and any nested group) are re-laid out
    // rather than moved, so the boxes close back around the members in their new place.
    private static bool CarryGroup(Snapshot snapshot, SizeF delta)
    {
        bool moved = false;
        foreach (Guid guid in snapshot.Members.Keys)
        {
            // Same two guards as the other direction: a member that has already travelled this delta
            // moved with the gesture, and a selected one is being dragged by Grasshopper along with the
            // proxy — moving either again would double its delta.
            if (AlreadyMoved(guid, snapshot.Members, MemberAnchors, delta)
                || !snapshot.Index.TryGetValue(guid, out IGH_DocumentObject? member)
                || member.Attributes is not { Selected: false } attributes)
            {
                continue;
            }

            Translate(attributes, delta);
            moved = true;
        }

        foreach (GHGroupObject group in snapshot.Groups)
        {
            group.ExpireCaches();
            group.Attributes?.ExpireLayout();
        }

        Redraw(moved);
        return moved;
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

    // True when the harness holds a Component Transmitter whose pipeline reads the canvas through
    // the master group — the same condition that creates the group when the transmitter's arrow is
    // dropped. A harness grounded on the whole canvas has no particular relationship to the group and
    // neither moves it nor follows it.
    private static bool WritesIntoGroup(HarnessComponent harness) =>
        harness.Outlets.OfType<ComponentTransmitter>().Any(t => t.UsesGroupScopedGrounding());

    private static void Translate(IGH_Attributes attributes, SizeF delta)
    {
        attributes.Pivot = new PointF(attributes.Pivot.X + delta.Width, attributes.Pivot.Y + delta.Height);
        attributes.ExpireLayout();
        attributes.PerformLayout();
    }

    private static void Redraw(bool moved)
    {
        if (moved)
        {
            // A repaint, and nothing else: the SET of attributes has not changed, so the document's
            // attribute cache is still valid — dropping it mid-drag would only make Grasshopper
            // rebuild the hit-test list on every one of these passes.
            Instances.ActiveCanvas?.Refresh();
        }
    }

    private static bool SameDelta(SizeF a, SizeF b) =>
        Math.Abs(a.Width - b.Width) <= MoveEpsilon && Math.Abs(a.Height - b.Height) <= MoveEpsilon;

    private static bool HasMoved(SizeF delta) =>
        Math.Abs(delta.Width) > MoveEpsilon || Math.Abs(delta.Height) > MoveEpsilon;

    // True when the anchors currently held were measured on this document.
    private static bool IsAnchorDocument(GH_Document host) =>
        _anchorDocument is { } weak
        && weak.TryGetTarget(out GH_Document? anchored)
        && ReferenceEquals(anchored, host);

    private static void Rebase(GH_Document host, Snapshot snapshot)
    {
        _anchorDocument = new WeakReference<GH_Document>(host);
        Replace(MemberAnchors, snapshot.Members);
        Replace(ProxyAnchors, snapshot.Proxies);
    }

    private static void Replace(Dictionary<Guid, PointF> anchors, Dictionary<Guid, PointF> measured)
    {
        anchors.Clear();
        foreach (KeyValuePair<Guid, PointF> pivot in measured)
        {
            anchors[pivot.Key] = pivot.Value;
        }
    }

    private static void Forget()
    {
        MemberAnchors.Clear();
        ProxyAnchors.Clear();
        _anchorDocument = null;
    }

    /// <summary>
    /// One pass's view of the canvas: every object by id (so nothing has to be looked up again), the
    /// pivots of the group's members and of the harness proxies, and the group objects whose boxes
    /// have to be re-laid out when their members move.
    /// </summary>
    /// <param name="Index">Every object on the canvas, by instance id.</param>
    /// <param name="Members">Pivots of the master group's members, nested groups expanded.</param>
    /// <param name="Proxies">Pivots of every harness proxy on the canvas.</param>
    /// <param name="Groups">The master group and any group nested inside it.</param>
    private sealed record Snapshot(
        Dictionary<Guid, IGH_DocumentObject> Index,
        Dictionary<Guid, PointF> Members,
        Dictionary<Guid, PointF> Proxies,
        List<GHGroupObject> Groups);
}
