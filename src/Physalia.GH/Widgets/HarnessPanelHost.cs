// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Runtime.CompilerServices;
using Grasshopper.GUI.Canvas;
using Physalia.GH.Harness;

namespace Physalia.GH.Widgets;

/// <summary>
/// Attaches one <see cref="HarnessPanel"/> to each canvas and keeps it pointed at whatever harness
/// that canvas is currently inside.
///
/// <para>The panel is a control rather than a widget, so nothing in Grasshopper's widget machinery
/// manages its lifetime — this does. It hangs off <c>WidgetListCreated</c> all the same, because that
/// is the one hook that fires once per canvas with the canvas in hand, which is exactly when a
/// control needs to be parented.</para>
///
/// <para>Panels are held weakly against their canvas: Grasshopper makes a canvas per document window
/// and disposes them on its own schedule, and a static list of them would keep every canvas a session
/// ever opened alive.</para>
/// </summary>
internal static class HarnessPanelHost
{
    private static readonly ConditionalWeakTable<GH_Canvas, HarnessPanel> Panels = new();

    /// <summary>
    /// Gives a canvas its harness panel.
    /// </summary>
    /// <param name="canvas">The canvas being set up.</param>
    internal static void Attach(GH_Canvas? canvas)
    {
        if (canvas is null || Panels.TryGetValue(canvas, out _))
        {
            return;
        }

        var panel = new HarnessPanel { Visible = false };
        Panels.Add(canvas, panel);

        canvas.Controls.Add(panel);
        panel.BringToFront();

        canvas.DocumentChanged += (sender, _) => Refresh(sender);

        // Covers the canvas already showing a harness document by the time this runs — reopening a
        // file that was saved from inside one.
        Refresh(canvas);
    }

    /// <summary>
    /// Re-points every attached panel, for when a harness's name changed under it.
    /// </summary>
    /// <param name="canvas">The canvas to refresh; null refreshes the active one.</param>
    internal static void Refresh(GH_Canvas? canvas)
    {
        canvas ??= Grasshopper.Instances.ActiveCanvas;
        if (canvas is null || !Panels.TryGetValue(canvas, out HarnessPanel? panel))
        {
            return;
        }

        panel.Show(HarnessComponent.OwnerOf(canvas.Document));
        panel.BringToFront();
    }
}
