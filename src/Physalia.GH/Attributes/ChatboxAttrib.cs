// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Attributes for the Chatbox component. The chat UI lives in a standalone window (opened on
/// double-click); on the canvas the Chatbox doubles as the proxy node for its collapsible
/// harness group. A small chevron toggles collapse, and while collapsed the node takes on a
/// distinct "stacked" look with a badge counting the hidden members.
/// </summary>
public class ChatboxAttrib : GH_ComponentAttributes
{
    private const float ChevronSize = 13f;
    private const float ChevronInset = 3f;

    private readonly Chatbox _chatbox;

    private RectangleF _chevronBounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatboxAttrib"/> class.
    /// </summary>
    /// <param name="chatbox">The Chatbox component that owns these attributes.</param>
    public ChatboxAttrib(Chatbox chatbox)
        : base(chatbox)
    {
        _chatbox = chatbox;
    }

    /// <inheritdoc/>
    protected override void Layout()
    {
        // This Chatbox may itself be a (plain) member of another harness — when that harness is
        // collapsed, hide it like any member: shrink to the collapse point and skip the proxy chrome.
        if (CollapseGuard.TryCollapseLayout(this))
        {
            _chevronBounds = RectangleF.Empty;
            return;
        }

        base.Layout();

        // The chevron only exists once this Chatbox is a harness (owns members); an empty Chatbox
        // is a plain node, so the toggle target collapses to nothing.
        _chevronBounds = _chatbox.Group.Count > 0
            ? new RectangleF(
                Bounds.Right - ChevronSize - ChevronInset,
                Bounds.Top + ChevronInset,
                ChevronSize,
                ChevronSize)
            : RectangleF.Empty;

        // While collapsed, keep the hidden members glued under this (possibly moved) proxy.
        _chatbox.Group.RefreshCollapsePoint();
    }

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // Hidden as a collapsed member of another harness — draw nothing at all.
        if (CollapseGuard.IsCollapsed(this))
        {
            return;
        }

        base.Render(canvas, graphics, channel);

        if (channel != GH_CanvasChannel.Objects)
        {
            return;
        }

        // Not a harness until it owns members — draw no chevron/decoration, so it reads as a plain node.
        if (_chatbox.Group.Count == 0)
        {
            return;
        }

        bool collapsed = _chatbox.Group.Collapsed;

        if (collapsed)
        {
            DrawCollapsedDecoration(graphics);
        }

        DrawChevron(graphics, collapsed);
    }

    /// <summary>
    /// Opens the chat window on double-click (outside the collapse chevron).
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled — the double-click is consumed to open the window.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_chevronBounds.Contains(e.CanvasLocation))
        {
            return GH_ObjectResponse.Handled;
        }

        _chatbox.OpenWindow();
        _chatbox.Attributes.Selected = false;
        sender.Refresh();
        return GH_ObjectResponse.Handled;
    }

    /// <summary>
    /// Toggles the harness collapse when the chevron is clicked.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if the chevron was clicked; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left && _chevronBounds.Contains(e.CanvasLocation))
        {
            _chatbox.ToggleCollapse();
            sender.Refresh();
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDown(sender, e);
    }

    // A double outline + accent badge so a collapsed Chatbox reads as "more than one node".
    private void DrawCollapsedDecoration(Graphics graphics)
    {
        var outer = RectangleF.Inflate(Bounds, 3f, 3f);
        using var accent = new Pen(Color.FromArgb(200, 119, 0, 255), 1.5f);
        graphics.DrawRectangle(accent, outer.X, outer.Y, outer.Width, outer.Height);

        // Member-count badge at the top-left corner.
        string count = _chatbox.Group.Count.ToString();
        var badge = new RectangleF(Bounds.Left - 7f, Bounds.Top - 7f, 16f, 16f);
        using var badgeFill = new SolidBrush(Color.FromArgb(255, 119, 0, 255));
        using var badgeText = new SolidBrush(Color.White);
        using var badgeFont = new Font(FontFamily.GenericSansSerif, 6f, FontStyle.Bold);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.FillEllipse(badgeFill, badge);
        graphics.DrawString(count, badgeFont, badgeText, badge, fmt);
    }

    // A small framed chevron: pointing right (▸) while collapsed, down (▾) while expanded.
    private void DrawChevron(Graphics graphics, bool collapsed)
    {
        using var bg = new SolidBrush(Color.FromArgb(180, 255, 255, 255));
        using var frame = new Pen(Color.FromArgb(255, 47, 8, 87), 1f);
        graphics.FillRectangle(bg, _chevronBounds);
        graphics.DrawRectangle(frame, _chevronBounds.X, _chevronBounds.Y, _chevronBounds.Width, _chevronBounds.Height);

        float cx = _chevronBounds.X + (_chevronBounds.Width / 2f);
        float cy = _chevronBounds.Y + (_chevronBounds.Height / 2f);
        using var arrow = new Pen(Color.FromArgb(255, 47, 8, 87), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        if (collapsed)
        {
            // ▸
            graphics.DrawLines(arrow, new[]
            {
                new PointF(cx - 2f, cy - 3.5f),
                new PointF(cx + 2.5f, cy),
                new PointF(cx - 2f, cy + 3.5f),
            });
        }
        else
        {
            // ▾
            graphics.DrawLines(arrow, new[]
            {
                new PointF(cx - 3.5f, cy - 2f),
                new PointF(cx, cy + 2.5f),
                new PointF(cx + 3.5f, cy - 2f),
            });
        }
    }
}
