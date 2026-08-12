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
/// Custom attributes for the Script I/O grounder. Renders a bezier wire from the
/// bottom-centre grip to the linked <see cref="ScriptTransmitterBase"/> — a Py or C# Transmitter.
/// Drag to link; Ctrl+drag to unlink.
/// </summary>
public class ScriptIOAttrib : GripLinkAttrib
{
    private readonly ScriptIO _scriptIO;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptIOAttrib"/> class.
    /// </summary>
    /// <param name="scriptIO">The Script I/O component that owns these attributes.</param>
    public ScriptIOAttrib(ScriptIO scriptIO)
        : base(scriptIO)
    {
        _scriptIO = scriptIO;
    }

    /// <inheritdoc/>
    public override WireGradient ArrowGradient => ArrowStyles.ScriptIO;

    /// <inheritdoc/>
    protected override IEnumerable<Guid> LinkedTargets
    {
        get
        {
            if (_scriptIO.LinkedGuid != Guid.Empty)
                yield return _scriptIO.LinkedGuid;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Any transmitter that pushes into an existing script component can be locked — the contract is
    /// its parameter set, which every language has.
    /// </remarks>
    protected override bool IsValidTarget(IGH_DocumentObject obj) => obj is ScriptTransmitterBase;

    /// <inheritdoc/>
    protected override void OnConnect(Guid targetGuid) => _scriptIO.LinkTo(targetGuid);

    /// <inheritdoc/>
    protected override void OnDisconnect(Guid targetGuid) => _scriptIO.Unlink();

    /// <inheritdoc/>
    /// <remarks>
    /// Wires land on the target transmitter's bottom edge, a quarter of the way along its width —
    /// left of the transmitter's own bottom-centre grip, so the two never collide.
    /// </remarks>
    protected override PointF GetTargetAnchor(RectangleF targetBounds)
        => new PointF(targetBounds.Left + (targetBounds.Width * 0.25f), targetBounds.Bottom);
}
