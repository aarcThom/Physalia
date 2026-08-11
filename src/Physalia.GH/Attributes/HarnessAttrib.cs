// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Attributes for the harness proxy. The node is drawn as a distinct capsule — light-blue body,
/// black edge, pink-to-white inner rim — so a harness reads at a glance as a container rather than
/// a working component.
///
/// <para>The proxy is the ONLY door onto the chat window: <b>double-click opens it</b> on the Chat
/// inside, and <b>right-click → "Edit Harness"</b> takes the canvas inside. The Chat itself answers
/// no gesture — it lives in the sub-document, where the user only reaches it deliberately.</para>
///
/// <para>The proxy also grows ONE drag grip per transmitter inside the harness — its outlets (see
/// <see cref="IHarnessOutlet"/>) — stacked down the right edge, each labelled ("node", "py") and
/// painted in that transmitter's own wire colour. The drag has to happen out here because the
/// things a transmitter points at live on the user's canvas, and a drag cannot cross two canvases.
/// The capsule grows taller to fit them; with no transmitters inside it stays a plain bar with no
/// grips at all.</para>
/// </summary>
public class HarnessAttrib : BottomGripAttributes
{
    // How much wider the proxy is than the capsule Grasshopper would give it. A harness stands for a
    // whole pipeline, and with no parameters at all GH's own layout would make it one of the smallest
    // nodes on the canvas. The base height is left alone — a node-height bar reads as a node — until
    // outlets need more room than that.
    private const float WidthFactor = 3f;

    // Inset from the capsule to the region the icon (or the nickname) is drawn in, keeping it clear of
    // the gradient rim.
    private const float ContentInset = 2f;

    // Vertical room each outlet grip claims. The capsule grows to n * this, so two grips are far
    // enough apart to aim at and to read their labels between.
    private const float RowHeight = 20f;

    // Size of one Chat's emoji, and the space between two of them. The bundled emoji bitmaps are
    // 24x24, so drawing them at that size keeps them crisp.
    private const float EmojiSize = 24f;
    private const float EmojiGap = 4f;

    // Gap between the labels and the capsule's right edge, so a tag never crowds its grip.
    private const float LabelInset = 7f;

    // Bounds on the strip reserved along the right edge for the outlet labels. The strip is measured
    // from the labels themselves — a fixed width cropped "node" to "nod" — but never so wide that a
    // long tag would crowd the mark out of its own capsule.
    private const float MinLabelColumn = 24f;
    private const float MaxLabelColumn = 72f;

    // Below this canvas zoom the labels are dropped, the way Grasshopper drops parameter names: the
    // adjusted font would render them as unreadable smears over the node.
    private const float LabelZoomFloor = 0.6f;

    private readonly HarnessComponent _harness;

    // One handle per outlet, rebuilt only when the harness's transmitters actually change so an
    // in-flight drag (and each arrow's cached wire geometry) survives a relayout.
    private readonly List<OutletHandle> _handles = new();

    // The Chats inside the harness, in switcher-row order — the proxy wears their emoji. Held as
    // components rather than as bitmaps so a Chat that re-rolls its emoji is picked up on the next
    // paint; each Chat caches its own icon, so asking per frame costs nothing.
    private readonly List<Chat> _chats = new();

    private OutletHandle? _dragging;

    // Width of the label strip, measured at layout from the labels actually present.
    private float _labelColumn;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessAttrib"/> class.
    /// </summary>
    /// <param name="harness">The harness component that owns these attributes.</param>
    public HarnessAttrib(HarnessComponent harness)
        : base(harness)
    {
        _harness = harness;
    }

    // The harness livery normally, Grasshopper's own selection palette while selected. Selection has to
    // read as selection — a node with a private colour scheme that ignores it looks broken next to every
    // other one — and GH_Skin is where that green comes from, so a canvas theme change follows along.
    private GH_PaletteStyle CapsuleStyle => Selected
        ? GH_Skin.palette_normal_selected
        : HarnessTheme.Style;

    /// <inheritdoc/>
    /// <remarks>
    /// The right-edge midpoint, where a Grasshopper output leaves from — and where a single outlet's
    /// grip lands. With more than one the grips are spread from here by
    /// <see cref="PositionHandles"/>, which is what the proxy actually draws and hit-tests against.
    /// </remarks>
    protected override PointF GripOrigin => RightCentre;

    // The capsule width the contents actually need: the row of Chat emoji, the outlet labels beside
    // it, and the insets either side. Only wider than the default proxy width once a harness holds
    // enough Chats to fill it.
    private float ContentWidth => EmojiStripWidth + _labelColumn + (ContentInset * 4f);

