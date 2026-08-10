// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Draws the Harness Notes panel: a titled block of wrapped text in the harness family's livery
/// (see <see cref="HarnessTheme"/>).
///
/// <para>Shaped like a Grasshopper panel rather than a component capsule — a title strip with the
/// nickname, then the note itself — because that is what it is. It has no parameters, so nothing here
/// has to make room for grips; the width is the user's, and the height follows the text.</para>
/// </summary>
public class HarnessNotesAttrib : PhyComponentAttributes
{
    /// <summary>Narrowest legible panel, in canvas units.</summary>
    internal const float MinWidth = 120f;

    /// <summary>Width a fresh panel starts at.</summary>
    internal const float DefaultWidth = 240f;

    // Title strip height, and the padding around the note text.
    private const float TitleHeight = 20f;
    private const float PaddingX = 6f;
    private const float PaddingY = 5f;

    // Shortest body the panel will draw, so an empty note is still a panel rather than a sliver.
    private const float MinBodyHeight = 28f;

    private readonly HarnessNotes _notes;

    // The body rectangle the note is drawn into, and the title strip above it. Computed in Layout.
    private RectangleF _titleBounds;
    private RectangleF _bodyBounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessNotesAttrib"/> class.
    /// </summary>
    /// <param name="notes">The Harness Notes component that owns these attributes.</param>
    public HarnessNotesAttrib(HarnessNotes notes)
        : base(notes)
    {
        _notes = notes;
    }

    // The placeholder shown while the note is empty, so the panel explains itself.
    private static string Placeholder => "Double-click to describe this harness.";

    /// <inheritdoc/>
    /// <remarks>
    /// Sizes the panel to its text: the width is whatever the component holds, and the height is the
    /// title strip plus however many lines the note wraps to at that width. Does NOT call
    /// base.Layout() — there are no parameters to place, and the base would size a component capsule
    /// instead of a panel.
    /// </remarks>
    protected override void Layout()
    {
        float width = _notes.Width;
        float bodyHeight = MeasureBody(width);

        Bounds = new RectangleF(Pivot.X, Pivot.Y, width, TitleHeight + bodyHeight);
        _titleBounds = new RectangleF(Bounds.X, Bounds.Y, width, TitleHeight);
        _bodyBounds = new RectangleF(Bounds.X, Bounds.Y + TitleHeight, width, bodyHeight);
    }

    /// <inheritdoc/>
    /// <remarks>Opens the note for editing, the way double-clicking a Grasshopper panel does.</remarks>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        _notes.EditNotes();
        Selected = false;
        sender?.Refresh();
        return GH_ObjectResponse.Handled;
    }

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Objects)
        {
            base.Render(canvas, graphics, channel);
            return;
        }

        RectangleF rec = Bounds;
        if (!canvas.Viewport.IsVisible(ref rec, 10f))
        {
            Bounds = rec;
            return;
        }

        Bounds = rec;

        var capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Normal);
        try
        {
            capsule.SetJaggedEdges(false, false);

            graphics.SmoothingMode = SmoothingMode.HighQuality;
            canvas.SetSmartTextRenderingHint();

            capsule.Render(graphics, HarnessTheme.Style);
            HarnessTheme.DrawGlow(graphics, Bounds);

            DrawTitle(graphics);
            DrawBody(graphics);
        }
        finally
        {
            capsule.Dispose();
        }
    }

    // The title strip: the nickname in ink, with a hairline under it separating title from note. Not a
    // second capsule — one silhouette keeps the panel reading as a single object.
    private void DrawTitle(Graphics graphics)
    {
        using var ink = new SolidBrush(HarnessTheme.Ink);
        using var rule = new Pen(HarnessTheme.Ink, 0.6f);

        Font font = GH_FontServer.StandardAdjusted;
        SizeF size = graphics.MeasureString(_notes.NickName, font);
        graphics.DrawString(
            _notes.NickName,
            font,
            ink,
            _titleBounds.X + PaddingX,
            _titleBounds.Y + ((_titleBounds.Height - size.Height) / 2f));

        graphics.DrawLine(
            rule,
            _titleBounds.X + PaddingX,
            _titleBounds.Bottom,
            _titleBounds.Right - PaddingX,
            _titleBounds.Bottom);
    }

    // The note itself, wrapped into the body rectangle. Drawn dimmed when empty, so the placeholder
    // reads as a prompt rather than as content.
    private void DrawBody(Graphics graphics)
    {
        bool empty = string.IsNullOrWhiteSpace(_notes.Notes);
        string text = empty ? Placeholder : _notes.Notes;

        using var ink = new SolidBrush(empty
            ? Color.FromArgb(140, HarnessTheme.Ink)
            : HarnessTheme.Ink);

        using var format = BodyFormat();
        graphics.DrawString(text, GH_FontServer.StandardAdjusted, ink, TextRectangle(), format);
    }

    // Height the note needs at a given panel width: the wrapped text plus padding, never less than
    // MinBodyHeight. Measured against a throwaway 1x1 surface because Layout has no Graphics of its own
    // and MeasureString needs one.
    private float MeasureBody(float width)
    {
        string text = string.IsNullOrWhiteSpace(_notes.Notes) ? Placeholder : _notes.Notes;

        using var surface = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(surface);
        using StringFormat format = BodyFormat();

        SizeF measured = graphics.MeasureString(
            text,
            GH_FontServer.StandardAdjusted,
            new SizeF(width - (PaddingX * 2f), float.MaxValue),
            format);

        return System.Math.Max(MinBodyHeight, measured.Height + (PaddingY * 2f));
    }

    // The note's text area, inset from the body rectangle.
    private RectangleF TextRectangle() => RectangleF.FromLTRB(
        _bodyBounds.X + PaddingX,
        _bodyBounds.Y + PaddingY,
        _bodyBounds.Right - PaddingX,
        _bodyBounds.Bottom - PaddingY);

    // Wrapping, left-aligned, no trailing-space trimming — so measuring and drawing agree exactly.
    private static StringFormat BodyFormat() => new StringFormat(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        FormatFlags = StringFormatFlags.NoClip,
        Trimming = StringTrimming.Word,
    };
}
