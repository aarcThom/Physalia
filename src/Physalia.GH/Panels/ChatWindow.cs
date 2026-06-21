// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Eto.Forms;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Components;

namespace Physalia.GH.Panels;

/// <summary>
/// Standalone Eto window hosting the Svelte chat UI (Physalia.UI) for a <see
/// cref="Chatbox"/> component. The UI is a single self-contained HTML file built by
/// Physalia.UI and shipped at Files/UI/chat.html; it is loaded from disk via file://
/// (the bundle inlines all JS/CSS, so there are no cross-origin module fetches).
///
///   JS -> C# : the page stashes the outgoing message as JSON on the window and
///              navigates to phbridge://submit; this class cancels that navigation and
///              pulls the JSON back with __physaliaTake() (the payload is far larger
///              than a URL can carry once images are attached).
///   C# -> JS : on a UI timer this class reads the wired Recorder's conversation, live
///              stream, and busy state (via <see cref="PromptPipelineView"/>) and pushes
///              the changed parts to window.physalia.{setHistory,setStream,setState}.
/// </summary>
public class ChatWindow : Form
{
    private const string BridgeScheme = "phbridge";

    private static readonly JsonSerializerOptions WriteOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions ReadOpts =
        new() { PropertyNameCaseInsensitive = true };

    private const string MissingHtml =
        "<!doctype html><html><body style='font:13px sans-serif;padding:24px;color:#333'>"
        + "<h3>Physalia chat UI not found</h3>"
        + "<p>Expected <code>Files/UI/chat.html</code> next to the plug-in. "
        + "Build the <b>Physalia.UI</b> project (<code>npm run build</code>, or "
        + "<code>dotnet build -p:BuildUI=true</code>) to generate it.</p></body></html>";

    private readonly Chatbox _component;
    private readonly WebView _webView;
    private readonly UITimer _timer;
    private bool _loaded;

    // last-pushed state, for change detection so we only ExecuteScript on a real change
    private Conversation? _lastConversation;
    private string? _lastStream;
    private bool? _lastConnected;
    private bool? _lastBusy;
    private string? _lastStatus;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatWindow"/> class.
    /// </summary>
    /// <param name="component">The Chatbox component this window drives.</param>
    public ChatWindow(Chatbox component)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));

        Title = "Physalia Chat";
        ClientSize = new Eto.Drawing.Size(460, 620);
        Resizable = true;
        Owner = Rhino.UI.RhinoEtoApp.MainWindow;

        // Float above the Rhino/Grasshopper canvas at all times — the user can keep clicking
        // and editing the canvas while the chat stays visible on top.
        Topmost = true;

        _webView = new WebView();
        _webView.DocumentLoading += OnDocumentLoading;
        Content = _webView;

        // GH never re-solves on a wire connection, so polling is the simplest correct
        // refresh (same cadence as Prompter's busy animation). Ticks run on the UI thread.
        _timer = new UITimer { Interval = 0.15 };
        _timer.Elapsed += (_, _) => Tick();

        // Load once the native handle exists — loading in the ctor is dropped on some backends.
        Shown += (_, _) =>
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            LoadUi();
            _timer.Start();
            HookHostClose();
#if WINDOWS
            HookWebMessage();
#endif
        };

        Closed += (_, _) =>
        {
            _timer.Stop();
            UnhookHostClose();
        };
    }

    // Close this window when its host goes away, so it never orphans on the desktop:
    //   - Grasshopper editor closed (X button) — Windows only (the editor is WinForms).
    //   - Rhino quitting — cross-platform.
    private void HookHostClose()
    {
        Rhino.RhinoApp.Closing += OnHostClosing;
#if WINDOWS
        System.Windows.Forms.Form? editor = Grasshopper.Instances.DocumentEditor;
        if (editor is not null)
        {
            _ghEditor = editor;
            editor.FormClosed += OnGhEditorClosed;
        }
#endif
    }

    private void UnhookHostClose()
    {
        Rhino.RhinoApp.Closing -= OnHostClosing;
#if WINDOWS
        if (_ghEditor is not null)
        {
            _ghEditor.FormClosed -= OnGhEditorClosed;
            _ghEditor = null;
        }
#endif
    }

    private void OnHostClosing(object? sender, EventArgs e) => CloseFromHost();

    // Host-close callbacks may arrive off the UI thread — marshal the Close onto it.
    private void CloseFromHost() => Application.Instance.AsyncInvoke(() =>
    {
        try
        {
            Close();
        }
        catch
        {
            // already torn down — nothing to close
        }
    });

