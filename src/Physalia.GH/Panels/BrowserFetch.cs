// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.IO;
using Eto.Forms;
using Physalia.Core.Naming;

namespace Physalia.GH.Panels;

/// <summary>
/// Fetching a file that a program is not allowed to fetch, by being a browser.
///
/// <para>The mechanism, shared by the two places it is shown: inside the chat window, which is where
/// it belongs, and in a window of its own for when there is no chat window open. Both hosts do the
/// same thing — navigate a real Chromium <c>WebView</c> and redirect what it downloads — so the
/// redirect lives here rather than twice.</para>
///
/// <para><b>Why a whole WebView and not an iframe in the chat page.</b> An iframe was the obvious
/// answer and it cannot work: a host that puts a challenge in front of its files also sends
/// <c>X-Frame-Options: SAMEORIGIN</c> (verified on <c>webtransfer.vancouver.ca</c>), so the page
/// refuses to be framed at all. A second native WebView navigates at the top level of its own
/// browser, which is the thing the header is about, and gets no such refusal.</para>
///
/// <para><b>What makes it honest.</b> This IS a browser with a person watching it, which is what the
/// protection is asking for. Deliberately NOT done: lifting the <c>cf_clearance</c> cookie out
/// afterwards to make plain <c>download_file</c> work on that host. It would be fragile — clearance
/// is bound to user agent, address and TLS fingerprint — and it amounts to working around a
/// protection rather than satisfying it.</para>
/// </summary>
internal static class BrowserFetch
{
    /// <summary>
    /// Shows a browser on a URL, saving whatever it downloads into a folder.
    ///
    /// <para>Prefers the chat window: the block is discovered mid-conversation, and that is where the
    /// user already is. Falls back to a window of its own only when there is no chat window to put it
    /// in — the node's right-click menu can be used with the chat closed.</para>
    /// </summary>
    /// <param name="url">The address to fetch. Must be absolute http(s).</param>
    /// <param name="folder">The project folder to save into.</param>
    internal static void Start(string? url, string? folder)
    {
        if (!TryResolve(url, folder, out Uri? target, out string problem))
        {
            Rhino.UI.Dialogs.ShowMessage(problem, "Fetch in Browser");
            return;
        }

        // Preferred surface first: the chat window, where the block was explained. It refuses when
        // its page is not up yet, which is what the standalone window is for.
        if (Components.Chat.ActiveWindow is { } window && window.TryShowFetch(target, folder!))
        {
            return;
        }

        BrowserFetchWindow.Open(target, folder!);
    }

    /// <summary>
    /// Validates a URL and destination before anything is shown.
    /// </summary>
    /// <param name="url">The address asked for.</param>
    /// <param name="folder">The project folder.</param>
    /// <param name="target">The parsed address.</param>
    /// <param name="problem">What to tell the user when it cannot be used.</param>
    /// <returns>True when the fetch can go ahead.</returns>
    internal static bool TryResolve(
        string? url,
        string? folder,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? target,
        out string problem)
    {
        target = null;
        problem = string.Empty;

        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            problem = $"\"{url}\" is not an http or https address.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            problem = "This node has no project folder, so there is nowhere to save the file. Wire a "
                + "Project Folder grounder into it, or type a folder name.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"The project folder could not be created: {ex.Message}";
            return false;
        }

