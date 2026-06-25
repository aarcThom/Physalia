// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the Component Transmitter. Renders a bezier arrow from the
/// bottom-centre grip to a free canvas point — unlike <see cref="GripLinkAttrib"/>, the
/// drop lands anywhere (no target object), and its tip marks the top-left origin where the
/// transmitter places its GhJSON graph. The point is stored as an offset from the
/// component's pivot, so the arrow travels with the component as it is moved.
/// Drag the grip to set the point; Ctrl+drag the tip to reposition it; clear it via the
/// component's right-click menu.
/// </summary>
public class CompTxAttrib : ArrowAttributeBase
{
    // Canvas-unit radius around the arrow tip within which a Ctrl+press grabs it to reposition.
    private const float TipHitRadius = 10f;

    private readonly ComponentTransmitter _transmitter;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompTxAttrib"/> class.
    /// </summary>
    /// <param name="transmitter">The Component Transmitter that owns these attributes.</param>
    public CompTxAttrib(ComponentTransmitter transmitter)
        : base(transmitter)
    {
        _transmitter = transmitter;
    }

    /// <inheritdoc/>
    public override WireGradient ArrowGradient => ArrowStyles.CompTx;

    /// <inheritdoc/>
    /// <remarks>The single settled wire lands on the stored free-canvas placement point, if any.</remarks>
    public override IEnumerable<PointF> SettledEndpoints(GH_Document doc)
    {
        if (_transmitter.PlacementTarget is { } target)
        {
            yield return target;
        }
    }

    /// <inheritdoc/>
    /// <remarks>The drop lands anywhere on the canvas — there is no target object to validate.</remarks>
    public override void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl)
    {
        _transmitter.SetPlacementTarget(dropPoint);
        _transmitter.ExpireSolution(true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Starts a fresh arrow from the bottom grip, or — when Ctrl is held over the existing arrow
    /// tip — grabs the tip to reposition the placement target. A free-point drop never disconnects,
    /// so the drag always carries the add (not remove) intent.
    /// </remarks>
    protected override bool TryStartDrag(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
        bool overTip = ctrl
            && _transmitter.PlacementTarget is { } tip
            && IsNear(e.CanvasLocation, tip, TipHitRadius);

        if (overTip || GripBounds.Contains(e.CanvasLocation))
        {
            Arrow.StartDrag(sender, e.CanvasLocation, ctrl: false);
            return true;
        }

        return false;
    }

    private static bool IsNear(PointF a, PointF b, float radius)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }
}