#if WINDOWS
    // The Grasshopper editor window this chat was opened from; held so we can unsubscribe.
    private System.Windows.Forms.Form? _ghEditor;

    private void OnGhEditorClosed(object? sender, System.Windows.Forms.FormClosedEventArgs e)
        => CloseFromHost();
#endif

    // Loads the built Svelte app from disk (file://), or an explanatory page if missing.
    private void LoadUi()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string htmlPath = assemblyDir is null
            ? string.Empty
            : Path.Combine(assemblyDir, "Files", "UI", "chat.html");

        if (!string.IsNullOrEmpty(htmlPath) && File.Exists(htmlPath))
        {
            _webView.Url = new Uri(htmlPath);
        }
        else
        {
            _webView.LoadHtml(MissingHtml);
        }
    }

    // JS->C# bridge: JS navigates to phbridge://submit ; we cancel and handle it instead.
    // The same intercept works on WebView2 (Windows) and WKWebView (Mac).
    private void OnDocumentLoading(object? sender, WebViewLoadingEventArgs e)
    {
        if (e.Uri.Scheme != BridgeScheme)
        {
            return;
        }

        e.Cancel = true; // must be synchronous to actually cancel the navigation

        // Defer the work off the navigation callback: running a GH solve (or ExecuteScript)
        // synchronously here re-enters the WebView core mid-navigation and crashes Rhino
        // (same hazard as ManageImagesDialog's grid-edit deferral). The Tick loop renders
        // the recorded turn afterwards.
        Uri uri = e.Uri;
        Application.Instance.AsyncInvoke(() => HandleSubmit(uri));
    }

    // Turns a phbridge://submit navigation into a Prompt Signal. Text rides in the URL
    // query (?text=...) — the small, proven path. An image-bearing message instead sets
    // ?images=1 and stashes the full JSON on the page, which we pull back here.
    private async void HandleSubmit(Uri uri)
    {
        string query = uri.Query;

        if (!QueryFlagSet(query, "images"))
        {
            string text = GetQueryValue(query, "text");
            if (!string.IsNullOrEmpty(text))
            {
                _component.SubmitFromWindow(text);
            }

            return;
        }

        // Fallback for the image path (non-WebView2, e.g. Mac WKWebView): the page stashed the
        // JSON and we pull it back. UNRELIABLE on WebView2 right after a cancelled navigation
        // (ExecuteScript runs in a transient context and returns null) — which is why the
        // primary image path is now WebMessageReceived (see OnWebMessage). Windows never takes
        // this branch (send() there uses postMessage).
        string raw;
        try
        {
            raw = await _webView.ExecuteScriptAsync("window.__physaliaTake ? window.__physaliaTake() : ''")
                ?? string.Empty;
        }
        catch
        {
            return; // page torn down between the navigation and this deferred call
        }

        SubmitJsonPayload(raw);
    }

#if WINDOWS
    // Subscribe to the WebView2 WebMessageReceived channel — the reliable JS->C# path for the
    // image-bearing payload (window.chrome.webview.postMessage). Eto exposes the native WebView2
    // control via ControlObject; reach its CoreWebView2 (dynamic, to avoid a Wpf-assembly
    // reference) and add the handler once the core is initialized.
    private async void HookWebMessage()
    {
        try
        {
            dynamic native = _webView.ControlObject;
            await (Task)native.EnsureCoreWebView2Async(null);
            Microsoft.Web.WebView2.Core.CoreWebView2? core = native.CoreWebView2;
            if (core is not null)
            {
                core.WebMessageReceived += OnWebMessage;
            }
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"[Physalia] WebMessage hook failed: {ex.Message}");
        }
    }

    private void OnWebMessage(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch
        {
            try
            {
                json = e.WebMessageAsJson;
            }
            catch
            {
                return;
            }
        }

        // Defer off the WebView callback (same re-entrancy hazard as the navigation handler).
        Application.Instance.AsyncInvoke(() => SubmitJsonPayload(json));
    }
