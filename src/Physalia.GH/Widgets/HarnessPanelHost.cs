// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Physalia.GH.Harness;

namespace Physalia.GH.Widgets;

/// <summary>
/// Gives each canvas its <see cref="HarnessPanel"/>, keeps it pointed at whatever harness that
/// canvas is inside, and keeps it sitting over the canvas's corner.
///
/// <para>The panel is a window rather than a child control, for the focus reasons written up on
/// <see cref="HarnessPanel"/>. That buys a working text field and costs exactly one thing: a child
/// control gets its position from its parent for free, and a window does not. So this is where the
/// panel is made to FOLLOW — anything that moves or resizes the canvas, or the editor window around
/// it, repositions the panel.</para>
///
/// <para>It still hangs off <c>WidgetListCreated</c>, which is the one static hook Grasshopper offers
/// that fires once per canvas with the canvas in hand. Panels are held weakly against their canvas:
/// Grasshopper makes one per document window and disposes them on its own schedule, and a static list
/// would keep every canvas a session ever opened alive.</para>
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

        var panel = new HarnessPanel { AnchorCanvas = canvas };
        Panels.Add(canvas, panel);

        // Owning the panel to a window, and following that window as it moves, is deliberately NOT
        // done here: WidgetListCreated fires while the editor is still being built, so there is no
        // window to find yet. HarnessPanel.EnsureHostWindow does it the first time the panel is
        // shown — see the note there, this is where it was got wrong.

        // The canvas moving inside its window — a docked panel resized, the ribbon shown or hidden —
        // moves the corner the panel is pinned to.
        Follow(h => canvas.LocationChanged += h, panel);
        Follow(h => canvas.SizeChanged += h, panel);
        Follow(h => canvas.ParentChanged += h, panel);

        canvas.VisibleChanged += (sender, _) => Refresh(sender as GH_Canvas);
        canvas.DocumentChanged += (sender, _) => Refresh(sender);

        // A canvas is disposed when its document window closes; the panel is its window and has to go
        // with it, or it is left floating over nothing.
        canvas.Disposed += (sender, _) =>
        {
            if (sender is GH_Canvas gone)
            {
                Panels.Remove(gone);
            }

            panel.Dispose();
        };

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
        canvas ??= Instances.ActiveCanvas;
        if (canvas is null || !Panels.TryGetValue(canvas, out HarnessPanel? panel) || panel.IsDisposed)
        {
            return;
        }

        // A canvas that is not on screen — another document's tab is showing — must not leave its
        // panel floating over the one that is.
        panel.Bind(canvas.Visible ? HarnessComponent.OwnerOf(canvas.Document) : null);
    }

    // Repositions the panel whenever the given event fires. Guarded against a disposed panel, since
    // these handlers outlive it if the canvas goes first.
    private static void Follow(Action<EventHandler> subscribe, HarnessPanel panel) =>
        subscribe((_, _) =>
        {
            if (!panel.IsDisposed && panel.Visible)
            {
                panel.Reposition();
            }
        });
}
