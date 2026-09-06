// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Eto.Drawing;
using Eto.Forms;
using Physalia.Core.Naming;

namespace Physalia.GH.Panels;

/// <summary>
/// A browser in a window of its own, for fetching a file a program is not allowed to fetch.
///
/// <para>The FALLBACK surface. The fetch belongs inside the chat window — that is where the block was
/// discovered and where the model explained it — so <see cref="BrowserFetch.Start"/> goes there
/// first. This is what happens when there is no chat window open, which the Download File node's
/// right-click menu allows.</para>
///
/// <para>The mechanism, and why it is a whole WebView rather than an iframe, is on
/// <see cref="BrowserFetch"/>; this class is only a frame around it.</para>
/// </summary>
internal sealed class BrowserFetchWindow : Form
{
    private readonly WebView _webView = new();
    private readonly Label _status = new();
    private readonly string _folder;
    private readonly Uri _target;

    private BrowserFetchWindow(Uri target, string folder)
    {
        this._target = target;
        this._folder = folder;

        this.Title = "Physalia — fetch a file";
        this.ClientSize = new Size(980, 720);
        this.Resizable = true;

        // Owned by Rhino rather than floating free, the same choice the chat window makes: it tracks
        // Rhino's z-order and drops behind whatever the user switches to.
        this.Owner = Rhino.UI.RhinoEtoApp.MainWindow;

        this._status.Text = $"Saving into {folder}";
        this._status.TextColor = Colors.Gray;

        var header = new Label
        {
            Text = target.ToString(),
            Font = SystemFonts.Bold(),
        };

        this.Content = new StackLayout
        {
            Orientation = Orientation.Vertical,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Padding(8),
            Spacing = 6,
            Items =
            {
                header,
                this._status,
                new StackLayoutItem(this._webView, expand: true),
            },
        };

        this.Shown += (_, _) => this.Start();
    }

    /// <summary>
    /// Opens a browser window on a URL and saves whatever it downloads into a folder.
    ///
    /// <para>The fallback surface. <see cref="BrowserFetch.Start"/> prefers the chat window, which is
    /// where the block was discovered; this is for when there is no chat window open — the node's
    /// right-click menu works with the chat closed.</para>
    /// </summary>
    /// <param name="target">The address to fetch; already validated.</param>
    /// <param name="folder">The project folder to save into; already created.</param>
    internal static void Open(Uri target, string folder)
    {
        ArgumentNullException.ThrowIfNull(target);
        new BrowserFetchWindow(target, folder).Show();
    }

    private void Start()
    {
        BrowserFetch.AttachRedirect(this._webView, this._folder, this.Report);
        this._webView.Url = this._target;
    }

    private void Report(string text) =>
        Application.Instance.AsyncInvoke(() => this._status.Text = text);
}
