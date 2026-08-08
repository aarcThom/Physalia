// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#if WINDOWS
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Widgets;
using Grasshopper.Kernel;
using Physalia.GH.Components;

namespace Physalia.GH.Widgets;

/// <summary>
/// Registers the Physalia canvas widget (<see cref="ChatWidget"/>) with every Grasshopper canvas.
/// Canvas widgets are not auto-discovered from the assembly the way components are — a plugin must
/// subscribe to <see cref="GH_Canvas.WidgetListCreated"/> at load time and add its widgets to each
/// canvas's freshly built list. The signal trace has no widget: it is opened from the chat window's
/// header, by wiring a Signal Trace human tool into the Conversation Log.
/// </summary>
public sealed class ChatWidgetPriority : GH_AssemblyPriority
{
    /// <summary>
    /// Hooks widget-list creation so the Physalia widgets are added to every canvas.
    /// </summary>
    /// <returns>An instruction telling Grasshopper to continue loading.</returns>
    public override GH_LoadingInstruction PriorityLoad()
    {
        GH_Canvas.WidgetListCreated += AddWidgets;

        // Runtime-message recording defaults ON so transient pipeline errors and warnings (a
        // truncated LLM response, a failed guardrail) land in the signal trace without the user
        // having opted in before the failure happened. The trace-window toggle still turns it
        // off. No canvas exists yet at priority load; the trace defers its event hookup.
        Diagnostics.RuntimeMessageTrace.Enabled = true;
        return GH_LoadingInstruction.Proceed;
    }

    private static void AddWidgets(object sender, GH_CanvasWidgetListEventArgs e)
    {
        e.AddWidget(new ChatWidget());

        // The harness back button. It draws only while the canvas is inside a harness document, so
        // it costs nothing on an ordinary canvas — but it must be registered up front like any
        // other widget, since the list is built once per canvas.
        e.AddWidget(new HarnessReturnWidget());
    }
}

/// <summary>
/// Canvas widget docked to the bottom-right of the Grasshopper canvas by default, above the
/// compass, and draggable like the built-in widgets (click-and-hold to move; the position
/// persists across restarts). Double-clicking it opens the Physalia chat window — even when no
/// document is open. If the document
/// has no Chat component to drive the pipeline (or there is no document at all), one is
/// created and the window places it onto the canvas (to its right) once a provider is
/// available, creating a new document first if needed — so it isn't dropped during first-run
/// setup. Grasshopper
/// auto-discovers the widget and lists it (with a visibility checkbox) in the canvas
/// Widgets right-click menu; the visibility choice persists in the GH settings.
///
/// The drawn graphic is the Physalia critter — the project's only logo mark — rasterized from
/// Images/phy_critter.svg into the embedded Resources/critter.png. The chat window draws the same
/// mark, inlined as SVG in Physalia.UI's HappyFace.svelte; keep the two in step.
/// </summary>
public sealed class ChatWidget : GH_Widget
{
    // Settings keys backing the Widgets-menu checkbox and drag position so they survive a restart.
    private const string VisibleKey = "Physalia.ChatWidget.Visible";
    private const string RightOffsetKey = "Physalia.ChatWidget.RightOffset";
    private const string BottomOffsetKey = "Physalia.ChatWidget.BottomOffset";

    // Embedded PNG rasterized from Images/phy_critter.svg (portrait critter, 365x512). The source
    // viewBox is tight to the artwork, so the bitmap needs no padding trim before FitCentred.
    private const string LogoResource = "Physalia.GH.Resources.critter.png";

    // Widget geometry, in device (screen) pixels. The default docks it bottom-right, above the
    // compass; the offsets are the gap from the canvas right/bottom edge to the widget's edge, and
    // become mutable once the user drags the widget.
    private const int BoxSize = 108;
    private const int DefaultRightOffset = 14;
    private const int DefaultBottomOffset = 84;

    // Pixels the cursor must travel with the button held before a press turns into a drag.
    private const int DragThreshold = 4;

    // Sentinel meaning "offsets not yet loaded from settings"; real offsets are always >= 0 after clamp.
    private const int Unloaded = int.MinValue;

    // Last-rendered frame in device pixels; reused for hit-testing in Contains/RespondToMouseDown.
    private Rectangle _frame;
    private Bitmap? _icon;
    private Bitmap? _logo;

    // Live drag position (gap from the canvas right/bottom edge to the widget's edge), loaded lazily
    // from settings and persisted on drag end.
    private int _rightOffset = Unloaded;
    private int _bottomOffset = Unloaded;

    // Drag state machine across Down/Move/Up.
    private bool _pressed;
    private bool _dragging;
    private Point _pressOrigin;   // control-space point where the press began (drag-threshold anchor)
    private Point _grabOffset;    // cursor position relative to the widget's top-left at press time

    /// <inheritdoc/>
    public override string Name => "Physalia Chat";

    /// <inheritdoc/>
    public override string Description => "Open the Physalia chat window.";

    /// <inheritdoc/>
    public override string TooltipText => "Open the Physalia chat window.";

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
    /// Draws the Physalia logo at its current position (docked bottom-right by default, or wherever
    /// the user dragged it), clamped inside the canvas window.
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

        Bitmap? logo = Logo;
        if (logo is null)
        {
            return;
        }

        Graphics g = canvas.Graphics;

        // Widget Render runs under the canvas pan/zoom transform — reset to device space so the
        // logo is pinned to the window corner regardless of pan/zoom (same as SerializeWidget).
        Matrix oldTransform = g.Transform;
        SmoothingMode oldMode = g.SmoothingMode;
        InterpolationMode oldInterp = g.InterpolationMode;
        g.ResetTransform();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Fit the portrait logo inside the square frame, preserving aspect ratio and centring.
        g.DrawImage(logo, FitCentred(logo.Size, _frame));

