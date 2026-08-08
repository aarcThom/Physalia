// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;

namespace Physalia.GH.Harness;

/// <summary>
/// A transmitter whose drag arrow is hosted by the harness proxy on the user's canvas.
///
/// <para>Transmitters draw no arrow of their own any more. They live inside a harness document,
/// while the things their arrow points at — a script component to link, a free point to place a
/// generated graph at — live on the host canvas, and a drag cannot cross two canvases. So the
/// proxy carries the grip and forwards the drag here, which puts the gesture on the canvas where
/// the target actually is.</para>
///
/// <para>The proxy only shows the arrow when its harness holds exactly one implementer; with none
/// or several there is nothing unambiguous to delegate to.</para>
/// </summary>
public interface IHarnessArrow
{
    /// <summary>
    /// Returns the canvas points where the settled (non-drag) wires currently land, resolved
    /// against the host document. Empty when nothing is connected or placed yet.
    /// </summary>
    /// <param name="hostDocument">The user's canvas, where the arrow's targets live.</param>
    /// <returns>The settled wire end points, in canvas coordinates.</returns>
    IEnumerable<PointF> GetArrowEndpoints(GH_Document hostDocument);

    /// <summary>
    /// Commits a completed drag: link or unlink a target, or store a placement point.
    /// </summary>
    /// <param name="hostDocument">The user's canvas, where the drop landed.</param>
    /// <param name="dropPoint">The drop point in canvas coordinates.</param>
    /// <param name="ctrl">Whether the drag carried the disconnect (Ctrl) intent.</param>
    void HandleDrop(GH_Document hostDocument, PointF dropPoint, bool ctrl);
}
