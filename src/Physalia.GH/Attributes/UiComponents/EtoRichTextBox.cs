// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;

namespace Physalia.GH.Attributes.UiComponents;

/// <summary>
/// A borderless WinForms popup form containing a <see cref="System.Windows.Forms.RichTextBox"/>,
/// positioned over the GH canvas at a specified canvas-space rectangle.
/// </summary>
/// <remarks>
/// Use <see cref="RichTextBox"/> to attach <c>KeyDown</c> and other text-level events.
/// Use <see cref="Popup"/> to attach <c>Deactivate</c> (user clicked away) and
/// <c>FormClosed</c> (cleanup after programmatic close) events.
/// Call <see cref="Show"/> to display and <see cref="Close"/> to dismiss.
/// </remarks>
public class EtoRichTextBox
{
    // PROPERTIES ==================================================================

    /// <summary>
    /// Gets the WinForms RichTextBox. Attach <c>KeyDown</c> and similar text events here.
    /// </summary>
    public RichTextBox RichTextBox { get; }

    /// <summary>
    /// Gets the popup Form. Attach <c>Deactivate</c> and <c>FormClosed</c> events here.
    /// </summary>
    public Form Popup { get; }

    /// <summary>
    /// Gets or sets the plain-text content.
    /// </summary>
    public string Text
    {
        get => RichTextBox.Text;
        set => RichTextBox.Text = value;
    }

    // CONSTRUCTOR ==================================================================

    /// <summary>
    /// Initializes a new <see cref="EtoRichTextBox"/>, projecting <paramref name="canvasBounds"/>
    /// through the canvas viewport to screen coordinates. Both the popup size and
    /// <paramref name="font"/> are expected to be zoom-aware so the popup scales with the canvas.
    /// Pass a <c>GH_FontServer.*Adjusted</c> font so text matches the apparent size of
    /// canvas-drawn text at any zoom level.
    /// </summary>
    /// <param name="canvas">The GH canvas the popup will float over.</param>
    /// <param name="canvasBounds">The rectangle in canvas (world) coordinates to cover.</param>
    /// <param name="backColor">Background colour for the text area.</param>
    /// <param name="font">The font to display text in.</param>
    /// <param name="initialText">The initial text content.</param>
    public EtoRichTextBox(GH_Canvas canvas, RectangleF canvasBounds, Color backColor, Font font, string initialText = "")
    {
        float zoom = canvas.Viewport.Zoom;
        PointF projected = canvas.Viewport.ProjectPoint(canvasBounds.Location);
        Point screenPt = canvas.PointToScreen(new Point((int)projected.X, (int)projected.Y));

        // font is expected to be a zoom-adjusted GH font (e.g. GH_FontServer.ConsoleSmallAdjusted)
        // so its size already reflects the current viewport zoom — no further scaling needed here.
        RichTextBox = new RichTextBox
        {
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.None,
            BorderStyle = BorderStyle.None,
            BackColor = backColor,
            Font = font,
            Text = initialText,
            Dock = DockStyle.Fill,
        };

        // Scale popup size by zoom so it covers the canvas component at any zoom level.
        Popup = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = screenPt,
            Size = new Size(
                (int)(canvasBounds.Width * zoom),
                (int)(canvasBounds.Height * zoom)),
            ShowInTaskbar = false,
        };

        Popup.Controls.Add(RichTextBox);
    }

    // PUBLIC METHODS ==================================================================

    /// <summary>
    /// Shows the popup as a non-modal owned window of <paramref name="owner"/>
    /// and focuses the text area.
    /// </summary>
    /// <param name="owner">The parent window (typically the GH editor form).</param>
    public void Show(IWin32Window owner)
    {
        Popup.Show(owner);
        RichTextBox.Focus();
    }

    /// <summary>
    /// Selects all text in the control.
    /// </summary>
    public void SelectAll() => RichTextBox.SelectAll();

    /// <summary>
    /// Closes and disposes the popup.
    /// </summary>
    public void Close() => Popup.Close();
}
