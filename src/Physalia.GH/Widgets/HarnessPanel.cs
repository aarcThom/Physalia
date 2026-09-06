// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Physalia.GH.Attributes;
using Physalia.GH.Harness;

namespace Physalia.GH.Widgets;

/// <summary>
/// The harness's own panel, floating at the top-left of the canvas while you are standing inside a
/// harness: what it is called, what it is for, what its chat window opens with, and the way back out.
///
/// <para><b>It is a real control, not a widget.</b> A <c>GH_Widget</c> is painted into the canvas in
/// device pixels and has no input controls of any kind — fine for the two pills this replaces,
/// impossible for three text fields.</para>
///
/// <para><b>And it is an owned top-level WINDOW, not a child of the canvas, because a child of the
/// canvas cannot reliably hold keyboard focus.</b> Parented to <c>GH_Canvas</c> it looked right and
/// typed into the RHINO COMMAND LINE. <c>GH_Canvas</c> derives from <c>Control</c>, not
/// <c>ContainerControl</c> (verified against the shipped assembly), so it breaks the chain WinForms
/// uses to restore focus into a child: the containing <c>Form</c> walks <c>ContainerControl</c>s, finds
/// a plain <c>Control</c>, and puts focus back on the canvas on every re-activation. Focus lands on
/// the field on click and does not survive. Calling <c>Focus()</c> explicitly does not help, because
/// the problem is not getting focus but keeping it.</para>
///
/// <para><b>Grasshopper's own in-canvas editor does not contradict this — it concedes the same
/// point.</b> <c>GH_TextBoxInputBase</c> hosts a <c>TextBox</c> on the canvas, focuses it outright,
/// and then <b>hides itself on LostFocus</b>: it is transient by design and never has to hold focus
/// through anything. A panel that stays on screen does.</para>
///
/// <para>So: a borderless <c>Form</c> owned by the Grasshopper editor, positioned over the canvas's
/// top-left corner and following it. It shows without activating, it follows the owner's z-order and
/// hides with it when minimised, and — the point of the exercise — it has its own focus chain, which
/// is what the chat window has always had and why typing has always worked there.</para>
///
/// <para><b>Every size here is MEASURED, never a pixel constant.</b> The first cut hard-coded row
/// heights and a panel width, which is only ever right at 100% scaling: at any other DPI the font
/// grows and the boxes do not, so labels lose their descenders, buttons read "Save as .p", and the
/// title runs into the button below it. Worse, a single-line <c>TextBox</c> IGNORES an assigned
/// Height — WinForms derives it from the font — so the row advance was wrong by however much the real
/// height exceeded the number it had been told, and the error accumulated down the panel. So rows are
/// laid out from each control's own measured height, text is measured with <c>TextRenderer</c> before
/// anything is sized around it, and the fixed insets are scaled from <see cref="Control.DeviceDpi"/>.
/// Same lesson the harness capsule learned when its outlet labels stopped being three fixed letters.</para>
///
/// <para><b>It opens collapsed.</b> Expanded it is a few hundred pixels square, permanently, over the
/// corner of a canvas somebody is working on — and its three fields are edited about twice in a
/// harness's life, while the way back out is wanted constantly. So the rolled-up state is the default
/// and the choice is remembered across sessions, the way the chat widget remembers its offsets.</para>
///
/// <para><b>Back to document is the last row, and is visible in both states.</b> It is the only
/// non-destructive way out of a harness, so it can never be behind the collapse toggle. Putting it at
/// the BOTTOM means it sits directly under the title strip when rolled up, which is where a
/// permanently-visible control belongs, rather than pushing the fields down when open.</para>
///
/// <para>The three fields live on the <see cref="HarnessComponent"/>, not here — a setting is only
/// worth anything if it travels, and these are exactly the fields a <c>.phy</c> carries to whoever
/// the workflow is shared with. This panel is a view onto them.</para>
/// </summary>
internal sealed class HarnessPanel : Form
{
    private const string CollapsedKey = "Physalia.HarnessPanel.Collapsed";

    // Unscaled design sizes, in pixels at 100%. Everything that reaches a Bounds goes through S().
    private const int MarginPx = 14;
    private const int MinWidthPx = 260;
    private const int MaxWidthPx = 420;
    private const int PadPx = 10;
    private const int RowGapPx = 6;
    private const int ButtonPadXPx = 12;
    private const int ButtonPadYPx = 6;
    private const int CornerPx = 8;

