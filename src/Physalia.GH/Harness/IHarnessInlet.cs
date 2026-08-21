// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Physalia.GH.Harness;

/// <summary>
/// One inlet of a harness: a Receiver inside it whose input parameter is hosted by the proxy on the
/// user's canvas. The mirror of <see cref="IHarnessOutlet"/>, and the only way data gets IN.
///
/// <para>The asymmetry between the two is deliberate, not an oversight. What a pipeline PRODUCES is
/// an edit to the user's canvas, and Grasshopper has no mechanism for "a wire that writes" — so an
/// outlet is a side effect carried by a drag arrow we draw ourselves. What a pipeline CONSUMES is
/// data the canvas already computed, and Grasshopper hands us wires pointing inward for free — so an
/// inlet is an ordinary input parameter on the proxy, with ordinary expiry, ordinary solve ordering
/// and an ordinary wire. Nothing here needs the watch machinery an arrow would have needed.</para>
///
/// <para><b>An inlet is passive.</b> Data arriving never mints a signal and never starts a round: a
/// Receiver latches what it was handed and outputs it, and that is all. Two reasons, and the second
/// is structural. A value source that fired the pipeline would launch an inference per slider tick;
/// and the moment a transmitter can write into something that feeds a receiver, harness-writes-canvas
/// / canvas-feeds-harness becomes a cycle Grasshopper's own detector cannot see, because half of it is
/// not a wire. Passive inlets mean nothing in the harness ACTS on inlet data alone, so that loop can
/// never close.</para>
///
/// <para>The proxy grows ONE input per inlet, stacked down its left edge in the order the Receivers
/// are laid out inside the harness. An inlet's input and the Receiver's own OUTPUT share ONE name —
/// both start out "Data", and renaming either end renames the other, so the label on the harness and
/// the label on the wire inside can never drift apart. Unlike an outlet's grip, an inlet's parameter
/// is a real object that other components' wires point AT, so it is bound to its Receiver by
/// InstanceGuid and never rebuilt while that Receiver lives: rebuilding it would drop the wire, and
/// re-binding by position would silently swap one Receiver's data for another's.</para>
/// </summary>
public interface IHarnessInlet
{
    /// <summary>
    /// Gets or sets the name this inlet is known by at BOTH ends — the Receiver's output parameter
    /// inside the harness, and the input it grows on the proxy outside. Reading it asks the Receiver
    /// what its output is called; setting it renames that output, which is what carries a rename made
    /// on the proxy back inside.
    /// </summary>
    string InletName { get; set; }

    /// <summary>
    /// Gets the description shown on the proxy's input tooltip.
    /// </summary>
    string InletDescription { get; }

    /// <summary>
    /// Hands this inlet the data currently on its parameter, latching it for every subsequent solve
    /// of the harness document. The harness pipeline solves on its own schedule — a signal round
    /// arrives long after the host solution that delivered this — so the data has to be held rather
    /// than read from a wire that is no longer live.
    /// </summary>
    /// <param name="data">The tree from the proxy's input, empty when nothing is wired.</param>
    /// <returns>True when this is different data from what was already held.</returns>
    bool Accept(GH_Structure<IGH_Goo> data);

    /// <summary>
    /// Drops the held data, putting the inlet back the way a freshly placed one starts. Used when a
    /// harness's contents are replaced, so nothing from the previous pipeline lingers.
    /// </summary>
    void ClearInlet();
}
