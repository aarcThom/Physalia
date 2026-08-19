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
///
/// <para>Down the LEFT edge it grows one ordinary Grasshopper input per Receiver inside — its inlets
/// (see <see cref="IHarnessInlet"/>). Those are real parameters, so Grasshopper lays them out itself;
/// this class only has to draw them, because the capsule here is composed by hand and never reaches
/// <see cref="GH_ComponentAttributes"/>'s own render. It also has to TRANSLATE them: Grasshopper sizes
/// the capsule from the parameters and this class then grows it for the outlets and the emoji row, so
/// the rows are re-centred on the grown capsule rather than left clinging to the top of it.</para>
/// </summary>
public class HarnessAttrib : BottomGripAttributes
{
    // How much wider the proxy is than the capsule Grasshopper would give it. A harness stands for a
    // whole pipeline, and with no parameters at all GH's own layout would make it one of the smallest
    // nodes on the canvas. The base height is left alone — a node-height bar reads as a node — until
    // outlets need more room than that.
    private const float WidthFactor = 2.35f;

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

    // Gap between an input's row and the name drawn in it, matching the breathing room Grasshopper
    // leaves on an ordinary component.
    private const float InputLabelInset = 3f;

    // Strip reserved along the right edge for the outlet labels — room for a short tag at 1:1 zoom,
    // which is what a canvas unit means.
    //
    // Fixed, NOT measured. Measuring looks tempting but cannot work here: layout runs in canvas units
    // while GH_FontServer's adjusted font follows the canvas zoom, and layout does not re-run when you
    // zoom — so a measured column reserved a third of the node at high zoom and left a hole between
    // the emoji and a four-letter tag. Nothing depends on it being exact, either: the labels are drawn
    // from their own measurement at paint time, so they cannot clip whatever this says.
    private const float LabelColumn = 30f;

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

    // How far the input rows must move to sit centred in the grown capsule, measured in
    // AdjustVisualBounds (the only place that sees both the size Grasshopper chose and the size the
    // outlets forced) and applied once the layout pass is over.
    private float _inputShift;

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
    // it, the strip Grasshopper laid the input names out in, and the insets either side. Only wider
    // than the default proxy width once a harness holds enough Chats to fill it.
    private float ContentWidthFrom(float left) =>
        EmojiStripWidth + LabelColumn + InputColumnFrom(left) + (ContentInset * 4f);

    // The strip along the left edge that the input rows occupy, measured from what Grasshopper's own
    // layout produced rather than guessed at — it sizes each row from the parameter name, which is a
    // Receiver's nickname and so any length at all. Zero when the harness holds no Receiver.
    //
    // The capsule's left edge is passed in rather than read from VisualBounds, because the measuring
    // pass needs this BEFORE VisualBounds has been recomputed — and on the very first layout that
    // field is still empty, which would make the column the whole distance from the canvas origin.
    private float InputColumnFrom(float left)
    {
        float right = left;
        foreach (IGH_Param param in _harness.Params.Input)
        {
            if (param.Attributes is { } attributes)
            {
                right = Math.Max(right, attributes.Bounds.Right);
            }
        }

        return Math.Max(0f, right - left);
    }

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
    protected override RectangleF AdjustVisualBounds(RectangleF bounds)
    {
        float height = Math.Max(bounds.Height, _handles.Count * RowHeight);

        // Recorded, not applied: the input rows are Grasshopper's own attributes and moving them from
        // inside the measuring pass would have this method reading bounds it had just changed.
        _inputShift = (height - bounds.Height) / 2f;

        // The widening factor applies only to a harness with no inputs. With inputs, Grasshopper has
        // already sized the capsule around the Receiver names, and multiplying THAT would give a node
        // stretching half the canvas for three short labels.
        float minimum = _harness.Params.Input.Count == 0
            ? bounds.Width * WidthFactor
            : bounds.Width;

        return new RectangleF(
            bounds.X,
            bounds.Y,
            Math.Max(minimum, ContentWidthFrom(bounds.X)),
            height);
    }

    /// <inheritdoc/>
    /// <remarks>The grips are on the right, so the hittable strip goes there rather than underneath.</remarks>
    protected override RectangleF ExpandForGrip(RectangleF visual) =>
        new(visual.X, visual.Y, visual.Width + GripExpansion, visual.Height);

    /// <inheritdoc/>
    /// <remarks>Only expands the pick region for the grips when there are outlets to host.</remarks>
    protected override void Layout()
    {
        // All three before base.Layout(), because AdjustVisualBounds sizes the capsule from the outlet
        // count and the emoji count — and Grasshopper measures each input row from its parameter name,
        // so a Receiver renamed inside the harness has to be picked up before that, not after.
        RefreshHandles();
        RefreshChats();
        _harness.RefreshInlets();

        base.Layout();

        // The capsule grew downward for the outlets after Grasshopper had already placed the input
        // rows against the shorter one; re-centre them on what is actually drawn. A pure translation,
        // so every point derived from a row — the input grip a wire lands on above all — moves with it.
        ShiftInputParams(_inputShift);

        // Grasshopper sized the inner region for the small capsule it thought it was laying out, so the
        // icon (or the nickname) would sit in a corner of the grown one. Re-centre it on the capsule,
        // less the strips the input names and the outlet labels occupy.
        RectangleF inner = RectangleF.Inflate(VisualBounds, -ContentInset, -ContentInset);
        float left = InputColumnFrom(VisualBounds.X);
        float right = _handles.Count == 0 ? 0f : LabelColumn;
        m_innerBounds = new RectangleF(
            inner.X + left,
            inner.Y,
            Math.Max(1f, inner.Width - left - right),
            inner.Height);

        PositionHandles();

        if (_handles.Count == 0)
        {
            Bounds = VisualBounds;
        }
    }