    // How far a text box sits inside the soft border drawn around it. The border is ours, not
    // WinForms' — see AddBox — so the box has to leave room for it and for a little breathing space,
    // or the text renders flush against the line.
    private const int WellInsetPx = 4;

    // How many lines of text the two multi-line boxes show. A description and an opening message are
    // both a sentence or three; more than this is a lot of canvas to occupy, and both boxes scroll.
    private const int MultilineRows = 3;

    private readonly Button _collapse = new();
    private readonly Button _back = new();
    private readonly Label _nameLabel = new();
    private readonly TextBox _name = new();
    private readonly Label _descriptionLabel = new();
    private readonly TextBox _description = new();
    private readonly Label _chatLabel = new();
    private readonly TextBox _chat = new();
    private readonly Button _save = new();
    private readonly Button _load = new();

    private HarnessComponent? _harness;

    // True while the panel is writing its own fields from the harness, so the TextChanged handlers
    // do not write straight back and re-enter.
    private bool _loading;

    // Kept in a field rather than read back off a child's Visible. Control.Visible returns EFFECTIVE
    // visibility — false whenever an ancestor is hidden — and this panel is hidden for most of its
    // life, whenever the canvas is not inside a harness. Deriving the state from it would report
    // "collapsed" every time the panel was merely off screen, and lay itself out that way on return.
    private bool _collapsed = true;

    // The canvas this window is pinned over. Not a parent any more — just the thing whose corner it
    // follows.
    private GH_Canvas? _owner;

    // The top-level window the canvas lives in — the Grasshopper editor while it floats, Rhino's own
    // window while it is docked. What the panel is owned by, and what it follows.
    private Form? _host;

    internal HarnessPanel()
    {
        this.DoubleBuffered = true;
        this.BackColor = HarnessTheme.Panel.Surface;
        this.ForeColor = HarnessTheme.Panel.Text;

        // A tool window: no chrome, no taskbar entry, placed by us. Not TopMost — the OWNER is what
        // keeps it above Grasshopper, so it drops behind whatever application the user switches to
        // and disappears when the editor is minimised, both of which TopMost would break.
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.ControlBox = false;
        this.Text = "Harness";
        this.AutoScaleMode = AutoScaleMode.None;
        this.KeyPreview = true;

        // Explicit rather than inherited from the canvas: GH sets its own font on the canvas, and a
        // panel of form controls should read as form controls. MessageBoxFont is the system UI font
        // and is already correct for the current DPI.
        this.Font = SystemFonts.MessageBoxFont ?? this.Font;

        this.BuildTitleRow();
        this.BuildFields();
        this.BuildActions();

        this._collapsed = Instances.Settings.GetValue(CollapsedKey, true);
        this.ApplyCollapsed();
    }

    /// <summary>
    /// Gets or sets a value indicating whether the panel is rolled up to its title bar.
    /// </summary>
    internal bool Collapsed
    {
        get => this._collapsed;

        set
        {
            this._collapsed = value;
            Instances.Settings.SetValue(CollapsedKey, value);
            this.ApplyCollapsed();
        }
    }

    /// <summary>
    /// Gets a value indicating whether showing this window should leave the focus where it is.
    ///
    /// <para>It must. The panel appears whenever the canvas enters a harness, which is not a moment
    /// the user asked to type — stealing activation there would take focus off whatever they were
    /// doing and, on entry from the proxy's menu, off Grasshopper itself.</para>
    /// </summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>
    /// Points the panel at a harness, or at nothing, and shows or hides it accordingly.
    ///
    /// <para>Deliberately not called Show: <c>Form.Show()</c> already exists, and an overload of it
    /// that also took a harness would be a trap for anyone calling the base method by habit.</para>
    /// </summary>
    /// <param name="harness">The harness the canvas is inside, or null when it is not inside one.</param>
    internal void Bind(HarnessComponent? harness)
    {
        this._harness = harness;

        if (harness is null)
        {
            this.Hide();
            return;
        }

        this._loading = true;
        try
        {
            this._name.Text = harness.NickName ?? string.Empty;
            this._description.Text = harness.ImportDescription ?? string.Empty;
            this._chat.Text = harness.ChatText ?? string.Empty;
        }
        finally
        {
            this._loading = false;
        }

        this.EnsureHostWindow();

        this.LayoutPanel();
        this.Reposition();

        if (!this.Visible)
        {
            this.Show();
        }

        this.Invalidate();
    }

