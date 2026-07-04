// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Attributes for the Chat component. The chat UI lives in a standalone window (opened on
/// double-click); on the canvas the Chat doubles as the proxy node for its collapsible
/// harness group. Once it owns members, the node takes on a distinct look — a light-blue
/// body with a lavender-pink edge — so a harness Chat reads distinctly from a plain one.
/// Collapse is driven from the right-click menu and the chat window.
/// </summary>
public class ChatAttrib : PhyComponentAttributes, IArrowHost
{
    // Harness tint: light-blue body, black capsule edge, dark-purple text. A secondary outline
    // (HarnessGlow → white, top-to-bottom) is traced just outside the black edge.
    private static readonly Color HarnessFill = Color.FromArgb(255, 218, 243, 245);
    private static readonly Color HarnessEdge = Color.Black;
    private static readonly Color HarnessText = Color.FromArgb(255, 47, 8, 87);
    private static readonly Color HarnessGlow = Color.FromArgb(255, 236, 0, 150);

    // Width of the secondary gradient outline; ~half straddles outside the 1px black edge.
    private const float GlowWidth = 1f;

    private readonly Chat _chat;

    // The delegated bottom arrow, drawn only while collapsed with exactly one transmitter member
    // (see Harness.TryGetSoleArrow). The proxy hosts the grip + wires and forwards the drag to the
    // real transmitter through IHarnessArrow, so the link/placement is real and survives expansion.
    // The Chat is a bespoke proxy (not a BottomGripAttributes), so it owns its own grip dot.
    private readonly CanvasGrip _grip = new(PointF.Empty);
    private readonly ArrowGrip _arrow = new();

    // Bounds before/after the downward grip expansion: the capsule draws at _visualBounds, while
    // _gripBounds (10px taller) is the object's pick region so the bottom grip is hittable.
    private RectangleF _visualBounds;
    private RectangleF _gripBounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatAttrib"/> class.
    /// </summary>
    /// <param name="chat">The Chat component that owns these attributes.</param>
    public ChatAttrib(Chat chat)
        : base(chat)
    {
        _chat = chat;
    }

    /// <inheritdoc/>
    public PointF ArrowOrigin => GripOrigin();

    /// <inheritdoc/>
    /// <remarks>The delegated arrow uses the Feedback blue→purple regardless of the transmitter.</remarks>
    public WireGradient ArrowGradient => ArrowStyles.Proxy;

    /// <inheritdoc/>
    public IArrowHead ArrowHead => TriangleArrowHead.Default;

    /// <inheritdoc/>
    /// <remarks>The proxy arrow terminates horizontally (rightward tip) toward its target.</remarks>
    public bool HorizontalArrow => true;

    /// <inheritdoc/>
    public IEnumerable<PointF> SettledEndpoints(GH_Document doc)
    {
        if (_chat.Group.TryGetSoleArrow(out IHarnessArrow? source) && source is not null)
        {
            return source.GetArrowEndpoints(doc);
        }

        return System.Array.Empty<PointF>();
    }

