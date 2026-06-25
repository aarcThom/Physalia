// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.GH.Attributes;
using Physalia.GH.Components;

namespace Physalia.GH.Harness;

/// <summary>
/// Manages a collapsible group of pipeline components behind a single proxy node (a Chatbox).
/// Holds the member set, the collapsed state, and the bookkeeping to hide and restore members
/// in place: Physalia (<see cref="PhyBase"/>) members are flagged and their own attributes
/// shrink them to the proxy; non-Physalia members have their attributes swapped for a
/// <see cref="CollapsedProxyAttributes"/> stand-in and restored on expand. Members are never
/// moved or removed, so they stay wired and keep solving while hidden.
///
/// <para>All the group/collapse logic lives here so the Chatbox stays a thin proxy that merely
/// delegates. The member set and collapsed flag persist with the Chatbox; the swapped-attribute
/// map is session-only and rebuilt by re-applying the collapsed state after a load.</para>
/// </summary>
public sealed class Harness
{
    private readonly IGH_DocumentObject _owner;
    private readonly HashSet<Guid> _members = new();
    private readonly Dictionary<Guid, IGH_Attributes> _swapped = new();

    // Original parameter attributes stashed while a member is hidden (keyed by param InstanceGuid),
    // so its grips can be made non-wireable behind the collapsed proxy and restored on expand.
    private readonly Dictionary<Guid, IGH_Attributes> _swappedParams = new();

    private bool _collapsed;

    // The collapse point last pushed to the members, so the layout-time refresh re-pushes only
    // when the proxy has actually moved (NaN = force on the next apply).
    private PointF _lastPoint = new(float.NaN, float.NaN);

