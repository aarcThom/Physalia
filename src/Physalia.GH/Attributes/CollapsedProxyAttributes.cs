// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using GH_IO.Serialization;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Stand-in attributes that hide a non-Physalia harness member (a native node such as a
/// Number Slider or Panel that has no Physalia collapse flag to honour). On collapse the
/// <see cref="Physalia.GH.Harness.Harness"/> stashes the member's real attributes and assigns this proxy, which
/// reports a zero-size rectangle at the shared collapse point and draws nothing — so the node
/// and its wires disappear while it stays in the document, wired and solving. On expand the
/// original attributes are restored.
///
/// <para>For a component owner the proxy also collapses the owner's parameter grips to the
/// point (a top-level no-op render alone would leave param wires drawn at their old grips); for
/// a standalone parameter owner the proxy's own bounds are the grip.</para>
/// </summary>
public class CollapsedProxyAttributes : GH_Attributes<IGH_DocumentObject>
{
    private PointF _point;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollapsedProxyAttributes"/> class.
    /// </summary>
    /// <param name="owner">The native document object being hidden.</param>
    /// <param name="original">The original attributes to restore on expand.</param>
    /// <param name="point">The shared collapse point (the proxy Chatbox pivot).</param>
    public CollapsedProxyAttributes(IGH_DocumentObject owner, IGH_Attributes original, PointF point)
        : base(owner)
    {
        Original = original;
        _point = point;
        Pivot = point;
        Bounds = new RectangleF(point, SizeF.Empty);
    }

    /// <summary>
    /// Gets the original attributes this proxy replaced, restored by the harness on expand.
    /// </summary>
    public IGH_Attributes Original { get; }

    /// <summary>
    /// Moves the collapse point so a hidden native member tracks the proxy Chatbox if it is
    /// dragged. Expires the layout so the new point takes effect.
    /// </summary>
    /// <param name="point">The new shared collapse point.</param>
    public void UpdatePoint(PointF point)
    {
        _point = point;
        ExpireLayout();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Delegates to the stashed original attributes so a save made while the harness is
    /// collapsed persists the member's true pivot and bounds (the proxy's pivot is the collapse
    /// point, and the base write would also skip type-specific geometry such as a Panel's
    /// user-set bounds — losing the member's real placement from the file).
    /// </remarks>
    public override bool Write(GH_IWriter writer) => Original.Write(writer);

    /// <inheritdoc/>
    /// <remarks>
    /// Symmetric with <see cref="Write"/>. In practice deserialization happens onto the
    /// member's fresh native attributes before the harness re-collapses, so this rarely runs.
    /// </remarks>
    public override bool Read(GH_IReader reader) => Original.Read(reader);

    /// <inheritdoc/>
    public override bool HasInputGrip => false;

    /// <inheritdoc/>
    public override bool HasOutputGrip => false;

    /// <inheritdoc/>
    /// <remarks>
    /// A hidden member must never be the object under the cursor — no hover tooltip, click, or drag.
    /// The zero-size bounds alone would not pick, but a wire relay's own hit region does not derive
    /// from bounds, so refuse the point-pick outright while collapsed.
    /// </remarks>
    public override bool IsPickRegion(PointF point) => false;

    /// <inheritdoc/>
    protected override void Layout()
    {
        Pivot = _point;
        Bounds = new RectangleF(_point, SizeF.Empty);

        if (Owner is IGH_Component component)
        {
            CollapseGuard.CollapseParams(component, _point);
        }
    }

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // Hidden: draw nothing — neither the node nor its wires.
    }
}
