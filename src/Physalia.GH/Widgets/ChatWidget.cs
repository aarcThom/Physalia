// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#if WINDOWS
#nullable enable

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Widgets;
using Grasshopper.Kernel;
using Physalia.GH.Components;

namespace Physalia.GH.Widgets;

/// <summary>
/// Registers <see cref="ChatWidget"/> with every Grasshopper canvas. Canvas widgets are not
/// auto-discovered from the assembly the way components are — a plugin must subscribe to
/// <see cref="GH_Canvas.WidgetListCreated"/> at load time and add its widget to each canvas's
/// freshly built list.
/// </summary>
public sealed class ChatWidgetPriority : GH_AssemblyPriority
{
    /// <summary>
    /// Hooks widget-list creation so a <see cref="ChatWidget"/> is added to every canvas.
    /// </summary>
    /// <returns>An instruction telling Grasshopper to continue loading.</returns>
    public override GH_LoadingInstruction PriorityLoad()
    {
        GH_Canvas.WidgetListCreated += AddChatWidget;
        return GH_LoadingInstruction.Proceed;
    }

    private static void AddChatWidget(object sender, GH_CanvasWidgetListEventArgs e)
        => e.AddWidget(new ChatWidget());
}

/// <summary>
/// Canvas widget pinned to the bottom-right of the Grasshopper canvas, above the compass.
/// Clicking it opens the Physalia chat window; if the document has no Chatbox component to
/// drive the pipeline, one is created and the window places it onto the canvas (to its right)
/// once a provider is available — so it isn't dropped during first-run setup. Grasshopper
/// auto-discovers the widget and lists it (with a visibility checkbox) in the canvas
/// Widgets right-click menu; the visibility choice persists in the GH settings.
///
/// The drawn graphic is a placeholder square — real artwork is swapped in later.
/// </summary>
public sealed class ChatWidget : GH_Widget
{
    // Settings key backing the Widgets-menu checkbox so the choice survives a restart.
    private const string VisibleKey = "Physalia.ChatWidget.Visible";

    // Placeholder geometry, in device (screen) pixels. Tunable; BottomOffset clears the compass.
    private const int BoxSize = 32;
    private const int RightMargin = 14;
    private const int BottomOffset = 84;
    private const int CornerRadius = 6;

    // Last-rendered frame in device pixels; reused for hit-testing in Contains/RespondToMouseDown.
    private Rectangle _frame;
    private Bitmap? _icon;

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
    public override Bitmap Icon_24x24 => _icon ??= CreatePlaceholderIcon();

    /// <summary>
    /// Draws the placeholder square pinned to the bottom-right corner of the canvas window.
    /// </summary>
    /// <param name="canvas">The canvas being painted.</param>
    public override void Render(GH_Canvas canvas)
    {
        if (!Visible || canvas?.Graphics is null)
        {
            return;
        }

        int x = canvas.Width - BoxSize - RightMargin;
        int y = canvas.Height - BoxSize - BottomOffset;
        _frame = new Rectangle(x, y, BoxSize, BoxSize);

        Graphics g = canvas.Graphics;

        // Widget Render runs under the canvas pan/zoom transform — reset to device space so the
        // square is pinned to the window corner regardless of pan/zoom (same as SerializeWidget).
        Matrix oldTransform = g.Transform;
        SmoothingMode oldMode = g.SmoothingMode;
        g.ResetTransform();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (GraphicsPath path = RoundedRect(_frame, CornerRadius))
        using (var fill = new SolidBrush(Color.FromArgb(235, 45, 45, 45)))
        using (var border = new Pen(Color.FromArgb(255, 255, 191, 0), 1.5f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

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
    /// Opens the chat window on a left-click inside the widget.
    /// </summary>
    /// <param name="sender">The canvas the mouse event originated from.</param>
    /// <param name="e">The mouse event.</param>
    /// <returns>Handled when the click opened the window, otherwise Ignore.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left && _frame.Contains(e.ControlLocation))
        {
            OpenChat(sender);
            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }

    // Opens (or focuses) the single shared chat window. Reuses a Chatbox already on the canvas;
    // otherwise creates one but does NOT place it — the window drops it onto the document itself,
    // to its right, once a provider is available (so first-run setup never litters the canvas).
    private static void OpenChat(GH_Canvas canvas)
    {
        GH_Document? doc = canvas?.Document;
        if (doc is null)
        {
            return;
        }

        Chatbox? chatbox = FindChatbox(doc);
        if (chatbox is null)
        {
            chatbox = new Chatbox();
            chatbox.CreateAttributes();
        }

        chatbox.OpenWindow();
    }

    private static Chatbox? FindChatbox(GH_Document doc)
    {
        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (obj is Chatbox chatbox)
            {
                return chatbox;
            }
        }

        return null;
    }

    // Placeholder menu/tooltip icon — a filled rounded square. Replaced by real artwork later.
    private static Bitmap CreatePlaceholderIcon()
    {
        var bitmap = new Bitmap(24, 24);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = RoundedRect(new Rectangle(2, 2, 20, 20), 4);
        using var fill = new SolidBrush(Color.FromArgb(45, 45, 45));
        using var border = new Pen(Color.FromArgb(255, 191, 0), 1.5f);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        return bitmap;
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
#endif
