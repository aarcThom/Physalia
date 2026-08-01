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
/// Custom attributes for the Interface Lock grounder. Renders a bezier wire from the
/// bottom-centre grip to the linked <see cref="PyTransmitter"/>.
/// Drag to link; Ctrl+drag to unlink.
/// </summary>
public class InterfaceLockAttrib : GripLinkAttrib
{
    private readonly InterfaceLock _interfaceLock;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterfaceLockAttrib"/> class.
    /// </summary>
    /// <param name="interfaceLock">The Interface Lock component that owns these attributes.</param>
    public InterfaceLockAttrib(InterfaceLock interfaceLock)
        : base(interfaceLock)
    {
        _interfaceLock = interfaceLock;
    }

    /// <inheritdoc/>
    public override WireGradient ArrowGradient => ArrowStyles.InterfaceLock;

    /// <inheritdoc/>
    protected override IEnumerable<Guid> LinkedTargets
    {
        get
        {
            if (_interfaceLock.LinkedGuid != Guid.Empty)
                yield return _interfaceLock.LinkedGuid;
        }
    }

    /// <inheritdoc/>
    protected override bool IsValidTarget(IGH_DocumentObject obj) => obj is PyTransmitter;

    /// <inheritdoc/>
    protected override void OnConnect(Guid targetGuid) => _interfaceLock.LinkTo(targetGuid);

    /// <inheritdoc/>
    protected override void OnDisconnect(Guid targetGuid) => _interfaceLock.Unlink();

    /// <inheritdoc/>
    /// <remarks>Wires land just above the top-centre of the target transmitter, clear of its own bottom grip.</remarks>
    protected override PointF GetTargetAnchor(RectangleF targetBounds)
        => new PointF(targetBounds.Left + (targetBounds.Width / 2f), targetBounds.Top - 6f);
}
