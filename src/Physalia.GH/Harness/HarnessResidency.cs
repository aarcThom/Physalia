// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Physalia.GH.Components;
using Physalia.GH.Generation;

namespace Physalia.GH.Harness;

/// <summary>
/// Keeps Physalia components inside a harness. The harness is the plug-in's unit of work: a
/// pipeline lives in its own document and the canvas carries only the proxy, so a pipeline
/// component dropped straight onto the user's model has nowhere useful to be — it cannot reach the
/// Chat, and it clutters the canvas the model is asked to reason about.
///
/// <para>Grasshopper has no veto hook for placement; a component only learns it was added once it
/// already is. So an offender is removed on the next idle pass, with the reason written to the
/// Rhino command line. Removal is deliberately NOT undoable: an undo would re-add the component,
/// which would trip this guard again and remove it right back.</para>
/// </summary>
internal static class HarnessResidency
{
    // Components seen landing outside a harness, drained on the next idle pass. Deferred rather
    // than removed on the spot because AddedToDocument runs while Grasshopper is still mutating the
    // document, and because a transient placement (a preset lands on the canvas and is moved into a
    // harness a moment later) must be allowed to settle before being judged.
    private static readonly List<PhyBase> Pending = new();

    private static bool _hooked;

    /// <summary>
    /// Notes a component that has just been added to a document, so it can be checked once the
    /// document settles. Ignores everything that is legitimately outside a harness.
    /// </summary>
    /// <param name="component">The component that was added.</param>
    /// <param name="document">The document it was added to.</param>
    internal static void Track(PhyBase component, GH_Document document)
    {
        if (component is HarnessComponent)
        {
            return; // the proxy is the one Physalia node that belongs on the user's canvas
        }

        if (PhyDocuments.IsHarnessDocument(document))
        {
            return; // already where it should be
        }

        if (GhJsonBridge.IsImporting)
        {
            return; // Physalia placing its own graph; the idle re-check will judge the result
        }

        // A document being built off-canvas is a file load (Grasshopper reads every object in
        // before handing the document to the canvas) or a harness archive being rehydrated. Neither
        // is a user placing a component, and existing files are deliberately left as they are.
        if (!ReferenceEquals(document, Instances.ActiveCanvas?.Document))
        {
            return;
        }

        Pending.Add(component);

        if (!_hooked)
        {
            _hooked = true;
            Rhino.RhinoApp.Idle += OnIdle;
        }
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        Rhino.RhinoApp.Idle -= OnIdle;
        _hooked = false;

        // Re-check rather than trusting the queue: anything that has since been moved into a
        // harness (a preset is placed on the canvas and swept in straight afterwards) or deleted is
        // no longer an offender.
        List<PhyBase> offenders = Pending
            .Where(c => c.OnPingDocument() is { } doc && !PhyDocuments.IsHarnessDocument(doc))
            .ToList();

        Pending.Clear();

        if (offenders.Count == 0)
        {
            return;
        }

        foreach (IGrouping<GH_Document, PhyBase> group in offenders.GroupBy(c => c.OnPingDocument()!))
        {
            group.Key.RemoveObjects(group.Cast<IGH_DocumentObject>().ToList(), false);
        }

        Report(offenders);
        Instances.ActiveCanvas?.Refresh();
    }

    private static void Report(IReadOnlyList<PhyBase> removed)
    {
        string names = string.Join(", ", removed.Select(c => c.Name).Distinct());
        string subject = removed.Count == 1 ? "component" : "components";

        Rhino.RhinoApp.WriteLine(
            $"[Physalia] Removed {names}: Physalia {subject} live inside a Harness, not on the canvas. "
            + "Right-click a Harness and choose \"Edit Harness\" to go in, then place it there. "
            + "No Harness yet? Double-click the Physalia widget to start one.");
    }
}
