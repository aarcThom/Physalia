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
/// Makes a harness proxy travel with the master "Physalia" group its pipeline writes into, WITHOUT
/// the proxy ever becoming a member of that group. Membership is the obvious way to get this and the
/// wrong one: the group is the model's frame — its members are what the group-scoped grounding
/// exports and what the model may target with a group op — so the pipeline must stay outside it while
/// still reading as attached to it. Dragging the group across the canvas therefore takes the proxy,
/// its outlet wires, and the placement point those wires end on along for the ride (the point is
/// stored as an offset from the proxy, so it follows for free).
///
/// <para>Grasshopper raises no event for an object being moved — <see cref="GH_Document"/> announces
/// added, deleted and solved, never moved — and a group is moved by moving its MEMBERS, since a
/// group's own bounds are merely their union. So the movement is noticed by comparing member pivots
/// on a throttled idle pass. A move counts as the group's only when EVERY member still present moved
/// by the SAME non-zero delta: that is a rigid drag, where one member moving on its own (or a
/// placement landing, which adds members without moving any) is not.</para>
///
/// <para>A proxy that is <see cref="IGH_Attributes.Selected"/> is skipped, because Grasshopper is
/// already dragging the whole selection — following it too would double the delta and send the node
/// twice as far as the group. Known limits, both deliberate: the group's own undo record does not
/// know about the proxy (undoing the drag returns the group and leaves the proxy where it followed
/// to, which the next drag fixes), and the proxy tracks the drag at the idle interval rather than
/// per frame.</para>
/// </summary>
internal static class MasterGroupFollower
{
    // Pivots are floats and a rigid drag translates them all by the same amount, but only up to
    // rounding: two members of one drag can disagree in the last fraction of a unit.
    private const float MoveEpsilon = 0.5f;

    // How often the group's members are re-measured. A drag emits mouse-move messages continuously
    // and Rhino goes idle between them, so this is the granularity at which the proxy tracks a drag
    // in progress — small enough to read as following, long enough that the poll is invisible.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(80);

    // Transmitters that want the watch, by InstanceGuid. Held by id rather than by reference so
    // nothing here can keep a removed component alive, and so re-adding the same one (a document
    // being handed around, a harness re-opened) cannot count twice.
    private static readonly HashSet<Guid> Followers = new();

    // Where the group's members were when they were last measured, and on which document. Session
    // only; nothing about following is persisted.
    private static readonly Dictionary<Guid, PointF> Anchors = new();

    // Which document the anchors were measured on. Weak, so a document the user closed mid-gesture is
    // collected normally rather than pinned by this watch.
    private static WeakReference<GH_Document>? _anchorDocument;

    private static DateTime _lastCheckUtc = DateTime.MinValue;
    private static bool _hooked;

    /// <summary>
    /// Starts watching for the master group being dragged, on behalf of a transmitter that could be
    /// attached to it. Idempotent — the watch is one process-wide idle handler however many
    /// transmitters ask for it.
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

    // Re-measures the master group's members and, when they all moved together, carries the attached
    // proxies with them. Deliberately cheap in the common case: no master group on the canvas the
    // user is looking at means one type-filtered scan and nothing else.
    private static void OnIdle(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastCheckUtc < CheckInterval)
        {
            return;
        }

        _lastCheckUtc = now;

        GH_Document? host = PhyDocuments.ActiveHost();
        if (host is null || GhJsonBridge.FindMasterGroup(host) is null)
        {
            Forget();
            return;
        }

        Dictionary<Guid, PointF> pivots = MemberPivots(host);
        if (!IsAnchorDocument(host))
        {
            // First sight of this canvas: nothing to compare against yet.
            Rebase(host, pivots);
            return;
        }

        if (RigidDelta(pivots) is { } delta)
        {
            CarryProxies(host, delta);
        }

        Rebase(host, pivots);
    }

    // Pivots of the group's members, nested groups expanded, skipping the groups themselves — a
    // group's pivot is derived from its contents, so it would report movement its members do not.
    private static Dictionary<Guid, PointF> MemberPivots(GH_Document host)
    {
        var pivots = new Dictionary<Guid, PointF>();

        foreach (Guid guid in GhJsonBridge.MasterGroupScope(host))
        {
            if (host.FindObject(guid, false) is { Attributes: { } attributes } obj
                && obj is not GHGroupObject)
            {
                pivots[guid] = attributes.Pivot;
            }
        }

        return pivots;
    }

    // The one delta every member moved by since the last measurement, or null when this was not a
    // rigid move of the whole group: nothing moved, only some members moved, or the members that can
    // be compared do not agree. Members added or removed in between are ignored rather than
    // disqualifying — a placement landing inside the group must not read as the group moving.
    private static SizeF? RigidDelta(Dictionary<Guid, PointF> pivots)
    {
        SizeF? shared = null;

        foreach (KeyValuePair<Guid, PointF> pivot in pivots)
        {
            if (!Anchors.TryGetValue(pivot.Key, out PointF was))
            {
                continue;
            }

            var delta = new SizeF(pivot.Value.X - was.X, pivot.Value.Y - was.Y);
            if (shared is not { } first)
            {
                shared = delta;
                continue;
            }

            if (Math.Abs(delta.Width - first.Width) > MoveEpsilon
                || Math.Abs(delta.Height - first.Height) > MoveEpsilon)
            {
                return null;
            }
        }

        return shared is { } moved
            && (Math.Abs(moved.Width) > MoveEpsilon || Math.Abs(moved.Height) > MoveEpsilon)
            ? moved
            : null;
    }

    // Translates every harness proxy attached to the group by the group's own delta.
    private static void CarryProxies(GH_Document host, SizeF delta)
    {
        bool moved = false;
        foreach (HarnessComponent harness in host.Objects.OfType<HarnessComponent>())
        {
            // Selected means Grasshopper is dragging it as part of the same gesture; it has already
            // travelled the delta and following would double it.
            if (harness.Attributes is not { Selected: false } attributes || !WritesIntoGroup(harness))
            {
                continue;
            }

            attributes.Pivot = new PointF(attributes.Pivot.X + delta.Width, attributes.Pivot.Y + delta.Height);
            attributes.ExpireLayout();
            attributes.PerformLayout();
            moved = true;
        }

        if (moved)
        {
            // A repaint, and nothing else: the SET of attributes has not changed, so the document's
            // attribute cache is still valid — dropping it mid-drag would only make Grasshopper
            // rebuild the hit-test list on every one of these ticks.
            Instances.ActiveCanvas?.Refresh();
        }
    }

    // True when the harness holds a Component Transmitter whose pipeline reads the canvas through
    // the master group — the same condition that creates the group when the transmitter's arrow is
    // dropped. A harness grounded on the whole canvas has no particular relationship to the group and
    // stays where the user put it.
    private static bool WritesIntoGroup(HarnessComponent harness) =>
        harness.Outlets.OfType<ComponentTransmitter>().Any(t => t.UsesGroupScopedGrounding());

    // True when the anchors currently held were measured on this document.
    private static bool IsAnchorDocument(GH_Document host) =>
        _anchorDocument is { } weak
        && weak.TryGetTarget(out GH_Document? anchored)
        && ReferenceEquals(anchored, host);

    private static void Rebase(GH_Document host, Dictionary<Guid, PointF> pivots)
    {
        _anchorDocument = new WeakReference<GH_Document>(host);
        Anchors.Clear();
        foreach (KeyValuePair<Guid, PointF> pivot in pivots)
        {
            Anchors[pivot.Key] = pivot.Value;
        }
    }

    private static void Forget()
    {
        Anchors.Clear();
        _anchorDocument = null;
    }
}
