// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the Delegate tool. Renders a bezier wire from the bottom-centre grip to the
/// harness this tool calls. Drag to link; Ctrl+drag to unlink.
///
/// <para>A drag is enough here, unlike the script transmitters' picker menus: the harness being
/// called sits in the SAME document as this node — a harness inside a harness — so there is no
/// boundary for the drag to cross. That is also the composition being expressed, and it is why the
/// wire is worth drawing: what it points at is the sub-pipeline, right there on the canvas.</para>
/// </summary>
public class DelegateAttrib : GripLinkAttrib
{
    private readonly DelegateTool _delegate;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateAttrib"/> class.
    /// </summary>
    /// <param name="tool">The Delegate component that owns these attributes.</param>
    public DelegateAttrib(DelegateTool tool)
        : base(tool)
    {
        _delegate = tool;
    }

    /// <inheritdoc/>
    public override WireGradient ArrowGradient => ArrowStyles.Delegate;

    /// <inheritdoc/>
    protected override IEnumerable<Guid> LinkedTargets
    {
        get
        {
            if (_delegate.LinkedGuid != Guid.Empty)
            {
                yield return _delegate.LinkedGuid;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only a harness. A delegated task needs a Conversation Log, a model and its own solve state —
    /// a whole pipeline — and a harness is the unit that holds one.
    /// </remarks>
    protected override bool IsValidTarget(IGH_DocumentObject obj) => obj is HarnessComponent;

    /// <inheritdoc/>
    protected override void OnConnect(Guid targetGuid) => _delegate.LinkTo(targetGuid);

    /// <inheritdoc/>
    protected override void OnDisconnect(Guid targetGuid) => _delegate.Unlink();

    /// <inheritdoc/>
    /// <remarks>
    /// The harness proxy's left edge already carries its inlets and its right edge the outlets, so the
    /// wire lands on the TOP, centred, where nothing else of the harness's own is drawn.
    /// </remarks>
    protected override PointF GetTargetAnchor(RectangleF targetBounds)
        => new PointF(targetBounds.Left + (targetBounds.Width * 0.5f), targetBounds.Top);
}