    /// <inheritdoc/>
    public void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl)
    {
        if (_chat.Group.TryGetSoleArrow(out IHarnessArrow? source) && source is not null)
        {
            source.HandleDrop(doc, dropPoint, ctrl);
        }
    }

    /// <inheritdoc/>
    protected override void Layout()
    {
        // PhyComponentAttributes handles the collapse guard and the normal GH layout. This Chat
        // may itself be a (plain) member of another harness — when that harness is collapsed, hide
        // it like any member: shrink to the collapse point and skip the proxy chrome.
        base.Layout();

        if (IsHarnessCollapsed)
        {
            _visualBounds = Bounds;
            _gripBounds = Bounds;
            return;
        }

        // While collapsed (as a proxy over its own group), keep the hidden members glued under this
        // (possibly moved) proxy.
        _chat.Group.RefreshCollapsePoint();

        // When collapsed over a single transmitter, expand the pick region 10px downward so the
        // delegated bottom grip is hittable; otherwise the bounds are unchanged.
        _visualBounds = Bounds;
        if (ShowsArrow(out _))
        {
            _gripBounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 10f);
            Bounds = _gripBounds;
        }
        else
        {
            _gripBounds = Bounds;
        }
    }

    // Whether the proxy should host a delegated arrow right now: collapsed with exactly one
    // arrow-bearing member. Returns that member when true.
    private bool ShowsArrow(out IHarnessArrow? arrow)
    {
        arrow = null;
        return _chat.Group.Collapsed && _chat.Group.TryGetSoleArrow(out arrow);
    }

    // Bottom-centre of the visible capsule — the origin of the delegated arrow.
    private PointF GripOrigin() =>
        new(_visualBounds.Left + (_visualBounds.Width / 2f), _visualBounds.Y + _visualBounds.Height);

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // Hidden as a collapsed member of another harness — draw nothing at all.
        if (IsHarnessCollapsed)
        {
            return;
        }

        bool harnessTint = channel == GH_CanvasChannel.Objects && _chat.Group.Count > 0;
        bool arrow = ShowsArrow(out _);

        // A plain Chat (no members), and the channels where neither the tint nor the arrow
        // apply, render as a normal node.
        if (!harnessTint && !(arrow && channel == GH_CanvasChannel.Wires))
        {
            base.Render(canvas, graphics, channel);
            return;
        }

        // The capsule and arrow draw against the un-expanded bounds; restore the pick region after.
        RectangleF gripBounds = Bounds;
        Bounds = _visualBounds;

        if (harnessTint)
        {
            // Draw the delegated grip first so the capsule paints over its top half — only the
            // lower half peeks out below the node, like the transmitters' grips.
            if (arrow)
            {
                _grip.Location = GripOrigin();
                _grip.Draw(graphics);
            }

            // Render the capsule ourselves for a clean chat look: GH would force a jagged
            // "no inputs" left edge and (because the Chat is not preview-capable) the Hidden
            // palette. Rounding both edges and driving fill/edge/text from our own style sidesteps
            // both. The output grip shows only while expanded; a collapsed proxy carries no grips.
            var style = new GH_PaletteStyle(HarnessFill, HarnessEdge, HarnessText);
            RenderSmoothCapsule(canvas, graphics, style);

            // Secondary outline on top, hugging the inside of the black edge: a pink→white gradient
            // traced on the exact capsule shape. A CompoundArray restricts the fat pen to its inner
            // band (GH's own inner-shine trick), so the stroke stays inside the path and the black
            // edge survives on the outside.
            DrawHarnessGlow(graphics);
        }
        else if (arrow)
        {
            _arrow.DrawWires(graphics, canvas.Document, this);
        }

        Bounds = gripBounds;
    }

    // Mirrors GH_ComponentAttributes.RenderComponentCapsule but rounds both edges
    // (SetJaggedEdges(false, false)) and draws the fill/edge/text from our own palette style,
    // skipping GH's jagged-left + Hidden-palette behaviour for a not-preview-capable component.
    // The output grip is added only while the harness is expanded.
    private void RenderSmoothCapsule(GH_Canvas canvas, Graphics graphics, GH_PaletteStyle style)
    {
        RectangleF rec = Bounds;
        bool visible = canvas.Viewport.IsVisible(ref rec, 10f);
        Bounds = rec;
        if (!visible)
        {
            return;
        }

        var capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Normal);
        try
        {
            capsule.SetJaggedEdges(false, false);

            // No inputs ever; the output grip is the proxy's only grip and is hidden while collapsed.
            if (!_chat.Group.Collapsed)
            {
                foreach (IGH_Param output in _chat.Params.Output)
                {
                    capsule.AddOutputGrip(output.Attributes.OutputGrip.Y);
                }
            }

            graphics.SmoothingMode = SmoothingMode.HighQuality;
            canvas.SetSmartTextRenderingHint();

            if (!string.IsNullOrWhiteSpace(_chat.Message))
            {
                capsule.RenderEngine.RenderMessage(graphics, _chat.Message, style);
            }

            capsule.Render(graphics, style);

            bool iconMode = _chat.IconDisplayMode == GH_IconDisplayMode.icon
                || (_chat.IconDisplayMode == GH_IconDisplayMode.application && Grasshopper.CentralSettings.CanvasObjectIcons);

            if (iconMode)
            {
                Image? icon = _chat.Locked ? _chat.Icon_24x24_Locked : _chat.Icon_24x24;
                if (icon != null)
                {
                    capsule.RenderEngine.RenderIcon(graphics, icon, m_innerBounds);
                }
            }
            else
            {
                var text = GH_Capsule.CreateTextCapsule(
                    m_innerBounds, m_innerBounds, GH_Palette.Black, _chat.NickName,
                    GH_FontServer.LargeAdjusted, GH_Orientation.vertical_center, 3, 6);
                text.Render(graphics, Selected, _chat.Locked, hidden: false);
                text.Dispose();
            }

            RenderComponentParameters(canvas, graphics, _chat, style);
        }
        finally
        {
            capsule.Dispose();
        }
    }

    // Traces the exact capsule silhouette (jagged input edge included) with a fat pen filled by a
    // vertical pink→white gradient, restricted by a CompoundArray to the pen's inner half so the
    // stroke lands just inside the black edge. Drawn on top of the fill: the rim fades from pink
    // at the bottom to white on top, with the black capsule edge still visible outside it.
    private void DrawHarnessGlow(Graphics graphics)
    {
        var capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Hidden);
        try
        {
            // Both edges rounded, matching the capsule drawn in RenderSmoothCapsule.
            capsule.SetJaggedEdges(false, false);
            GraphicsPath? outline = capsule.OutlineShape;
            if (outline is null)
            {
                return;
            }

            // White at the top of the node, pink at the bottom.
            using var brush = new LinearGradientBrush(
                RectangleF.Inflate(Bounds, 2f, 2f), Color.White, HarnessGlow, LinearGradientMode.Vertical);

            // Pen centred on the path; CompoundArray draws only the inner band (1f is the inner
            // edge of the stroke, as in GH's InnerContourPen), keeping it inside the path.
            using var pen = new Pen(brush, GlowWidth * 2f)
            {
                LineJoin = LineJoin.Round,
                CompoundArray = new[] { 0.5f, 1f },
            };

            SmoothingMode prev = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawPath(pen, outline);
            graphics.SmoothingMode = prev;
        }
        finally
        {
            capsule.Dispose();
        }
    }

    /// <summary>
    /// Begins a delegated arrow drag when the bottom grip is pressed (only while collapsed over a
    /// single transmitter). The grip hit zone is just the bottom-centre handle, so the rest of the
    /// proxy stays free to select and move.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Capture if the grip was hit; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left && ShowsArrow(out _) && GripHitZone().Contains(e.CanvasLocation))
        {
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            _arrow.StartDrag(sender, e.CanvasLocation, ctrl);
            return GH_ObjectResponse.Capture;
        }

        return base.RespondToMouseDown(sender, e);
    }

    /// <summary>
    /// Updates the live drag wire as the user moves the mouse.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a drag is in progress; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_arrow.IsDragging)
        {
            _arrow.UpdateDrag(sender, e.CanvasLocation);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseMove(sender, e);
    }

    /// <summary>
    /// Completes the drag, forwarding the drop to the contained transmitter through
    /// <see cref="IHarnessArrow.HandleDrop"/> so the real link/placement is updated.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a drag was in progress; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_arrow.IsDragging)
        {
            _arrow.EndDrag(sender, sender.Document, e.CanvasLocation, this);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseUp(sender, e);
    }

    // A small box around the bottom-centre grip (not the whole body) so dragging the rest of the
    // proxy still moves it. Spans the grip circle and the 10px expansion strip below the node.
    private RectangleF GripHitZone()
    {
        PointF o = GripOrigin();
        return new RectangleF(o.X - 8f, o.Y - 8f, 16f, 18f);
    }

    /// <summary>
    /// Opens the chat window on double-click.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled — the double-click is consumed to open the window.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        _chat.OpenWindow();
        _chat.Attributes.Selected = false;
        sender.Refresh();
        return GH_ObjectResponse.Handled;
    }
}
