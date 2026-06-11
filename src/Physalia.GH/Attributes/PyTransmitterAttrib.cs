// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components.GhPython;
using Physalia.GH.Generation;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the PyTransmitter component. Renders a bezier wire from the
/// bottom-centre grip to the linked GH Python Script component.
/// Drag to link; Ctrl+drag to unlink.
/// </summary>
public class PyTransmitterAttrib : GripLinkAttrib
{
    private static readonly WireGradient _defaultGradient = new WireGradient(Color.DarkGreen, Color.LimeGreen);

    private readonly PyTransmitter _pyTransmitter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PyTransmitterAttrib"/> class.
    /// </summary>
    /// <param name="pyTransmitter">The PyTransmitter component that owns these attributes.</param>
    public PyTransmitterAttrib(PyTransmitter pyTransmitter)
        : base(pyTransmitter)
    {
        _pyTransmitter = pyTransmitter;
    }

    /// <inheritdoc/>
    protected override WireGradient Gradient => _defaultGradient;

    /// <inheritdoc/>
    protected override IEnumerable<Guid> LinkedTargets
    {
        get
        {
            if (_pyTransmitter.LinkedGuid != Guid.Empty)
                yield return _pyTransmitter.LinkedGuid;
        }
    }

    /// <inheritdoc/>
    protected override bool IsValidTarget(IGH_DocumentObject obj) => GhPythonBridge.IsScriptComponent(obj);

    /// <inheritdoc/>
    protected override void OnConnect(Guid targetGuid) => _pyTransmitter.LinkTo(targetGuid);

    /// <inheritdoc/>
    protected override void OnDisconnect(Guid targetGuid) => _pyTransmitter.Unlink();

    /// <inheritdoc/>
    /// <remarks>Wires land on the top-centre of the target script component.</remarks>
    protected override PointF GetTargetAnchor(RectangleF targetBounds)
        => new PointF(targetBounds.Left + targetBounds.Width / 2f, targetBounds.Y);
}