        target = parsed;
        return true;
    }

    /// <summary>
    /// Works out where a download should land: the browser's suggested file name, sanitized to one
    /// segment and joined to the project folder.
    ///
    /// <para>The suggestion comes from the PAGE, so it is checked exactly as a model-supplied name
    /// is — a page proposing <c>..\..\Startup\run.bat</c> must not be obeyed.</para>
    /// </summary>
    /// <param name="folder">The project folder.</param>
    /// <param name="suggested">The path the browser proposed.</param>
    /// <param name="target">The resolved destination.</param>
    /// <returns>True when the destination is inside the project folder.</returns>
    internal static bool TryPlace(string folder, string? suggested, out string target)
    {
        target = string.Empty;

        string name = Path.GetFileName(suggested ?? string.Empty);
        string stem = ProjectPaths.FolderKey(Path.GetFileNameWithoutExtension(name));
        string extension = Path.GetExtension(name);

        try
        {
            target = Path.GetFullPath(Path.Combine(folder, stem + extension));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return ProjectPaths.IsContained(folder, target);
    }

    /// <summary>
    /// Points a WebView's downloads at a folder instead of the browser's own.
    ///
    /// <para><c>CoreWebView2.DownloadStarting</c> hands over a settable <c>ResultFilePath</c>, which
    /// is the whole trick: the file is written where the pipeline is looking and never touches the
    /// download directory, so there is nothing to move afterwards.</para>
    ///
    /// <para>Windows only. Eto's WebView is WebView2 here and WKWebView on macOS, whose download API
    /// differs; there the caller is told the file will land in the browser's own folder, so the
    /// feature degrades rather than disappearing.</para>
    /// </summary>
    /// <param name="webView">The view to hook.</param>
    /// <param name="folder">Where downloads should go.</param>
    /// <param name="report">Called with progress and outcome, for the host to display.</param>
    internal static async void AttachRedirect(WebView webView, string folder, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(report);

#if WINDOWS
        try
        {
            // The route the chat window already takes to CoreWebView2: Eto exposes the native
            // control, reached dynamically to avoid referencing the Wpf assembly.
            dynamic native = webView.ControlObject;
            await (System.Threading.Tasks.Task)native.EnsureCoreWebView2Async(null);
            Microsoft.Web.WebView2.Core.CoreWebView2? core = native.CoreWebView2;

            if (core is null)
            {
                report("The browser could not be started, so the download cannot be redirected.");
                return;
            }

            core.DownloadStarting += (_, e) => OnDownloadStarting(e, folder, report);
        }
        catch (Exception ex)
        {
            report($"The download could not be redirected: {ex.Message}");
            Rhino.RhinoApp.WriteLine($"[Physalia] Browser fetch hook failed: {ex.Message}");
        }
#else
        await System.Threading.Tasks.Task.CompletedTask;
        report(
            "This build cannot redirect the download, so the file will land in your browser's own "
            + $"download folder. Move it into {folder} afterwards.");
#endif
    }

#if WINDOWS
    private static void OnDownloadStarting(
        Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs e,
        string folder,
        Action<string> report)
    {
        Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation operation = e.DownloadOperation;

        if (!TryPlace(folder, operation.ResultFilePath, out string target))
        {
            e.Cancel = true;
            report("That download proposed a file name that would land outside the project folder, so it was refused.");
            return;
        }

        e.ResultFilePath = target;

        // Handled suppresses WebView2's own download bar, because the host reports progress itself
        // and two indicators disagreeing is worse than either.
        e.Handled = true;

        string name = Path.GetFileName(target);
        report($"Downloading {name}…");

        operation.BytesReceivedChanged += (_, _) =>
        {
            // TotalBytesToReceive is ulong? — a server can decline to say — and a cast through long
            // is safe for any size a download actually is.
            ulong? total = operation.TotalBytesToReceive;
            string size = total is > 0
                ? $"{Core.Files.FileDownload.Describe(operation.BytesReceived)} of "
                    + Core.Files.FileDownload.Describe((long)total.Value)
                : Core.Files.FileDownload.Describe(operation.BytesReceived);

            report($"Downloading {name} — {size}");
        };

        operation.StateChanged += (_, _) =>
        {
            switch (operation.State)
            {
                case Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed:
                    report(
                        $"Saved {name} ({Core.Files.FileDownload.Describe(operation.BytesReceived)}). "
                        + "The pipeline will pick it up on its own.");
                    Rhino.RhinoApp.WriteLine($"[Physalia] Fetched in browser: {target}");
                    break;

                case Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Interrupted:
                    report($"{name} stopped: {operation.InterruptReason}.");
                    break;

                default:
                    break;
            }
        };
    }
#endif
}