    /// <summary>
    /// Finds the window the canvas actually lives in, owns this panel to it, and subscribes to its
    /// movement.
    ///
    /// <para><b>Resolved from the CANVAS, not from <c>Instances.DocumentEditor</c>.</b> The editor is
    /// the right window only while Grasshopper floats; docked, the canvas is hosted in a Rhino panel
    /// and it is RHINO's window that moves it. Asking the canvas which form contains it is right in
    /// both cases, and re-checking means docking or undocking mid-session re-points the panel instead
    /// of leaving it tracking a window the canvas has left.</para>
    ///
    /// <para><b>Lazy, and this is what the first cut got wrong.</b> The owner and these handlers were
    /// set up at attach time, but <c>WidgetListCreated</c> fires while the editor is still being
    /// built — so there was no window yet, nothing was ever subscribed, and moving Grasshopper left
    /// the panel behind on screen. Doing it here means it happens the first time the panel is
    /// actually shown, by which point the window certainly exists.</para>
    /// </summary>
    private void EnsureHostWindow()
    {
        Form? host = this._owner?.FindForm();
        if (host is null || host.IsDisposed || ReferenceEquals(host, this._host))
        {
            return;
        }

        if (this._host is { IsDisposed: false } previous)
        {
            previous.Move -= this.OnHostMoved;
            previous.Resize -= this.OnHostMoved;
        }

        this._host = host;
        host.Move += this.OnHostMoved;
        host.Resize += this.OnHostMoved;

        // Owning the panel to that window is what keeps it above Grasshopper, drops it behind
        // whatever application the user switches to, and hides it when the window is minimised.
        this.Owner = host;
    }

