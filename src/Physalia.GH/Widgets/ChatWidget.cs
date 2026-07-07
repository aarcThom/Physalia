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
/// Clicking it opens the Physalia chat window — even when no document is open. If the document
/// has no Chat component to drive the pipeline (or there is no document at all), one is
/// created and the window places it onto the canvas (to its right) once a provider is
/// available, creating a new document first if needed — so it isn't dropped during first-run
/// setup. Grasshopper
/// auto-discovers the widget and lists it (with a visibility checkbox) in the canvas
/// Widgets right-click menu; the visibility choice persists in the GH settings.
///
/// The drawn graphic is the Physalia logo (the same jellyfish shown in the chat window),
/// rasterized from Images/logo.svg into the embedded Resources/logo.png.
/// </summary>
public sealed class ChatWidget : GH_Widget
{
    // Settings key backing the Widgets-menu checkbox so the choice survives a restart.
    private const string VisibleKey = "Physalia.ChatWidget.Visible";

    // Embedded PNG rasterized from Images/logo.svg (portrait jellyfish, 215x256).
    private const string LogoResource = "Physalia.GH.Resources.logo.png";

    // Widget geometry, in device (screen) pixels. Tunable; BottomOffset clears the compass.
    private const int BoxSize = 108;
    private const int RightMargin = 14;
    private const int BottomOffset = 84;

    // Last-rendered frame in device pixels; reused for hit-testing in Contains/RespondToMouseDown.
    private Rectangle _frame;
    private Bitmap? _icon;
    private Bitmap? _logo;

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
    /// Draws the Physalia logo pinned to the bottom-right corner of the canvas window.
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

    // Opens (or focuses) the single shared chat window. Reuses a Chat already on the canvas;
    // otherwise creates one but does NOT place it — the window drops it onto the document itself,
    // to its right, once a provider is available (so first-run setup never litters the canvas).
    // Works even with no document open: the new Chat stays detached until the window places it,
    // at which point the window creates a document for it.
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

    private static Chat? FindChat(GH_Document doc)
    {
        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (obj is Chat chat)
            {
                return chat;
            }
        }

        return null;
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
