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
/// <para><b>It is a real control, not a widget, and it had to be.</b> A <c>GH_Widget</c> is painted
/// into the canvas in device pixels and has no input controls of any kind — fine for the two pills
/// this replaces, impossible for three text fields. So the panel is an ordinary WinForms control
/// parented to the <c>GH_Canvas</c>, which is itself a <c>Control</c>. Parenting rather than floating
/// a separate form is what makes it behave: it is positioned in client coordinates so it stays pinned
/// to the corner for free, it z-orders above the canvas by construction, it takes its own mouse and
/// keyboard input without fighting the canvas for them, and it cannot end up behind Rhino or adrift
/// on a second monitor the way an owned form can.</para>
///
/// <para>The three fields live on the <see cref="HarnessComponent"/>, not here — a setting is only
/// worth anything if it travels, and these are exactly the fields a <c>.phy</c> carries to whoever
/// the workflow is shared with. This panel is a view onto them.</para>
///
/// <para>It can be rolled up to its title bar, because it is a large thing to leave permanently over
/// the top-left of a canvas somebody is working on. That choice is remembered across sessions in
/// Grasshopper's own settings, the way the chat widget remembers its offsets.</para>
/// </summary>
internal sealed class HarnessPanel : UserControl
{
    private const string CollapsedKey = "Physalia.HarnessPanel.Collapsed";

    private const int Margin = 14;
    private const int PanelWidth = 260;
    private const int Pad = 10;
    private const int RowGap = 6;
    private const int TitleHeight = 28;
    private const int ButtonHeight = 24;

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
    private bool _collapsed;

    internal HarnessPanel()
    {
        this.DoubleBuffered = true;
        this.BackColor = HarnessTheme.Fill;
        this.ForeColor = HarnessTheme.Ink;
        this.Width = PanelWidth;

        this.BuildTitleRow();
        this.BuildFields();
        this.BuildActions();

        this.Collapsed = Instances.Settings.GetValue(CollapsedKey, false);
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

            foreach (Control control in this.Controls)
            {
                if (!ReferenceEquals(control, this._collapse) && !ReferenceEquals(control, this._back))
                {
                    control.Visible = !value;
                }
            }

            this._collapse.Text = value ? "▾" : "▴";
            Instances.Settings.SetValue(CollapsedKey, value);
            this.LayoutPanel();
        }
    }

    /// <summary>
    /// Points the panel at a harness, or at nothing.
    /// </summary>
    /// <param name="harness">The harness the canvas is inside, or null when it is not inside one.</param>
    internal void Show(HarnessComponent? harness)
    {
        this._harness = harness;
        this.Visible = harness is not null;

        if (harness is null)
        {
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

        this.LayoutPanel();
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
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var body = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
        using (var edge = new Pen(HarnessTheme.Edge))
        {
            g.DrawRectangle(edge, body);
        }

        // The title strip carries the family's aqua so the panel reads as Physalia's at a glance,
        // the same job the pills' fill used to do.
        var title = new Rectangle(1, 1, this.Width - 2, TitleHeight);
        using (var fill = new SolidBrush(HarnessTheme.Aqua))
        {
            g.FillRectangle(fill, title);
        }

        using (var rule = new Pen(HarnessTheme.Edge))
        {
            g.DrawLine(rule, 1, TitleHeight + 1, this.Width - 2, TitleHeight + 1);
        }

        string caption = this._harness?.NickName ?? "Harness";
        using var ink = new SolidBrush(HarnessTheme.Ink);
        using var font = new Font(this.Font, FontStyle.Bold);
        g.DrawString(caption, font, ink, new PointF(Pad, 7f));
    }

    private void BuildTitleRow()
    {
        this._collapse.Text = "▴";
        this._collapse.FlatStyle = FlatStyle.Flat;
        this._collapse.FlatAppearance.BorderSize = 0;
        this._collapse.BackColor = HarnessTheme.Aqua;
        this._collapse.ForeColor = HarnessTheme.Ink;
        this._collapse.Size = new Size(22, 20);
        this._collapse.TabStop = false;
        this._collapse.Click += (_, _) => this.Collapsed = !this.Collapsed;
        this.Controls.Add(this._collapse);

        this._back.Text = "← Back to document";
        this._back.FlatStyle = FlatStyle.Flat;
        this._back.BackColor = HarnessTheme.Fill;
        this._back.ForeColor = HarnessTheme.Ink;
        this._back.Height = ButtonHeight;
        this._back.Click += (_, _) => this._harness?.ReturnToHost();
        this.Controls.Add(this._back);
    }

    private void BuildFields()
    {
        this.AddLabel(this._nameLabel, "Name");
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

        this.AddLabel(this._descriptionLabel, "What this pipeline is for");
        this.AddBox(this._description, true);
        this._description.TextChanged += (_, _) =>
        {
            if (!this._loading && this._harness is not null)
            {
                this._harness.ImportDescription = this._description.Text;
            }
        };

        this.AddLabel(this._chatLabel, "Chat window opening text");
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
        this._save.Text = "Save as .phy";
        this._load.Text = "Load…";

        foreach (Button button in new[] { this._save, this._load })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = HarnessTheme.Fill;
            button.ForeColor = HarnessTheme.Ink;
            button.Height = ButtonHeight;
            this.Controls.Add(button);
        }

        this._save.Click += (_, _) => this._harness?.SaveAsPreset();
        this._load.Click += (_, _) => this._harness?.LoadFromFile();
    }

    private void AddLabel(Label label, string text)
    {
        label.Text = text;
        label.ForeColor = HarnessTheme.Ink;
        label.AutoSize = false;
        label.Height = 15;
        this.Controls.Add(label);
    }

    private void AddBox(TextBox box, bool multiline)
    {
        box.Multiline = multiline;
        box.Height = multiline ? 46 : 22;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None;
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

    private void LayoutPanel()
    {
        this._collapse.Location = new Point(this.Width - this._collapse.Width - Pad, 5);

        int y = TitleHeight + 2 + Pad;
        int inner = this.Width - (Pad * 2);

        this._back.SetBounds(Pad, y, inner, ButtonHeight);
        y += ButtonHeight + RowGap;

        if (!this.Collapsed)
        {
            y = this.Row(this._nameLabel, this._name, inner, y);
            y = this.Row(this._descriptionLabel, this._description, inner, y);
            y = this.Row(this._chatLabel, this._chat, inner, y);

            int half = (inner - RowGap) / 2;
            this._save.SetBounds(Pad, y, half, ButtonHeight);
            this._load.SetBounds(Pad + half + RowGap, y, inner - half - RowGap, ButtonHeight);
            y += ButtonHeight;
        }

        this.Height = y + Pad;
        this.Location = new Point(Margin, Margin);
    }

    private int Row(Label label, TextBox box, int inner, int y)
    {
        label.SetBounds(Pad, y, inner, label.Height);
        y += label.Height + 2;
        box.SetBounds(Pad, y, inner, box.Height);
        return y + box.Height + RowGap;
    }
}
