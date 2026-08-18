// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;

namespace Physalia.GH.Harness;

/// <summary>
/// One outlet of a harness: a transmitter inside it whose drag arrow is hosted by the proxy on the
/// user's canvas. This is the harness's only kind of "output" — a harness exchanges no dataflow with
/// the canvas, so what leaves it is never a wire's worth of data, only a transmitter's reach onto
/// the document it writes to.
///
/// <para>Transmitters draw no arrow of their own. They live inside a harness document, while the
/// things their arrow points at — a script component to link, a free point to place a generated
/// graph at — live on the host canvas, and a drag cannot cross two canvases. So the proxy carries
/// the grips and forwards each drag to the outlet it belongs to, which puts the gesture on the
/// canvas where the target actually is.</para>
///
/// <para>The proxy grows ONE grip per outlet, stacked down its right edge in the order the
/// transmitters are laid out inside the harness, each labelled and coloured by the outlet itself —
/// so a harness holding a node transmitter and a Python transmitter offers two distinguishable
/// grips rather than none. Implemented by <c>TransmitterComponentBase</c>, which every transmitter
/// derives from.</para>
/// </summary>
public interface IHarnessOutlet
{
    /// <summary>
    /// Gets the very short tag drawn beside this outlet's grip on the proxy — "node", "py", and so
    /// on. Kept to a few characters: it shares the capsule with the harness icon, and its only job
    /// is to tell one grip from another.
    /// </summary>
    string OutletLabel { get; }

    /// <summary>
    /// Gets the gradient this outlet's wire is painted with. Per-outlet rather than per-proxy, so
    /// two grips on the same harness read as two different kinds of reach.
    /// </summary>
    WireGradient OutletGradient { get; }

    /// <summary>
    /// Gets whether this outlet's wire arrives at its endpoint from the LEFT with a rightward tip,
    /// as a Grasshopper wire enters an input grip — rather than curving up from below the endpoint
    /// with an upward tip.
    ///
    /// <para>The outlet decides, because it is the one that says where its endpoints are: a wire that
    /// feeds an input arrives at that input, while a wire that only points AT a whole component turns
    /// up underneath it, so the tip reads as naming the node above it. The departure is not the
    /// outlet's business — every grip sits on the proxy's right edge, so every wire sets off
    /// rightwards.</para>
    /// </summary>
    bool HorizontalArrowEnd { get; }

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

    /// <summary>
    /// Forgets whatever this outlet was aiming at on the host canvas — the linked target, the stored
    /// placement point — putting it back the way a freshly placed one starts.
    ///
    /// <para>Every outlet persists its reach so a saved definition reopens still pointing where the
    /// user put it. That reach is meaningful only on the canvas it was aimed at, so a PRESET must not
    /// carry it: the archive is loaded onto some other document entirely, where a linked id resolves
    /// to nothing and a placement offset — measured from the proxy's pivot, so it needs no resolving
    /// at all — silently reproduces the author's own drop as a wire hanging off the new node.
    /// <see cref="HarnessComponent.ReadDocumentFile"/> calls this on every outlet it loads.</para>
    ///
    /// <para>Quiet: no undo record and no re-solve. It runs while the document is still being read,
    /// before anything is on a canvas to solve.</para>
    /// </summary>
    void ClearHostTarget();
}
