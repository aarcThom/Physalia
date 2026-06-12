// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the Feedback component that render a drag-to-link bezier wire
/// from Feedback's bottom-centre grip to each linked FeedbackCollector.
/// Supports multiple simultaneous connections. Ctrl+drag disconnects.
/// </summary>
public class FeedbackAttrib : GripLinkAttrib
{
    private static readonly WireGradient _defaultGradient = new WireGradient(Color.Blue, Color.Purple);

    private readonly Feedback _feedback;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedbackAttrib"/> class.
    /// </summary>
    /// <param name="feedback">The Feedback component that owns these attributes.</param>
    public FeedbackAttrib(Feedback feedback)
        : base(feedback)
    {
        _feedback = feedback;
    }

    /// <inheritdoc/>
    protected override WireGradient Gradient => _defaultGradient;

    /// <inheritdoc/>
    protected override IEnumerable<Guid> LinkedTargets => _feedback.CollectorGuids;

    /// <inheritdoc/>
    protected override bool IsValidTarget(IGH_DocumentObject obj) => obj is FeedbackCollector;

    /// <inheritdoc/>
    protected override void OnConnect(Guid targetGuid) => _feedback.AddCollector(targetGuid);

    /// <inheritdoc/>
    protected override void OnDisconnect(Guid targetGuid) => _feedback.RemoveCollector(targetGuid);

    /// <inheritdoc/>
    /// <remarks>Wires land on the bottom-centre of the collector (its own grip position).</remarks>
    protected override PointF GetTargetAnchor(RectangleF targetBounds)
        => new PointF(targetBounds.Left + targetBounds.Width / 2f, targetBounds.Y + targetBounds.Height);
}