        g.InterpolationMode = oldInterp;
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

            // Become the canvas's active widget so GH routes every subsequent move/up straight to us
            // (bypassing the Contains() hit-test) — otherwise a fast drag that outruns the icon bounds
            // stops receiving move events and the widget is dropped mid-drag.
            if (sender is not null)
            {
                sender.ActiveWidget = this;
            }

            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }

    /// <summary>
    /// Drags the widget once the cursor moves past the threshold, repositioning it under the cursor
    /// and clamping it inside the canvas.
    /// </summary>
    /// <param name="sender">The canvas the mouse event originated from.</param>
    /// <param name="e">The mouse event.</param>
    /// <returns>Handled while a drag is in progress, otherwise Ignore.</returns>
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        // Gate on _pressed only (set on left-down, cleared on up). While pressed we are the canvas's
        // active widget, so every move routes here; on hover _pressed is false and we ignore, so the
        // icon never follows the cursor unless it was actually picked up.
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
    /// Opens the chat window on a left double-click inside the widget.
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
            OpenChat(sender);
            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }

    // Relinquishes the canvas's active-widget capture if we currently hold it.
    private void ReleaseCapture(GH_Canvas? canvas)
    {
        if (canvas is not null && ReferenceEquals(canvas.ActiveWidget, this))
        {
            canvas.ActiveWidget = null;
        }
    }

    // Opens (or focuses) the single shared chat window. Reuses a Chat already in the file (inside a
    // harness or, in an older file, loose on the canvas); otherwise creates one but does NOT place
    // it — the window wraps it in a Harness and drops that on the document, to its right, once a
    // provider is available (so first-run setup never litters the canvas). A first click therefore
    // yields a Harness on the canvas, not a bare Chat: the harness is the plug-in's unit of work.
    // Works even with no document open: the new Chat stays detached until the window places it, at
    // which point the window creates a document for it.
    private static void OpenChat(GH_Canvas canvas)
    {
        GH_Document? doc = canvas?.Document;
        Chat? chat = doc is null ? null : FindChat(doc);
        if (chat is null)
        {
            chat = new Chat();
            chat.CreateAttributes();
        }

        chat.OpenWindow();
    }

    // Finds a Chat anywhere in the file, harnesses included — once a pipeline has moved into one,
    // that is the only place a Chat exists, and the widget must still find it rather than dropping
    // a second, unwired Chat onto the canvas.
    private static Chat? FindChat(GH_Document doc)
    {
        GH_Document? host = Physalia.GH.Harness.PhyDocuments.Host(doc);
        foreach (IGH_DocumentObject obj in Physalia.GH.Harness.PhyDocuments.ObjectsIncludingHarnesses(host))
        {
            if (obj is Chat chat)
            {
                return chat;
            }
        }

        return null;
    }

    // Loads the persisted drag offsets on first use (defaulting to the docked bottom-right corner).
    private void EnsureOffsetsLoaded()
    {
        if (_rightOffset == Unloaded)
        {
            _rightOffset = Instances.Settings.GetValue(RightOffsetKey, DefaultRightOffset);
            _bottomOffset = Instances.Settings.GetValue(BottomOffsetKey, DefaultBottomOffset);
        }
    }

    // The widget rectangle for the given canvas size, positioned from the offsets and clamped so it
    // stays fully on-screen (a shrunk window can otherwise push a corner-anchored widget off-canvas).
    private Rectangle ComputeFrame(int canvasWidth, int canvasHeight)
    {
        int x = canvasWidth - BoxSize - _rightOffset;
        int y = canvasHeight - BoxSize - _bottomOffset;
        x = Math.Max(0, Math.Min(x, Math.Max(0, canvasWidth - BoxSize)));
        y = Math.Max(0, Math.Min(y, Math.Max(0, canvasHeight - BoxSize)));
        return new Rectangle(x, y, BoxSize, BoxSize);
    }

    // Repositions the widget so its top-left tracks the cursor (minus the grab offset), clamped to
    // the canvas, and recomputes the edge offsets from the new position.
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

    // The embedded logo bitmap, loaded once and cached. Null if the resource is missing.
    private Bitmap? Logo => _logo ??= LoadLogo();

    private static Bitmap? LoadLogo()
    {
        Assembly assembly = typeof(ChatWidget).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(LogoResource);
        return stream is null ? null : new Bitmap(stream);
    }

    // Menu/tooltip icon — the logo scaled into a 24x24 transparent bitmap, aspect preserved.
    private static Bitmap CreateIcon()
    {
        var bitmap = new Bitmap(24, 24);
        using Bitmap? logo = LoadLogo();
        if (logo is null)
        {
            return bitmap;
        }

        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(logo, FitCentred(logo.Size, new Rectangle(0, 0, 24, 24)));
        return bitmap;
    }

    // Scales source into bounds preserving aspect ratio and centres the result.
    private static Rectangle FitCentred(Size source, Rectangle bounds)
    {
        float scale = Math.Min((float)bounds.Width / source.Width, (float)bounds.Height / source.Height);
        int w = (int)Math.Round(source.Width * scale);
        int h = (int)Math.Round(source.Height * scale);
        int x = bounds.X + ((bounds.Width - w) / 2);
        int y = bounds.Y + ((bounds.Height - h) / 2);
        return new Rectangle(x, y, w, h);
    }
}
#endif
