// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#if WINDOWS
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Widgets;

namespace Physalia.GH.Widgets;

/// <summary>
/// Canvas widget that opens the Physalia signal-trace window (double-click). Docked to the
/// bottom-right of the canvas just above the chat widget by default, and draggable like the
/// built-in widgets (click-and-hold to move; position persists across restarts). Grasshopper
/// lists it (with a visibility checkbox) in the canvas Widgets right-click menu; the choice
/// persists in the GH settings. The glyph is a GDI-drawn pulse waveform — no embedded resource.
/// </summary>
public sealed class SignalTraceWidget : GH_Widget
{
    // Settings keys backing the Widgets-menu checkbox and drag position so they survive a restart.
    private const string VisibleKey = "Physalia.TraceWidget.Visible";
    private const string RightOffsetKey = "Physalia.TraceWidget.RightOffset";
    private const string BottomOffsetKey = "Physalia.TraceWidget.BottomOffset";

    // Widget geometry, in device (screen) pixels. Defaults dock it directly above the chat
    // widget (right offset matches; bottom offset clears the chat widget's 108px box + gap).
    private const int BoxSize = 48;
    private const int DefaultRightOffset = 14;
    private const int DefaultBottomOffset = 200;

    // Pixels the cursor must travel with the button held before a press turns into a drag.
    private const int DragThreshold = 4;

    // Sentinel meaning "offsets not yet loaded from settings"; real offsets are always >= 0 after clamp.
    private const int Unloaded = int.MinValue;

    // Last-rendered frame in device pixels; reused for hit-testing in Contains/RespondToMouseDown.
    private Rectangle _frame;
    private Bitmap? _icon;

    // Live drag position (gap from the canvas right/bottom edge to the widget's edge), loaded lazily
    // from settings and persisted on drag end.
    private int _rightOffset = Unloaded;
    private int _bottomOffset = Unloaded;

    // Drag state machine across Down/Move/Up.
    private bool _pressed;
    private bool _dragging;
    private Point _pressOrigin;
    private Point _grabOffset;

    /// <inheritdoc/>
    public override string Name => "Physalia Signal Trace";

    /// <inheritdoc/>
    public override string Description => "Open the Physalia signal trace window.";

    /// <inheritdoc/>
    public override string TooltipText => "Open the Physalia signal trace window.";

    /// <inheritdoc/>
    public override bool TooltipEnabled => true;

    /// <inheritdoc/>
    public override bool Visible
    {
        get => Instances.Settings.GetValue(VisibleKey, true);
        set => Instances.Settings.SetValue(VisibleKey, value);
    }

    /// <inheritdoc/>
    public override Bitmap Icon_24x24 => _icon ??= CreateIcon();

    /// <summary>
    /// Draws the pulse glyph at its current position (docked above the chat widget by default,
    /// or wherever the user dragged it), clamped inside the canvas window.
    /// </summary>
    /// <param name="canvas">The canvas being painted.</param>
    public override void Render(GH_Canvas canvas)
    {
        if (!Visible || canvas?.Graphics is null)
        {
            return;
        }

        EnsureOffsetsLoaded();
        _frame = ComputeFrame(canvas.Width, canvas.Height);

        Graphics g = canvas.Graphics;

        // Widget Render runs under the canvas pan/zoom transform — reset to device space so the
        // glyph is pinned to the window corner regardless of pan/zoom (same as ChatWidget).
        Matrix oldTransform = g.Transform;
        SmoothingMode oldMode = g.SmoothingMode;
        g.ResetTransform();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawGlyph(g, _frame);

        g.SmoothingMode = oldMode;
        g.Transform = oldTransform;
        oldTransform.Dispose();
    }

    /// <summary>
    /// Hit-tests a canvas point against the rendered square.
    /// </summary>
    /// <param name="pt_control">The point in control (device) coordinates.</param>
    /// <param name="pt_canvas">The point in canvas (world) coordinates.</param>
    /// <returns>true when the point is inside the widget.</returns>
    public override bool Contains(Point pt_control, PointF pt_canvas)
        => Visible && _frame.Contains(pt_control);

    /// <summary>
    /// Begins a potential drag on a left-press inside the widget. The press only becomes a drag
    /// once the cursor moves past <see cref="DragThreshold"/>; opening is handled by double-click.
    /// </summary>
    /// <param name="sender">The canvas the mouse event originated from.</param>
    /// <param name="e">The mouse event.</param>
    /// <returns>Handled when the press landed on the widget, otherwise Ignore.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (Visible && e.Button == MouseButtons.Left && _frame.Contains(e.ControlLocation))
        {
            _pressed = true;
            _dragging = false;
            _pressOrigin = e.ControlLocation;
            _grabOffset = new Point(e.ControlLocation.X - _frame.X, e.ControlLocation.Y - _frame.Y);

            // Become the canvas's active widget so GH routes every subsequent move/up straight to
            // us — otherwise a fast drag that outruns the icon bounds drops the widget mid-drag.
            if (sender is not null)
            {
                sender.ActiveWidget = this;
            }

            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }

    /// <summary>
    /// Drags the widget once the cursor moves past the threshold, repositioning it under the
    /// cursor and clamping it inside the canvas.
    /// </summary>
    /// <param name="sender">The canvas the mouse event originated from.</param>
    /// <param name="e">The mouse event.</param>
    /// <returns>Handled while a drag is in progress, otherwise Ignore.</returns>
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (!_pressed || sender is null)
        {
            return GH_ObjectResponse.Ignore;
        }