#endif

    // Parses a {text, images[]} JSON payload (from the postMessage channel, or the pull
    // fallback) into interleaved content blocks (text first, then images) and submits it.
    private void SubmitJsonPayload(string raw)
    {
        SubmitMessage? message = ParseSubmitMessage(raw);
        if (message is null)
        {
            return;
        }

        string msgText = message.Text ?? string.Empty;
        IReadOnlyList<SubmitImage> images = message.Images ?? (IReadOnlyList<SubmitImage>)Array.Empty<SubmitImage>();

        var blocks = new List<MessageContent>();
        if (!string.IsNullOrEmpty(msgText))
        {
            blocks.Add(new TextContent(msgText));
        }

        foreach (SubmitImage image in images)
        {
            if (string.IsNullOrEmpty(image.Base64))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(image.Base64);
            }
            catch
            {
                continue;
            }

            blocks.Add(new ImageContent(new InlineImage(bytes, image.MediaType ?? "image/png")));
        }

        if (blocks.Count == 0)
        {
            return;
        }

        _component.SubmitFromWindow(msgText, blocks);
    }

    // Parses the JSON returned by __physaliaTake(). Eto's ExecuteScript may hand back the
    // value already JSON-encoded (a quoted string), so unwrap one layer if needed.
    private static SubmitMessage? ParseSubmitMessage(string raw)
    {
        raw = raw?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return null;
        }

        if (raw.StartsWith("\"", StringComparison.Ordinal))
        {
            try
            {
                raw = JsonSerializer.Deserialize<string>(raw) ?? raw;
            }
            catch
            {
                // not a wrapped string — fall through and try as-is
            }
        }

        try
        {
            return JsonSerializer.Deserialize<SubmitMessage>(raw, ReadOpts);
        }
        catch
        {
            return null;
        }
    }

    private static bool QueryFlagSet(string query, string key)
        => !string.IsNullOrEmpty(GetQueryValue(query, key));

    // Minimal query-string reader (avoids a System.Web dependency).
    private static string GetQueryValue(string query, string key)
    {
        if (string.IsNullOrEmpty(query))
        {
            return string.Empty;
        }

        foreach (string pair in query.TrimStart('?').Split('&'))
        {
            int eq = pair.IndexOf('=');
            string name = eq < 0 ? pair : pair.Substring(0, eq);
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                continue;
            }

            string value = eq < 0 ? string.Empty : pair.Substring(eq + 1);
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return string.Empty;
    }

    // Pulls current pipeline state and pushes only what changed to the page.
    private void Tick()
    {
        if (!_loaded)
        {
            return;
        }

        Recorder? recorder = PromptPipelineView.FindRecorder(_component, 0);
        Conversation? convo = recorder?.ActiveConversation;
        bool busy = recorder is not null && PromptPipelineView.IsPipelineBusy(recorder);
        bool connected = recorder is not null;
        string status = recorder is null ? "Connect a Recorder to begin."
            : busy ? "Working…"
            : string.Empty;

        if (!ReferenceEquals(convo, _lastConversation))
        {
            _lastConversation = convo;
            string payload = JsonSerializer.Serialize(BuildMessages(convo), WriteOpts);
            Exec($"window.physalia&&window.physalia.setHistory({payload});");
        }

        string? stream = busy ? PromptPipelineView.GetStreamingText(recorder!) : null;
        if (stream != _lastStream)
        {
            _lastStream = stream;
            Exec($"window.physalia&&window.physalia.setStream({JsonSerializer.Serialize(stream)});");
        }

        if (connected != _lastConnected || busy != _lastBusy || status != _lastStatus)
        {
            _lastConnected = connected;
            _lastBusy = busy;
            _lastStatus = status;
            string state = JsonSerializer.Serialize(new { connected, busy, status }, WriteOpts);
            Exec($"window.physalia&&window.physalia.setState({state});");
        }
    }

    // Maps the committed conversation to the UI message shape (text / images / tool calls).
    private static List<UiMessage> BuildMessages(Conversation? convo)
    {
        var messages = new List<UiMessage>();
        if (convo is null)
        {
            return messages;
        }

        // Tool results live in a later user-role message keyed by the call id; collect them first.
        var results = new Dictionary<string, ToolResultContent>();
        foreach (ConversationMessage message in convo.Messages)
        {
            foreach (MessageContent block in message.Content)
            {
                if (block is ToolResultContent result)
                {
                    results[result.ToolCallId] = result;
                }
            }
        }

        int index = 0;
        foreach (ConversationMessage message in convo.Messages)
        {
            index++;
            string role = message.Role == Role.Assistant ? "assistant" : "user";

            var textParts = new List<string>();
            var images = new List<UiImage>();
            var tools = new List<UiTool>();
            bool sawNonToolResult = false;

            foreach (MessageContent block in message.Content)
            {
                switch (block)
                {
                    case TextContent text:
                        if (!string.IsNullOrEmpty(text.Text))
                        {
                            textParts.Add(text.Text);
                        }

                        sawNonToolResult = true;
                        break;

                    case ImageContent { Source: InlineImage inline }:
                        images.Add(new UiImage(Convert.ToBase64String(inline.Data), inline.MimeType));
                        sawNonToolResult = true;
                        break;

                    case ImageContent:
                        sawNonToolResult = true; // URL/managed images aren't rendered inline
                        break;

                    case ToolCallContent call:
                        tools.Add(BuildTool(call, results));
                        sawNonToolResult = true;
                        break;

                    case ToolResultContent:
                        break; // surfaced on the matching tool call, not as its own turn
                }
            }

            // A user turn that carries only tool results is the result carrier — hide it.
            if (!sawNonToolResult)
            {
                continue;
            }

            messages.Add(new UiMessage(
                $"m{index}",
                role,
                string.Join("\n\n", textParts),
                images.Count > 0 ? images : null,
                tools.Count > 0 ? tools : null));
        }

        return messages;
    }

    private static UiTool BuildTool(ToolCallContent call, IReadOnlyDictionary<string, ToolResultContent> results)
    {
        object? input = ParseJsonOrString(call.InputJson);

        if (results.TryGetValue(call.Id, out ToolResultContent? result))
        {
            return result.IsError
                ? new UiTool(call.Id, call.Name, "output-error", input, null, result.Content)
                : new UiTool(call.Id, call.Name, "output-available", input, result.Content, null);
        }

        return new UiTool(call.Id, call.Name, "input-available", input, null, null);
    }

    // Tool input renders nicest as a real object; fall back to the raw string if not JSON.
    private static object? ParseJsonOrString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return json;
        }
    }

    // C#->JS push. Uses the ASYNC API: the synchronous ExecuteScript blocks Rhino's UI
    // thread until the script returns, which (at 6–10 Hz while streaming) froze the whole
    // window during a solve. Fire-and-forget instead; swallow the early-tick race and any
    // faulted task so it never surfaces as an unobserved exception.
    private void Exec(string script)
    {
        try
        {
            _webView.ExecuteScriptAsync(script)
                .ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
        }
        catch
        {
            // page not ready yet (or torn down) — next tick will resend the current state
        }
    }

    private sealed record UiImage(string Base64, string MediaType);

    private sealed record UiTool(string Id, string Name, string State, object? Input, string? Output, string? ErrorText);

    private sealed record UiMessage(
        string Id,
        string Role,
        string Text,
        IReadOnlyList<UiImage>? Images,
        IReadOnlyList<UiTool>? Tools);

    private sealed record SubmitImage(string Base64, string MediaType, string Filename);

    private sealed record SubmitMessage(string Text, List<SubmitImage>? Images);
}