    // The row of emoji, or nothing at all when the harness holds no Chat — an empty harness keeps the
    // plug-in's own mark and needs no room reserved for a row that is not there.
    private float EmojiStripWidth => _chats.Count == 0
        ? 0f
        : (_chats.Count * EmojiSize) + ((_chats.Count - 1) * EmojiGap);

    /// <summary>
    /// Opens the chat window on double-click, on the Chat inside the harness. The proxy stands in
    /// for that Chat, so it answers the same gesture; entering the harness is the right-click item.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled when a Chat was found; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_harness.FindChat() is not { } chat)
        {
            return base.RespondToMouseDoubleClick(sender, e);
        }

        chat.OpenWindow();
        Selected = false;
        sender.Refresh();
        return GH_ObjectResponse.Handled;
    }

    /// <inheritdoc/>
    /// <remarks>A press starts a drag only when it lands on one particular outlet's grip.</remarks>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left && HandleAt(e.CanvasLocation) is { } handle)
        {
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            _dragging = handle;
            handle.Grip.StartDrag(sender, e.CanvasLocation, ctrl);
            return GH_ObjectResponse.Capture;
        }

        return base.RespondToMouseDown(sender, e);
    }

    /// <inheritdoc/>
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_dragging is { } handle && handle.Grip.IsDragging)
        {
            handle.Grip.UpdateDrag(sender, e.CanvasLocation);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseMove(sender, e);
    }

    /// <inheritdoc/>
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_dragging is { } handle && handle.Grip.IsDragging)
        {
            handle.Grip.EndDrag(sender, sender.Document, e.CanvasLocation, handle);
            _dragging = null;
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseUp(sender, e);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Widens the capsule, keeping its left edge where Grasshopper put it — so the node reaches
    /// rightwards from the spot it was placed at rather than jumping — and grows it downward when the
    /// outlets need more height than one node-row. A harness with several Chats in it stretches
    /// further right still, enough to show every emoji beside the outlet labels.
    /// </remarks>
    protected override RectangleF AdjustVisualBounds(RectangleF bounds) =>
        new(
            bounds.X,
            bounds.Y,
            Math.Max(bounds.Width * WidthFactor, ContentWidth),
            Math.Max(bounds.Height, _handles.Count * RowHeight));

    /// <inheritdoc/>
    /// <remarks>The grips are on the right, so the hittable strip goes there rather than underneath.</remarks>
    protected override RectangleF ExpandForGrip(RectangleF visual) =>
        new(visual.X, visual.Y, visual.Width + GripExpansion, visual.Height);

    /// <inheritdoc/>
    /// <remarks>Only expands the pick region for the grips when there are outlets to host.</remarks>
    protected override void Layout()
    {
        // All before base.Layout(), because AdjustVisualBounds sizes the capsule from the outlet
        // count, the emoji count and the width the labels need.
        RefreshHandles();
        RefreshChats();
        _labelColumn = MeasureLabelColumn();

        base.Layout();

        // Grasshopper sized the inner region for the small capsule it thought it was laying out, so the
        // icon (or the nickname) would sit in a corner of the grown one. Re-centre it on the capsule,
        // less the strip the outlet labels occupy.
        RectangleF inner = RectangleF.Inflate(VisualBounds, -ContentInset, -ContentInset);
        m_innerBounds = _handles.Count == 0
            ? inner
            : new RectangleF(inner.X, inner.Y, Math.Max(1f, inner.Width - _labelColumn), inner.Height);

        PositionHandles();

        if (_handles.Count == 0)
        {
            Bounds = VisualBounds;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Composed by hand rather than through the base render: the harness draws a bespoke capsule,
    /// so it cannot fall through to <see cref="GH_ComponentAttributes"/>'s.
    /// </remarks>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // The capsule, grips and arrows all draw against the un-expanded bounds; restore the pick
        // region afterwards so hit-testing still covers the grip strip.
        RectangleF outer = Bounds;
        Bounds = VisualBounds;

        if (channel == GH_CanvasChannel.Objects)
        {
            // Grips first, so the capsule paints over their inner halves and only the outer parts peek
            // past the node's edge — the same look the transmitters used to have.
            foreach (OutletHandle handle in _handles)
            {
                DrawGrip(graphics, handle.Origin);
            }

            RenderSmoothCapsule(canvas, graphics, CapsuleStyle);

            // The rim is drawn in both states: it is the harness's signature, and the point of the
            // selection colour is to answer "is this selected", which the body already does.
            HarnessTheme.DrawGlow(graphics, Bounds);

            // Labels last: they belong on top of the capsule, not under it.
            DrawOutletLabels(canvas, graphics);
        }

        // The arrow wires (Wires channel).
        RenderGripContent(canvas, graphics, channel);

        Bounds = outer;
    }

    /// <inheritdoc/>
    /// <remarks>Each outlet draws its own settled wires, in its own colour.</remarks>
    protected override void RenderGripContent(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Wires)
        {
            return;
        }

        foreach (OutletHandle handle in _handles)
        {
            handle.Grip.DrawWires(graphics, canvas.Document, handle);
        }
    }

    // Rebuilds the handle list when the harness's set of transmitters has changed. Identity is
    // preserved otherwise: a handle owns an ArrowGrip holding the live drag state, so replacing them
    // wholesale on every relayout would drop a drag mid-gesture.
    private void RefreshHandles()
    {
        IReadOnlyList<IHarnessOutlet> outlets = _harness.Outlets;

        if (_handles.Count == outlets.Count)
        {
            bool unchanged = true;
            for (int i = 0; i < outlets.Count; i++)
            {
                if (!ReferenceEquals(_handles[i].Outlet, outlets[i]))
                {
                    unchanged = false;
                    break;
                }
            }

            if (unchanged)
            {
                return;
            }
        }

        _handles.Clear();
        _dragging = null;

        foreach (IHarnessOutlet outlet in outlets)
        {
            _handles.Add(new OutletHandle(outlet));
        }
    }

    // Re-reads the Chats inside the harness. Cheap enough at layout, and it keeps the per-frame
    // render off the sub-document's object list.
    private void RefreshChats()
    {
        _chats.Clear();
        _chats.AddRange(_harness.Chats);
    }

    // Spreads the grips evenly down the right edge of the capsule. With one outlet this lands exactly
    // on the right-edge midpoint, where a Grasshopper output leaves from.
    private void PositionHandles()
    {
        RectangleF capsule = VisualBounds;
        for (int i = 0; i < _handles.Count; i++)
        {
            float y = capsule.Y + (capsule.Height * (i + 0.5f) / _handles.Count);
            _handles[i].Origin = new PointF(capsule.Right, y);
        }
    }

    // The outlet whose grip a canvas point lands on, or null. The hit patch is deliberately much
    // smaller than the pick region, which covers the whole node: pressing anywhere else has to keep
    // meaning "move me".
    private OutletHandle? HandleAt(PointF point)
    {
        foreach (OutletHandle handle in _handles)
        {
            var hit = new RectangleF(
                handle.Origin.X - GripExpansion,
                handle.Origin.Y - (RowHeight / 2f),
                GripExpansion * 2f,
                RowHeight);

            if (hit.Contains(point))
            {
                return handle;
            }
        }

        return null;
    }

    // The short tag beside each grip ("node", "py"), right-aligned against the capsule edge. Dropped
    // when zoomed out, as Grasshopper drops parameter names.
    //
    // Drawn from a measured POINT rather than into a rectangle: a rectangle clips, and clipping is
    // what turned "node" into "nod". Measuring here also means the width comes from the very font the
    // text is drawn with, which the layout pass cannot know — GH_FontServer's adjusted font follows
    // the canvas zoom, and layout does not re-run when you zoom.
    private void DrawOutletLabels(GH_Canvas canvas, Graphics graphics)
    {
        if (_handles.Count == 0 || canvas.Viewport.Zoom < LabelZoomFloor)
        {
            return;
        }

        using var ink = new SolidBrush(HarnessTheme.Ink);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap,
        };

        Font font = GH_FontServer.StandardAdjusted;
        float right = VisualBounds.Right - LabelInset;

        foreach (OutletHandle handle in _handles)
        {
            string label = handle.Outlet.OutletLabel;
            SizeF size = graphics.MeasureString(label, font, PointF.Empty, format);

            graphics.DrawString(
                label,
                font,
                ink,
                Math.Max(m_innerBounds.Left, right - size.Width),
                handle.Origin.Y - (size.Height / 2f),
                format);
        }
    }

    // The strip the labels need along the right edge, measured off-screen because Layout has no
    // Graphics of its own. Only the icon's region depends on this — the labels themselves are placed
    // from their own measurement at draw time — so the unadjusted font is close enough here.
    private float MeasureLabelColumn()
    {
        if (_handles.Count == 0)
        {
            return 0f;
        }

        using var surface = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(surface);

        float widest = 0f;
        foreach (OutletHandle handle in _handles)
        {
            widest = Math.Max(widest, graphics.MeasureString(handle.Outlet.OutletLabel, GH_FontServer.Standard).Width);
        }

        return Math.Clamp(widest + LabelInset, MinLabelColumn, MaxLabelColumn);
    }

    // Mirrors GH_ComponentAttributes.RenderComponentCapsule but rounds both edges and drives
    // fill/edge/text from our own palette style: the harness has no parameters at all, so GH would
    // otherwise force a jagged "no inputs" left edge and, because the node is not preview-capable,
    // the Hidden palette.
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

            graphics.SmoothingMode = SmoothingMode.HighQuality;
            canvas.SetSmartTextRenderingHint();

            // No RenderMessage call: the proxy deliberately carries no message tag, so the black
            // caption Grasshopper hangs under a node can never appear beneath a harness.
            capsule.Render(graphics, style);

            bool iconMode = _harness.IconDisplayMode == GH_IconDisplayMode.icon
                || (_harness.IconDisplayMode == GH_IconDisplayMode.application && Grasshopper.CentralSettings.CanvasObjectIcons);

            if (iconMode)
            {
                if (_chats.Count > 0)
                {
                    RenderChatEmoji(capsule, graphics);
                }
                else
                {
                    Image? icon = _harness.Locked ? _harness.Icon_24x24_Locked : _harness.Icon_24x24;
                    if (icon is not null)
                    {
                        capsule.RenderEngine.RenderIcon(graphics, icon, m_innerBounds);
                    }
                }
            }
            else
            {
                var text = GH_Capsule.CreateTextCapsule(
                    m_innerBounds, m_innerBounds, GH_Palette.Black, _harness.NickName,
                    GH_FontServer.LargeAdjusted, GH_Orientation.vertical_center, 3, 6);
                text.Render(graphics, Selected, _harness.Locked, hidden: false);
                text.Dispose();
            }
        }
        finally
        {
            capsule.Dispose();
        }
    }

    // The harness's face: one emoji per Chat inside it, in the order the chat window's switcher row
    // shows them, so the node on the canvas and the row of circles in the window are plainly the same
    // list. A harness IS its conversations — the plug-in's own mark says nothing a user needs once
    // there is a Chat in there, and it is what an empty harness falls back to.
    //
    // Each Chat's icon is asked for at paint time rather than cached here, so a Chat that re-rolls its
    // emoji (the dedupe on placement) is right on the next frame.
    private void RenderChatEmoji(GH_Capsule capsule, Graphics graphics)
    {
        float size = Math.Min(EmojiSize, m_innerBounds.Height);
        float strip = (_chats.Count * size) + ((_chats.Count - 1) * EmojiGap);

        float x = m_innerBounds.X + ((m_innerBounds.Width - strip) / 2f);
        float y = m_innerBounds.Y + ((m_innerBounds.Height - size) / 2f);

        foreach (Chat chat in _chats)
        {
            Image? icon = _harness.Locked ? chat.Icon_24x24_Locked : chat.Icon_24x24;
            if (icon is not null)
            {
                capsule.RenderEngine.RenderIcon(graphics, icon, new RectangleF(x, y, size, size));
            }

            x += size + EmojiGap;
        }
    }

    /// <summary>
    /// One outlet's arrow: the grip's position on the proxy plus the drag/wire mechanics, with the
    /// outlet itself supplying colour, endpoints and what a drop means.
    ///
    /// <para>This is why the proxy composes <see cref="ArrowGrip"/> rather than deriving from
    /// <see cref="ArrowAttributeBase"/> as the single-arrow components do: a harness hosts as many
    /// arrows as it holds transmitters, each with its own drag state.</para>
    /// </summary>
    private sealed class OutletHandle : IArrowHost
    {
        internal OutletHandle(IHarnessOutlet outlet)
        {
            Outlet = outlet;
        }

        /// <summary>Gets the transmitter this grip stands for.</summary>
        internal IHarnessOutlet Outlet { get; }

        /// <summary>Gets this outlet's own arrow controller, holding its wires and drag state.</summary>
        internal ArrowGrip Grip { get; } = new();

        /// <summary>Gets or sets where the grip sits on the proxy, set at layout.</summary>
        internal PointF Origin { get; set; }

        /// <inheritdoc/>
        public PointF ArrowOrigin => Origin;

        /// <inheritdoc/>
        public WireGradient ArrowGradient => Outlet.OutletGradient;

        /// <inheritdoc/>
        public IArrowHead ArrowHead => TriangleArrowHead.Default;

        /// <inheritdoc/>
        /// <remarks>
        /// The grips sit on the right edge, so the wire sets off rightwards and arrives with a
        /// rightward tip rather than diving under the node first.
        /// </remarks>
        public bool HorizontalArrow => true;

        /// <inheritdoc/>
        public IEnumerable<PointF> SettledEndpoints(GH_Document doc) => Outlet.GetArrowEndpoints(doc);

        /// <inheritdoc/>
        public void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl) =>
            Outlet.HandleDrop(doc, dropPoint, ctrl);
    }
}