        if (!_dragging &&
            (Math.Abs(e.ControlLocation.X - _pressOrigin.X) > DragThreshold ||
             Math.Abs(e.ControlLocation.Y - _pressOrigin.Y) > DragThreshold))
        {
            _dragging = true;
        }

        if (_dragging)
        {
            MoveTo(e.ControlLocation, sender.Width, sender.Height);
            sender.Invalidate();
            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }

    /// <summary>
    /// Ends a drag (persisting the new position) or completes a plain press.
    /// </summary>
    /// <param name="sender">The canvas the mouse event originated from.</param>
    /// <param name="e">The mouse event.</param>
    /// <returns>Handled when the press/drag was ours, otherwise Ignore.</returns>
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (!_pressed)
        {
            return GH_ObjectResponse.Ignore;
        }

        bool wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        ReleaseCapture(sender);

        if (wasDragging)
        {
            PersistOffsets();
        }

        return GH_ObjectResponse.Handled;
    }

    /// <summary>
    /// Opens the signal trace window on a left double-click inside the widget.
    /// </summary>
    /// <param name="sender">The canvas the mouse event originated from.</param>
    /// <param name="e">The mouse event.</param>
    /// <returns>Handled when the double-click opened the window, otherwise Ignore.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (Visible && e.Button == MouseButtons.Left && _frame.Contains(e.ControlLocation))
        {
            _pressed = false;
            _dragging = false;
            ReleaseCapture(sender);
            Panels.SignalTraceWindow.ShowOrFocus();
            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }

    // Draws the widget glyph: a soft rounded square with an ECG-style pulse line.
    private static void DrawGlyph(Graphics g, Rectangle frame)
    {
        using GraphicsPath path = RoundedRect(frame, frame.Width / 6);
        using var fill = new SolidBrush(Color.FromArgb(235, 250, 250, 250));
        using var edge = new Pen(Color.FromArgb(255, 120, 120, 120), 1f);
        g.FillPath(fill, path);
        g.DrawPath(edge, path);

        using var pulse = new Pen(Color.FromArgb(255, 0, 140, 160), Math.Max(1.6f, frame.Width / 20f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pulse, PulsePoints(frame));
    }

    // The ECG polyline, proportional to the frame: flat — spike up — dip down — flat.
    private static PointF[] PulsePoints(Rectangle frame)
    {
        float x = frame.X;
        float y = frame.Y;
        float w = frame.Width;
        float h = frame.Height;
        float mid = y + (h * 0.52f);

        return new[]
        {
            new PointF(x + (w * 0.14f), mid),
            new PointF(x + (w * 0.34f), mid),
            new PointF(x + (w * 0.44f), y + (h * 0.22f)),
            new PointF(x + (w * 0.58f), y + (h * 0.76f)),
            new PointF(x + (w * 0.66f), mid),
            new PointF(x + (w * 0.86f), mid),
        };
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Relinquishes the canvas's active-widget capture if we currently hold it.
    private void ReleaseCapture(GH_Canvas? canvas)
    {
        if (canvas is not null && ReferenceEquals(canvas.ActiveWidget, this))
        {
            canvas.ActiveWidget = null;
        }
    }

    // Loads the persisted drag offsets on first use (defaulting to just above the chat widget).
    private void EnsureOffsetsLoaded()
    {
        if (_rightOffset == Unloaded)
        {
            _rightOffset = Instances.Settings.GetValue(RightOffsetKey, DefaultRightOffset);
            _bottomOffset = Instances.Settings.GetValue(BottomOffsetKey, DefaultBottomOffset);
        }
    }

    // The widget rectangle for the given canvas size, positioned from the offsets and clamped so
    // it stays fully on-screen.
    private Rectangle ComputeFrame(int canvasWidth, int canvasHeight)
    {
        int x = canvasWidth - BoxSize - _rightOffset;
        int y = canvasHeight - BoxSize - _bottomOffset;
        x = Math.Max(0, Math.Min(x, Math.Max(0, canvasWidth - BoxSize)));
        y = Math.Max(0, Math.Min(y, Math.Max(0, canvasHeight - BoxSize)));
        return new Rectangle(x, y, BoxSize, BoxSize);
    }

    // Repositions the widget so its top-left tracks the cursor (minus the grab offset), clamped
    // to the canvas, and recomputes the edge offsets from the new position.
    private void MoveTo(Point cursor, int canvasWidth, int canvasHeight)
    {
        int x = cursor.X - _grabOffset.X;
        int y = cursor.Y - _grabOffset.Y;
        x = Math.Max(0, Math.Min(x, Math.Max(0, canvasWidth - BoxSize)));
        y = Math.Max(0, Math.Min(y, Math.Max(0, canvasHeight - BoxSize)));
        _rightOffset = canvasWidth - BoxSize - x;
        _bottomOffset = canvasHeight - BoxSize - y;
        _frame = new Rectangle(x, y, BoxSize, BoxSize);
    }

    // Writes the current offsets to settings so the position survives a restart.
    private void PersistOffsets()
    {
        Instances.Settings.SetValue(RightOffsetKey, _rightOffset);
        Instances.Settings.SetValue(BottomOffsetKey, _bottomOffset);
    }

    // Menu/tooltip icon — the pulse glyph drawn into a 24x24 transparent bitmap.
    private static Bitmap CreateIcon()
    {
        var bitmap = new Bitmap(24, 24);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawGlyph(g, new Rectangle(1, 1, 22, 22));
        return bitmap;
    }
}
#endif
