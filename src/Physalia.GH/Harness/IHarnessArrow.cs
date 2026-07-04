// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;

namespace Physalia.GH.Harness;

/// <summary>
/// A harness member that exposes a bottom-centre drag arrow (the transmitters). When such a
/// member is the sole arrow-bearing component in a collapsed harness, the Chat proxy draws
/// the arrow from its own bottom and delegates the drag through this interface, so the real
/// link/placement is updated and survives expansion. Hides the two transmitters' different
/// interaction models (link-to-object vs drop-to-point) behind common operations.
/// </summary>
public interface IHarnessArrow
{
    /// <summary>
    /// Returns the canvas points where the settled arrow(s) currently land — the linked target's
    /// anchor for a link arrow, or the stored placement point for a drop arrow. Empty when nothing
    /// is connected yet.
    /// </summary>
    /// <param name="doc">The document to resolve linked targets against.</param>
    /// <returns>The settled wire end points, in canvas coordinates.</returns>
    IEnumerable<PointF> GetArrowEndpoints(GH_Document doc);

    /// <summary>
    /// Applies a drop at the given canvas point, performing whatever this transmitter does on drop
    /// (link to a valid object under the point, or store the point as a placement target) and
    /// expiring itself so the change takes effect.
    /// </summary>
    /// <param name="doc">The document the drop landed in.</param>
    /// <param name="dropPoint">The drop point in canvas coordinates.</param>
    /// <param name="ctrl">Whether Ctrl was held (disconnect intent for link arrows).</param>
    void HandleDrop(GH_Document doc, PointF dropPoint, bool ctrl);
}