    // Slides the input rows down (or up) by the amount the capsule grew, so they read as belonging to
    // the node rather than hanging off its top edge. Bounds and Pivot both move: Grasshopper derives
    // the input grip from them, and a grip left behind would take wires to the wrong row.
    private void ShiftInputParams(float dy)
    {
        if (Math.Abs(dy) < 0.01f)
        {
            return;
        }

        foreach (IGH_Param param in _harness.Params.Input)
        {
            if (param.Attributes is not { } attributes)
            {
                continue;
            }

            RectangleF bounds = attributes.Bounds;
            attributes.Bounds = new RectangleF(bounds.X, bounds.Y + dy, bounds.Width, bounds.Height);
            attributes.Pivot = new PointF(attributes.Pivot.X, attributes.Pivot.Y + dy);
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
            DrawInputLabels(canvas, graphics);

            Bounds = outer;
            return;
        }

        // Every other channel — the Wires channel above all — goes to the base, which draws this
        // proxy's outlet arrows and then hands on to Grasshopper's own render.
        //
        // That last hop is what draws the wires ARRIVING at the inputs, and skipping it is exactly
        // what this method used to do: the Objects channel is composed here by hand, so the base was
        // never called at all. It went unnoticed for as long as a harness had no inputs and there was
        // no incoming wire to lose — the data still crossed, because delivery is the solver's business
        // and has nothing to do with what is painted.
        base.Render(canvas, graphics, channel);

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

    // The name of each input, in the row Grasshopper laid out for it. Drawn here because this class
    // composes its own capsule and so never reaches the base render that would normally draw them —
    // without this the parameters would be laid out, wireable and completely invisible.
    //
    // The parameter's own nickname, which the Receiver and the parameter keep in step between them by
    // overriding the virtual NickName setter at both ends — so this is both what the user typed, if
    // they renamed the input out here, and what the Receiver is called, if they renamed it inside.
    // Drawn from a measured point rather than clipped into the row, so a name that outgrew the width
    // Grasshopper last measured overhangs rather than losing its tail.
    private void DrawInputLabels(GH_Canvas canvas, Graphics graphics)
    {
        if (_harness.Params.Input.Count == 0 || canvas.Viewport.Zoom < LabelZoomFloor)
        {
            return;
        }

        using var ink = new SolidBrush(HarnessTheme.Ink);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap,
        };

        Font font = GH_FontServer.StandardAdjusted;

        foreach (IGH_Param param in _harness.Params.Input)
        {
            if (param.Attributes is not { } attributes)
            {
                continue;
            }

            RectangleF row = attributes.Bounds;
            SizeF size = graphics.MeasureString(param.NickName, font, PointF.Empty, format);

            graphics.DrawString(
                param.NickName,
                font,
                ink,
                row.X + InputLabelInset,
                row.Y + ((row.Height - size.Height) / 2f),
                format);
        }
    }

    // Mirrors GH_ComponentAttributes.RenderComponentCapsule but rounds both edges and drives
    // fill/edge/text from our own palette style: the harness has no parameters of its own, so GH would
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

            // The nub each input wire lands on. Visual only — the parameter's own bounds are what
            // Grasshopper hit-tests a wire drag against, and Layout has already placed those.
            foreach (IGH_Param input in _harness.Params.Input)
            {
                if (input.Attributes is { HasInputGrip: true } attributes)
                {
                    capsule.AddInputGrip(attributes.InputGrip.Y);
                }
            }

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

        // Centred in the content region, but never starting left of it: the capsule is sized to hold
        // the row, and if that ever fails the emoji must run out over the labels rather than out of
        // the node altogether.
        float x = Math.Max(m_innerBounds.X, m_innerBounds.X + ((m_innerBounds.Width - strip) / 2f));
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
        /// The grips sit on the right edge, so every outlet's wire sets off rightwards rather than
        /// diving under the proxy first.
        /// </remarks>
        public bool HorizontalArrow => true;

        /// <inheritdoc/>
        /// <remarks>The arrival is the outlet's own call — see <see cref="IHarnessOutlet"/>.</remarks>
        public bool HorizontalArrowEnd => Outlet.HorizontalArrowEnd;

        /// <inheritdoc/>
        public IEnumerable<PointF> SettledEndpoints(GH_Document doc) => Outlet.GetArrowEndpoints(doc);

        /// <inheritdoc/>
        public void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl) =>
            Outlet.HandleDrop(doc, dropPoint, ctrl);
    }
}
