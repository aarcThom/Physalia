// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;

namespace Physalia.GH.Attributes.UiElements;

/// <summary>
/// The single place every Physalia drag-arrow gradient is defined. Each component's wire colour
/// lives here as a named <see cref="WireGradient"/>, so a colour can be changed in one spot rather
/// than hunting through the individual attribute classes.
/// </summary>
public static class ArrowStyles
{
    /// <summary>Feedback → FeedbackCollector links: blue to purple.</summary>
    public static readonly WireGradient Feedback = new(Color.Blue, Color.Purple);

    /// <summary>PyTransmitter → GH Python Script link: aquamarine to deep pink.</summary>
    public static readonly WireGradient PyTransmitter = new(Color.Aquamarine, Color.DeepPink);

    /// <summary>CsTransmitter → GH C# Script link: blue violet to turquoise.</summary>
    public static readonly WireGradient CsTransmitter = new(Color.BlueViolet, Color.Turquoise);

    /// <summary>Script I/O → script transmitter link: lime green to teal.</summary>
    public static readonly WireGradient ScriptIO = new(Color.LimeGreen, Color.Teal);

    /// <summary>Component Transmitter free-point drop arrow: orange to medium orchid.</summary>
    public static readonly WireGradient CompTx = new(Color.Orange, Color.MediumOrchid);

    /// <summary>Text Transmitter → the input it feeds: neon green to blue.</summary>
    public static readonly WireGradient TextTx = new(Color.FromArgb(255, 57, 255, 20), Color.Blue);

    /// <summary>ZoomGuid → any component link: gold to royal blue.</summary>
    public static readonly WireGradient ZoomGuid = new(Color.Gold, Color.RoyalBlue);
}