    private void OnHostMoved(object? sender, EventArgs e)
    {
        if (!this.IsDisposed && this.Visible)
        {
            this.Reposition();
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && this._host is { IsDisposed: false } host)
        {
            host.Move -= this.OnHostMoved;
            host.Resize -= this.OnHostMoved;
            this._host = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Puts the window over the canvas's top-left corner.
    ///
    /// <para>Screen coordinates now, rather than the client coordinates a child control had for
    /// free — which is the one thing hosting it as a window costs, and why the host tracks the
    /// canvas and the editor for anything that moves either.</para>
    /// </summary>
    internal void Reposition()
    {
        if (this._owner is not { IsDisposed: false, Visible: true } canvas)
        {
            return;
        }

        try
        {
            int margin = this.S(MarginPx);
            this.Location = canvas.PointToScreen(new Point(margin, margin));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // A canvas whose handle has gone during a document switch; the next Bind repositions.
        }
    }

    /// <summary>
    /// The canvas this panel floats over. Named to stay clear of <c>Control.Anchor</c>, which means
    /// something entirely different and is inherited.
    /// </summary>
    internal GH_Canvas? AnchorCanvas
    {
        get => this._owner;
        set => this._owner = value;
    }

    /// <summary>
    /// Re-reads the harness's name, for when something else changed it.
    /// </summary>
    internal void RefreshName()
    {
        if (this._harness is null || this._name.Focused)
        {
            return;
        }

        this._loading = true;
        try
        {
            this._name.Text = this._harness.NickName ?? string.Empty;
        }
        finally
        {
            this._loading = false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>A different font is a different layout — every row is measured from it.</remarks>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        this.LayoutPanel();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Dragging Rhino onto a monitor at another scaling re-measures everything. Without this the
    /// panel keeps the sizes it worked out for the monitor it was born on, which is the same clipping
    /// as hard-coded constants, just delayed.
    /// </remarks>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        this.LayoutPanel();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The first layout that can be trusted. Before the handle exists <c>DeviceDpi</c> is the default
    /// 96 whatever the monitor is doing, so the constructor's measurements are provisional.
    /// </remarks>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        this.LayoutPanel();
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int radius = this.S(CornerPx);
        int titleHeight = this.TitleHeight;
        var body = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

        // The title strip is clipped to the rounded body so its top corners follow the panel's,
        // instead of squaring off inside them.
        using (GraphicsPath shape = Rounded(body, radius))
        using (var clip = new Region(shape))
        {
            Region previous = g.Clip;
            g.Clip = clip;

            using (var fill = new SolidBrush(HarnessTheme.Panel.Title))
            {
                g.FillRectangle(fill, new Rectangle(0, 0, this.Width, titleHeight));
            }

            g.Clip = previous;

            using var edge = new Pen(HarnessTheme.Panel.Edge);
            g.DrawPath(edge, shape);
            g.DrawLine(edge, 1, titleHeight, this.Width - 2, titleHeight);
        }

        // The wells around the text boxes. Drawn here rather than by BorderStyle.FixedSingle, whose
        // colour comes from the system window frame and cannot be softened.
        using (var well = new Pen(HarnessTheme.Panel.Edge))
        {
            foreach (TextBox box in new[] { this._name, this._description, this._chat })
            {
                if (!box.Visible)
                {
                    continue;
                }

                Rectangle around = Rectangle.Inflate(box.Bounds, this.S(WellInsetPx), this.S(WellInsetPx));
                using GraphicsPath path = Rounded(
                    new Rectangle(around.X, around.Y, around.Width - 1, around.Height - 1),
                    this.S(CornerPx / 2));
                g.DrawPath(well, path);
            }
        }

        // Ellipsized rather than clipped: a four-word name is long, and a title cut off mid-glyph
        // reads as a rendering fault where "humble-thorn-ladder-…" reads as a name that continues.
        // The collapse button owns the right end of the strip, so the text stops before it.
        int pad = this.S(PadPx);
        var caption = new Rectangle(
            pad,
            0,
            Math.Max(this.Width - (pad * 2) - this._collapse.Width, 1),
            titleHeight);

        using var font = new Font(this.Font, FontStyle.Bold);
        TextRenderer.DrawText(
            g,
            this._harness?.NickName ?? "Harness",
            font,
            caption,
            HarnessTheme.Panel.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }

    // A rounded-rectangle path. Degenerates to the rectangle itself when there is no room to round.
    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(radius, 1) * 2;

        if (d >= bounds.Width || d >= bounds.Height)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Scales an unscaled design size to this monitor. DeviceDpi is 96 until the handle exists, which
    // is why OnHandleCreated re-lays out.
    private int S(int pixels) => (int)Math.Round(pixels * this.DeviceDpi / 96.0);

    // The title strip's height, from the bold font it has to hold rather than from a number.
    private int TitleHeight
    {
        get
        {
            using var font = new Font(this.Font, FontStyle.Bold);
            return font.Height + this.S(ButtonPadYPx * 2);
        }
    }

    private void ApplyCollapsed()
    {
        foreach (Control control in this.Controls)
        {
            // The collapse toggle and the way OUT both survive rolling up. Back to document is the
            // only non-destructive exit from a harness, so hiding it behind the toggle would strand
            // anyone who collapsed the panel — and it is collapsed by default.
            if (!ReferenceEquals(control, this._collapse) && !ReferenceEquals(control, this._back))
            {
                control.Visible = !this._collapsed;
            }
        }

        this._collapse.Text = this._collapsed ? "▾" : "▴";
        this.LayoutPanel();
    }

    private void BuildTitleRow()
    {
        this.StyleButton(this._collapse);
        this._collapse.BackColor = HarnessTheme.Panel.Title;
        this._collapse.FlatAppearance.BorderSize = 0;
        this._collapse.ForeColor = HarnessTheme.Panel.Muted;
        this._collapse.TabStop = false;
        this._collapse.Text = "▾";
        this._collapse.Click += (_, _) => this.Collapsed = !this.Collapsed;
        this.Controls.Add(this._collapse);

        this.StyleButton(this._back);
        this._back.Text = "← Back to document";
        this._back.Click += (_, _) => this._harness?.ReturnToHost();
        this.Controls.Add(this._back);
    }

    private void BuildFields()
    {
        this.AddLabel(this._nameLabel, "Name", this._name);
        this.AddBox(this._name, false);

        // Committed on leaving the box rather than per keystroke: the name is the project folder's
        // name, and renaming the folder once per typed character is not something to do to a disk.
        this._name.Leave += (_, _) => this.CommitName();
        this._name.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                this.CommitName();
            }
        };

        this.AddLabel(this._descriptionLabel, "What this pipeline is for", this._description);
        this.AddBox(this._description, true);
        this._description.TextChanged += (_, _) =>
        {
            if (!this._loading && this._harness is not null)
            {
                this._harness.ImportDescription = this._description.Text;
            }
        };

        this.AddLabel(this._chatLabel, "Chat window opening text", this._chat);
        this.AddBox(this._chat, true);
        this._chat.TextChanged += (_, _) =>
        {
            if (!this._loading && this._harness is not null)
            {
                this._harness.ChatText = this._chat.Text;
            }
        };
    }

    private void BuildActions()
    {
        this.StyleButton(this._save);

        // Named for what it does, not for what it writes. The user asks for a preset; .phy is the
        // format that happens to carry one, and the extension is the file dialog's business.
        this._save.Text = "Save as preset";
        this._save.ForeColor = HarnessTheme.Panel.Accent;
        this._save.Click += (_, _) => this._harness?.SaveAsPreset();
        this.Controls.Add(this._save);

        this.StyleButton(this._load);
        this._load.Text = "Load…";
        this._load.Click += (_, _) => this._harness?.LoadFromFile();
        this.Controls.Add(this._load);
    }

    /// <summary>
    /// Makes a click on this control take keyboard focus, explicitly.
    ///
    /// <para><b>Without this, typing into the panel went to the RHINO COMMAND LINE.</b> Rhino routes
    /// keystrokes to its command prompt unless the focused window is a text control, so anything that
    /// leaves focus where it was — on the canvas — means what you type is read as a Rhino command.
    /// Relying on WinForms' default click-to-focus is what left the door open: <c>GH_Canvas</c>
    /// derives from <c>Control</c>, not <c>ContainerControl</c>, so it carries none of the active-control
    /// machinery a form uses to move focus into a child.</para>
    ///
    /// <para>Grasshopper's own in-canvas editors do exactly this — <c>GH_TextBoxInputBase</c> adds its
    /// <c>TextBox</c> to the canvas and then calls <c>Focus()</c> on it outright, never waiting for a
    /// click to do it. Copying that is what makes the panel's fields behave like the box you get when
    /// you rename a component.</para>
    ///
    /// <para>Harmless where focus already worked: the handler runs after the control has taken focus
    /// on its own and sees nothing to do.</para>
    /// </summary>
    /// <param name="control">The control to make focusable by click.</param>
    private static void FocusOnClick(Control control) =>
        control.MouseDown += (sender, _) =>
        {
            if (sender is Control clicked && clicked.CanFocus && !clicked.Focused)
            {
                clicked.Focus();
            }
        };

    private void StyleButton(Button button)
    {
        FocusOnClick(button);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = HarnessTheme.Panel.Surface;
        button.ForeColor = HarnessTheme.Panel.Muted;
        button.FlatAppearance.BorderColor = HarnessTheme.Panel.Edge;
        button.FlatAppearance.MouseOverBackColor = HarnessTheme.Panel.Well;
        button.UseVisualStyleBackColor = false;
        button.AutoSize = false;
    }

    private void AddLabel(Label label, string text, TextBox? labels = null)
    {
        // A click on the label puts the caret in the field it names, the way a form label does. It
        // also means the gap between rows is not dead space that swallows a click and leaves focus
        // — and therefore typing — somewhere else entirely.
        if (labels is not null)
        {
            label.MouseDown += (_, _) => labels.Focus();
        }

        label.Text = text;
        label.ForeColor = HarnessTheme.Panel.Muted;
        label.AutoSize = false;

        // Truncated with an ellipsis rather than clipped, for the same reason the title is: a label
        // that runs out of room should say so.
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        this.Controls.Add(label);
    }

    private void AddBox(TextBox box, bool multiline)
    {
        FocusOnClick(box);

        // Escape hands keyboard control back to the canvas. While a field holds focus the canvas
        // sees no keys — correct, but it means Grasshopper's own shortcuts stop working until you
        // click away, and there should be a way out that is not "find somewhere safe to click".
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;

                // Activation as well as focus: the panel is its own window now, so focusing a
                // control in the editor without raising the editor would leave the keyboard here.
                Instances.DocumentEditor?.Activate();
                this._owner?.Focus();
            }
        };

        box.Multiline = multiline;

        // No border of its own: FixedSingle draws in the system window-frame colour, which cannot be
        // set and is far harder than anything else on the panel. OnPaint draws a soft rounded well
        // around the box instead, and the layout insets the box to leave room for it.
        box.BorderStyle = BorderStyle.None;
        box.BackColor = HarnessTheme.Panel.Well;
        box.ForeColor = HarnessTheme.Panel.Text;
        box.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None;
        box.WordWrap = multiline;
        this.Controls.Add(box);
    }

