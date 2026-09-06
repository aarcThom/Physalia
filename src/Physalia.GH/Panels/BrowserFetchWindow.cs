// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.IO;
using Eto.Drawing;
using Eto.Forms;
using Physalia.Core.Naming;

namespace Physalia.GH.Panels;

/// <summary>
/// Fetches a file that a program is not allowed to fetch, by being a browser — and saves it straight
/// into the project folder.
///
/// <para><b>Why this exists.</b> Plenty of the data an architect wants sits behind a bot challenge.
/// Vancouver's LiDAR host answers every programmatic request with 403 and a Cloudflare challenge
/// page, a full browser User-Agent included, while downloading perfectly in a browser. A managed
/// challenge is passed by executing JavaScript, so no HTTP client gets through it, and telling the
/// user to download the file by hand and then move it into the right folder is two chores for
/// something the plug-in can do properly.</para>
///
/// <para><b>What makes it honest.</b> This IS a browser — the same Chromium WebView2 the chat window
/// runs on — with a person watching it. The site gets a real browser and a real user, which is what
/// its protection is asking for. Deliberately NOT done: lifting the <c>cf_clearance</c> cookie out of
/// this WebView afterwards to make plain <c>download_file</c> work on that host. It would be fragile
/// (clearance is bound to user agent, address and TLS fingerprint) and it would amount to working
/// around a protection the site owner switched on, rather than satisfying it.</para>
///
/// <para><b>The download is redirected, not followed.</b> <c>CoreWebView2.DownloadStarting</c> hands
/// over a settable <c>ResultFilePath</c>, so the file is written into the project folder and never
/// touches the browser's own download directory — which is the whole point, since the folder is where
/// the pipeline is looking. The Project Folder grounder watches it, so the model sees the file
/// arrive without anything else being told.</para>
///
/// <para>Windows only for the redirect: Eto's WebView is WebView2 here and WKWebView on macOS, whose
/// download API is different. Off Windows the window still opens and navigates, and says plainly that
/// the file will land in the browser's own download folder instead.</para>
/// </summary>
internal sealed class BrowserFetchWindow : Form
{
    private readonly WebView _webView = new();
    private readonly Label _status = new();
    private readonly string _folder;
    private readonly Uri _target;

    private bool _hooked;

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
    /// </summary>
    /// <param name="url">The address to fetch. Must be absolute http(s).</param>
    /// <param name="folder">The project folder to save into; created if missing.</param>
    /// <returns>The window, already shown, or null when the arguments were unusable.</returns>
    internal static BrowserFetchWindow? Open(string? url, string? folder)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? target)
            || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
        {
            Rhino.UI.Dialogs.ShowMessage(
                $"\"{url}\" is not an http or https address.", "Fetch a File");
            return null;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            Rhino.UI.Dialogs.ShowMessage(
                "This node has no project folder, so there is nowhere to save the file. Wire a Project "
                + "Folder grounder into it, or type a folder name.",
                "Fetch a File");
            return null;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Rhino.UI.Dialogs.ShowMessage($"The project folder could not be created: {ex.Message}", "Fetch a File");
            return null;
        }

        var window = new BrowserFetchWindow(target, folder);
        window.Show();
        return window;
    }

    private void Start()
    {
#if WINDOWS
        this.HookDownloads();
#else
        this._status.Text =
            "This build cannot redirect the download, so the file will land in your browser's own "
            + $"download folder. Move it into {this._folder} afterwards.";
#endif

        this._webView.Url = this._target;
    }

    // Where a download should land: the browser's suggested file name, sanitized to one segment and
    // joined to the project folder. The suggestion comes from the page, so it is checked exactly as a
    // model-supplied name is — a page proposing ..\..\Startup\run.bat must not be obeyed.
    private bool TryPlace(string? suggested, out string target)
    {
        target = string.Empty;

        string name = Path.GetFileName(suggested ?? string.Empty);
        string stem = ProjectPaths.FolderKey(Path.GetFileNameWithoutExtension(name));
        string extension = Path.GetExtension(name);

        try
        {
            target = Path.GetFullPath(Path.Combine(this._folder, stem + extension));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return ProjectPaths.IsContained(this._folder, target);
    }

    private void Report(string text) =>
        Application.Instance.AsyncInvoke(() => this._status.Text = text);

#if WINDOWS
    private async void HookDownloads()
    {
        if (this._hooked)
        {
            return;
        }

        this._hooked = true;

        try
        {
            // The same route the chat window takes to CoreWebView2: Eto exposes the native control,
            // and it is reached dynamically to avoid referencing the Wpf assembly.
            dynamic native = this._webView.ControlObject;
            await (System.Threading.Tasks.Task)native.EnsureCoreWebView2Async(null);
            Microsoft.Web.WebView2.Core.CoreWebView2? core = native.CoreWebView2;

            if (core is null)
            {
                this.Report("The browser could not be started; the download cannot be redirected.");
                return;
            }

            core.DownloadStarting += this.OnDownloadStarting;
        }
        catch (Exception ex)
        {
            this.Report($"The download could not be redirected: {ex.Message}");
            Rhino.RhinoApp.WriteLine($"[Physalia] Browser fetch hook failed: {ex.Message}");
        }
    }

    private void OnDownloadStarting(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs e)
    {
        Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation operation = e.DownloadOperation;

        if (!this.TryPlace(operation.ResultFilePath, out string target))
        {
            e.Cancel = true;
            this.Report("That download proposed a file name that would land outside the project folder, so it was refused.");
            return;
        }

        e.ResultFilePath = target;

        // Handled suppresses WebView2's own download bar, because this window reports progress
        // itself and two progress indicators disagreeing is worse than either.
        e.Handled = true;

        string name = Path.GetFileName(target);
        this.Report($"Downloading {name}…");

        operation.BytesReceivedChanged += (_, _) =>
        {
            // TotalBytesToReceive is ulong? — a server can decline to say, and a cast through long
            // is safe for any size a download actually is.
            ulong? total = operation.TotalBytesToReceive;
            string size = total is > 0
                ? $"{Core.Files.FileDownload.Describe(operation.BytesReceived)} of {Core.Files.FileDownload.Describe((long)total.Value)}"
                : Core.Files.FileDownload.Describe(operation.BytesReceived);

            this.Report($"Downloading {name} — {size}");
        };

        operation.StateChanged += (_, _) =>
        {
            switch (operation.State)
            {
                case Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed:
                    this.Report(
                        $"Saved {name} ({Core.Files.FileDownload.Describe(operation.BytesReceived)}) into {this._folder}. "
                        + "The pipeline will pick it up on its own — you can close this window.");
                    Rhino.RhinoApp.WriteLine($"[Physalia] Fetched in browser: {target}");
                    break;

                case Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Interrupted:
                    this.Report($"{name} stopped: {operation.InterruptReason}.");
                    break;

                default:
                    break;
            }
        };
    }
#endif
}
