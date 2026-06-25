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
/// Custom attributes for the ZoomGuid component. Renders a bezier wire from the bottom-centre
/// grip to the linked component. Drag to link to any component; Ctrl+drag to unlink.
/// </summary>
public class ZoomGuidAttrib : GripLinkAttrib
{
    private readonly ZoomGuid _zoomGuid;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZoomGuidAttrib"/> class.
    /// </summary>
    /// <param name="zoomGuid">The ZoomGuid component that owns these attributes.</param>
    public ZoomGuidAttrib(ZoomGuid zoomGuid)
        : base(zoomGuid)
    {
        _zoomGuid = zoomGuid;
    }

    /// <inheritdoc/>
    public override WireGradient ArrowGradient => ArrowStyles.ZoomGuid;

    /// <inheritdoc/>
    protected override IEnumerable<Guid> LinkedTargets
    {
        get
        {
            if (_zoomGuid.LinkedGuid != Guid.Empty)
                yield return _zoomGuid.LinkedGuid;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Any component is a valid target, except this ZoomGuid itself.</remarks>
    protected override bool IsValidTarget(IGH_DocumentObject obj)
        => obj is IGH_Component && obj.InstanceGuid != _zoomGuid.InstanceGuid;

    /// <inheritdoc/>
    protected override void OnConnect(Guid targetGuid) => _zoomGuid.LinkTo(targetGuid);

    /// <inheritdoc/>
    protected override void OnDisconnect(Guid targetGuid) => _zoomGuid.Unlink();

    /// <inheritdoc/>
    /// <remarks>Wires land on the top-centre of the target component.</remarks>
    protected override PointF GetTargetAnchor(RectangleF targetBounds)
        => new PointF(targetBounds.Left + targetBounds.Width / 2f, targetBounds.Bottom + 6f);
}