    // Applies a typed name. Blank falls back to the harness's generated name rather than being
    // obeyed: a nameless harness would resolve its project folder to nothing recognisable.
    private void CommitName()
    {
        if (this._loading || this._harness is null)
        {
            return;
        }

        string typed = this._name.Text.Trim();
        if (string.Equals(typed, this._harness.NickName, StringComparison.Ordinal))
        {
            return;
        }

        // The setter normalises a blank name and schedules the project folder to follow.
        this._harness.NickName = typed;
        this.RefreshName();
        this._harness.ExpireProxyLayout();
        this.Invalidate();
        Instances.ActiveCanvas?.Refresh();
    }

    // Measures the text a control has to hold, plus its padding.
    private Size MeasureButton(Button button)
    {
        Size text = TextRenderer.MeasureText(button.Text, this.Font);
        return new Size(text.Width + this.S(ButtonPadXPx * 2), text.Height + this.S(ButtonPadYPx * 2));
    }

    private void LayoutPanel()
    {
        int pad = this.S(PadPx);
        int gap = this.S(RowGapPx);
        int inset = this.S(WellInsetPx);
        int lineHeight = this.Font.Height;

        Size back = this.MeasureButton(this._back);
        Size save = this.MeasureButton(this._save);
        Size load = this.MeasureButton(this._load);
        int buttonHeight = Math.Max(back.Height, Math.Max(save.Height, load.Height));

        // Both action buttons take the width of the WIDER one, and the panel is sized to fit two of
        // those. Splitting the row in half instead is a subtler version of the bug the measured
        // layout is about: "Save as preset" is half again as wide as "Load…", so an even split clips
        // the long one however wide the panel is.
        int action = Math.Max(save.Width, load.Width);

        // The panel is sized to its CONTENT: the widest thing that has to fit, clamped so a long
        // label cannot make the panel enormous (labels ellipsize) and a short one cannot make it
        // too narrow to type a name into.
        int needed = Math.Max(back.Width, (action * 2) + gap);
        needed = Math.Max(needed, this.MeasureLabels());
        this.Width = Math.Clamp(needed + (pad * 2), this.S(MinWidthPx), this.S(MaxWidthPx));

        int inner = this.Width - (pad * 2);
        int titleHeight = this.TitleHeight;

        // The collapse glyph is square on the strip, right-aligned, vertically centred in it.
        int glyph = titleHeight - this.S(ButtonPadYPx);
        this._collapse.SetBounds(
            this.Width - glyph - pad,
            ((titleHeight - glyph) / 2) + 1,
            glyph,
            glyph);

        int y = titleHeight + 2 + pad;

        if (!this._collapsed)
        {
            y = this.Row(this._nameLabel, this._name, inner, pad, gap, inset, lineHeight, y);
            y = this.Row(this._descriptionLabel, this._description, inner, pad, gap, inset, lineHeight, y);
            y = this.Row(this._chatLabel, this._chat, inner, pad, gap, inset, lineHeight, y);

            int width = Math.Min(action, (inner - gap) / 2);
            this._save.SetBounds(pad, y, width, buttonHeight);
            this._load.SetBounds(this.Width - pad - width, y, width, buttonHeight);
            y += buttonHeight + gap;
        }

        // Last row, in both states: the exit is the one control that is always wanted, and rolled up
        // this puts it directly under the title strip.
        this._back.SetBounds(pad, y, inner, buttonHeight);
        y += buttonHeight;

        this.Height = y + pad;
        this.Reposition();

        // Rounded corners are a Region rather than paint, so the canvas shows through them instead of
        // the panel's own colour squaring off the shape drawn in OnPaint.
        using GraphicsPath shape = Rounded(new Rectangle(0, 0, this.Width, this.Height), this.S(CornerPx));
        this.Region?.Dispose();
        this.Region = new Region(shape);

        this.Invalidate();
    }

    private int MeasureLabels()
    {
        int widest = 0;
        foreach (Label label in new[] { this._nameLabel, this._descriptionLabel, this._chatLabel })
        {
            widest = Math.Max(widest, TextRenderer.MeasureText(label.Text, this.Font).Width);
        }

        return widest;
    }

    // One label-over-box row. The box's height is asked for, not assigned, because a single-line
    // TextBox derives its own from the font and silently ignores anything else — assigning one and
    // advancing by it is what made the first version's rows drift down the panel. The box is inset so
    // the well OnPaint draws around it lands on the row's full width.
    private int Row(Label label, TextBox box, int inner, int pad, int gap, int inset, int lineHeight, int y)
    {
        label.SetBounds(pad, y, inner, lineHeight + this.S(2));
        y += label.Height + this.S(2);

        int width = inner - (inset * 2);
        int top = y + inset;

        if (box.Multiline)
        {
            box.SetBounds(pad + inset, top, width, lineHeight * MultilineRows);
        }
        else
        {
            box.SetBounds(pad + inset, top, width, box.PreferredHeight);
        }

        return box.Bottom + inset + gap;
    }
}
