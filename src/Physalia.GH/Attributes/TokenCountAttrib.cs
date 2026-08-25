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
/// Custom attributes for the Token Count human tool. Renders a bezier wire from the bottom-centre
/// grip to the linked <see cref="TokenEstimator"/> — the one whose count the chat window shows.
/// Drag to link; Ctrl+drag to unlink.
/// </summary>
public class TokenCountAttrib : GripLinkAttrib
{
    private readonly TokenCount _tokenCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenCountAttrib"/> class.
    /// </summary>
    /// <param name="tokenCount">The Token Count component that owns these attributes.</param>
    public TokenCountAttrib(TokenCount tokenCount)
        : base(tokenCount)
    {
        _tokenCount = tokenCount;
    }

    /// <inheritdoc/>
    public override WireGradient ArrowGradient => ArrowStyles.TokenCount;

    /// <inheritdoc/>
    protected override IEnumerable<Guid> LinkedTargets
    {
        get
        {
            if (_tokenCount.LinkedGuid != Guid.Empty)
            {
                yield return _tokenCount.LinkedGuid;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only a Token Estimator. The other token components measure for a decision the pipeline makes
    /// on its own (a Token Threshold's gate, a Token Window's budget); the estimator is the one
    /// whose whole output is a number for someone to read.
    /// </remarks>
    protected override bool IsValidTarget(IGH_DocumentObject obj) => obj is TokenEstimator;

    /// <inheritdoc/>
    protected override void OnConnect(Guid targetGuid) => _tokenCount.LinkTo(targetGuid);

    /// <inheritdoc/>
    protected override void OnDisconnect(Guid targetGuid) => _tokenCount.Unlink();

    /// <inheritdoc/>
    /// <remarks>Wires land on the estimator's bottom edge, centred — it hosts no grip of its own.</remarks>
    protected override PointF GetTargetAnchor(RectangleF targetBounds)
        => new PointF(targetBounds.Left + (targetBounds.Width * 0.5f), targetBounds.Bottom);
}