    /// <summary>
    /// Initializes a new instance of the <see cref="Harness"/> class.
    /// </summary>
    /// <param name="owner">The proxy node (Chatbox) that owns and represents the group.</param>
    public Harness(IGH_DocumentObject owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>Gets a value indicating whether the group is currently collapsed (hidden).</summary>
    public bool Collapsed => _collapsed;

    /// <summary>Gets the number of components in the group.</summary>
    public int Count => _members.Count;

    // The shared point every member collapses to: the proxy node's pivot.
    private PointF Point => _owner.Attributes?.Pivot ?? PointF.Empty;

    /// <summary>
    /// Adds components to the group, ignoring the proxy itself and duplicates. Newly added
    /// members are hidden immediately when the group is already collapsed.
    /// </summary>
    /// <param name="guids">The InstanceGuids to add.</param>
    /// <returns>The members actually added (excludes the proxy and existing members).</returns>
    public IReadOnlyList<Guid> Add(IEnumerable<Guid> guids)
    {
        GH_Document? doc = _owner.OnPingDocument();
        var added = new List<Guid>();
        foreach (Guid g in guids)
        {
            if (g == _owner.InstanceGuid || !CanContain(doc, g))
            {
                continue;
            }

            if (_members.Add(g))
            {
                added.Add(g);
            }
        }

        if (added.Count > 0 && _collapsed)
        {
            ApplyState();
        }

        return added;
    }

    /// <summary>Whether the given component is a member of this group.</summary>
    /// <param name="g">The component InstanceGuid to test.</param>
    /// <returns>true when it is a member.</returns>
    public bool Contains(Guid g) => _members.Contains(g);

    /// <summary>
    /// Whether a component is already a member of some Chatbox's harness other than the one
    /// identified by <paramref name="exceptOwnerGuid"/>. Keeps each component in at most one
    /// harness and stops a chatbox that is itself a member from starting its own (no nesting).
    /// </summary>
    /// <param name="doc">The document to scan.</param>
    /// <param name="memberGuid">The candidate component's InstanceGuid.</param>
    /// <param name="exceptOwnerGuid">A Chatbox owner to ignore (usually the asking harness).</param>
    /// <returns>true when another harness already owns the component.</returns>
    public static bool IsMemberOfAnyHarness(GH_Document doc, Guid memberGuid, Guid exceptOwnerGuid)
    {
        foreach (Chatbox cb in doc.Objects.OfType<Chatbox>())
        {
            if (cb.InstanceGuid != exceptOwnerGuid && cb.Group.Contains(memberGuid))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a candidate may join this harness: not a Chatbox that already owns a harness (no
    // nesting), and not already a member of another Chatbox's harness (single membership). A plain
    // chatbox with no members of its own may still be added.
    private bool CanContain(GH_Document? doc, Guid g)
    {
        if (doc is null)
        {
            return true; // can't validate without a document (rare, e.g. before load) — allow.
        }

        IGH_DocumentObject? obj = doc.FindObject(g, false);
        if (obj is null)
        {
            return false;
        }

        if (obj is Chatbox cb && cb.Group.Count > 0)
        {
            return false;
        }

        return !IsMemberOfAnyHarness(doc, g, _owner.InstanceGuid);
    }

    /// <summary>
    /// Removes components from the group, restoring any that were hidden so they reappear on
    /// the canvas.
    /// </summary>
    /// <param name="guids">The InstanceGuids to remove.</param>
    /// <returns>The members actually removed (those that were in the group).</returns>
    public IReadOnlyList<Guid> Remove(IEnumerable<Guid> guids)
    {
        var removed = new List<Guid>();
        foreach (Guid g in guids.ToList())
        {
            if (_members.Remove(g))
            {
                ShowMember(Find(g), g);
                removed.Add(g);
            }
        }

        if (removed.Count > 0)
        {
            ResetIfEmpty();
            Refresh(expireOwner: true);
        }

        return removed;
    }

    /// <summary>
    /// Collapses (hides) or expands (restores) the whole group.
    /// </summary>
    /// <param name="collapsed">true to hide the members, false to restore them.</param>
    public void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        ApplyState();
    }

    /// <summary>Toggles the collapsed state.</summary>
    public void Toggle() => SetCollapsed(!_collapsed);

    /// <summary>
    /// (Re)applies the current collapsed state to every member, pruning any that have left the
    /// document. Idempotent — safe to call after a load, after edits, or when the proxy moves.
    /// </summary>
    /// <param name="expireOwner">
    /// Whether to expire the proxy's own layout too. False when called from the proxy's layout
    /// pass (e.g. <see cref="RefreshCollapsePoint"/>) to avoid re-entrant layout.
    /// </param>
    public void ApplyState(bool expireOwner = true)
    {
        GH_Document? doc = _owner.OnPingDocument();
        if (doc is null)
        {
            return;
        }

        PointF point = Point;
        _lastPoint = _collapsed ? point : new PointF(float.NaN, float.NaN);

        foreach (Guid g in _members.ToList())
        {
            IGH_DocumentObject? obj = doc.FindObject(g, false);
            if (obj is null)
            {
                _members.Remove(g);
                _swapped.Remove(g);
                continue;
            }

            if (_collapsed)
            {
                HideMember(obj, point);
            }
            else
            {
                ShowMember(obj, g);
            }
        }

        Refresh(expireOwner);
    }

    /// <summary>
    /// Drops members that are no longer in the document. Cheap; safe to call every solve.
    /// </summary>
    /// <param name="doc">The owning document.</param>
    public void Prune(GH_Document doc)
    {
        foreach (Guid g in _members.ToList())
        {
            if (doc.FindObject(g, false) is null)
            {
                _members.Remove(g);
                _swapped.Remove(g);
            }
        }

        ResetIfEmpty();
    }

    // Once the group is empty it is no longer a harness, so drop any lingering collapsed state.
    private void ResetIfEmpty()
    {
        if (_members.Count == 0)
        {
            _collapsed = false;
            _lastPoint = new PointF(float.NaN, float.NaN);
        }
    }

    /// <summary>
    /// Keeps hidden members glued under the proxy when it is dragged. Cheap and safe to call
    /// from the proxy's own layout pass: it re-pushes the collapse point only when the proxy has
    /// actually moved, expires just the member layouts, and never refreshes the canvas (which
    /// would loop the paint). No-op when expanded.
    /// </summary>
    public void RefreshCollapsePoint()
    {
        if (!_collapsed)
        {
            return;
        }

        PointF point = Point;
        if (point == _lastPoint)
        {
            return;
        }

        GH_Document? doc = _owner.OnPingDocument();
        if (doc is null)
        {
            return;
        }

        _lastPoint = point;

        foreach (Guid g in _members)
        {
            IGH_DocumentObject? obj = doc.FindObject(g, false);
            if (obj is PhyBase member)
            {
                member.HarnessCollapsePoint = point;
                member.Attributes?.ExpireLayout();
            }
            else if (obj?.Attributes is CollapsedProxyAttributes proxy)
            {
                proxy.UpdatePoint(point);
            }
        }
    }

    /// <summary>
    /// Persists the member set and collapsed flag with the owning Chatbox.
    /// </summary>
    /// <param name="writer">The writer to persist into.</param>
    public void Write(GH_IWriter writer)
    {
        writer.SetBoolean("HarnessCollapsed", _collapsed);
        writer.SetInt32("HarnessMemberCount", _members.Count);

        int i = 0;
        foreach (Guid g in _members)
        {
            writer.SetGuid("HarnessMember", i++, g);
        }
    }

    /// <summary>
    /// Restores the member set and collapsed flag. The collapsed state is re-applied to the
    /// members later (once the document has loaded) via <see cref="ApplyState"/>.
    /// </summary>
    /// <param name="reader">The reader to restore from.</param>
    public void Read(GH_IReader reader)
    {
        _members.Clear();
        _swapped.Clear();
        _collapsed = false;

        reader.TryGetBoolean("HarnessCollapsed", ref _collapsed);

        int count = 0;
        reader.TryGetInt32("HarnessMemberCount", ref count);
        for (int i = 0; i < count; i++)
        {
            if (reader.ItemExists("HarnessMember", i))
            {
                _members.Add(reader.GetGuid("HarnessMember", i));
            }
        }
    }

    private void HideMember(IGH_DocumentObject obj, PointF point)
    {
        // Drop the member's parameter grips (computed from the original attributes' Kind) so no
        // wire can be pulled from the collapsed cluster, then hide the member itself.
        HideParamGrips(obj);

        if (obj is PhyBase member)
        {
            member.HarnessCollapsed = true;
            member.HarnessCollapsePoint = point;
            member.Attributes?.ExpireLayout();
            return;
        }

        if (obj.Attributes is CollapsedProxyAttributes proxy)
        {
            // Already hidden — just keep it under the (possibly moved) proxy.
            proxy.UpdatePoint(point);
            return;
        }

        IGH_Attributes original = obj.Attributes;
        _swapped[obj.InstanceGuid] = original;
        obj.Attributes = new CollapsedProxyAttributes(obj, original, point);
        obj.Attributes.ExpireLayout();
    }

    private void ShowMember(IGH_DocumentObject? obj, Guid g)
    {
        if (obj is PhyBase member)
        {
            member.HarnessCollapsed = false;
            member.Attributes?.ExpireLayout();
        }
        else if (obj is not null && _swapped.TryGetValue(g, out IGH_Attributes? original))
        {
            obj.Attributes = original;
            original.ExpireLayout();
        }

        _swapped.Remove(g);
        ShowParamGrips(obj);
    }

    // Wraps each of a member's parameter attributes so their grips disappear while the harness is
    // collapsed (gated on this harness). Stashes the originals for restore; idempotent.
    private void HideParamGrips(IGH_DocumentObject obj)
    {
        if (obj is not IGH_Component component)
        {
            return;
        }

        foreach (IGH_Param param in component.Params.Input.Concat(component.Params.Output))
        {
            if (param.Attributes is HarnessParamAttributes)
            {
                continue;
            }

            _swappedParams[param.InstanceGuid] = param.Attributes;
            param.Attributes = new HarnessParamAttributes(param, component.Attributes, this);
        }
    }

    // Restores a member's original parameter attributes (and their normal grips) on expand.
    private void ShowParamGrips(IGH_DocumentObject? obj)
    {
        if (obj is not IGH_Component component)
        {
            return;
        }

        foreach (IGH_Param param in component.Params.Input.Concat(component.Params.Output))
        {
            if (_swappedParams.TryGetValue(param.InstanceGuid, out IGH_Attributes? original))
            {
                param.Attributes = original;
                param.Attributes.ExpireLayout();
                _swappedParams.Remove(param.InstanceGuid);
            }
        }
    }

    private IGH_DocumentObject? Find(Guid g) => _owner.OnPingDocument()?.FindObject(g, false);

    private void Refresh(bool expireOwner)
    {
        if (expireOwner)
        {
            _owner.Attributes?.ExpireLayout();
        }

        Grasshopper.Instances.ActiveCanvas?.Refresh();
    }
}
