// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Eto.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.Core.Config;
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

    // Shared client for the llama-server setup probe. A short timeout bounds the rare case where
    // packets to the default endpoint are dropped (a refused connection fails fast on its own).
    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    // How often the setup probe re-runs once a result is known, so the setup state clears within a
    // few seconds of the user adding a key / starting a local server (and reappears if removed).
    private static readonly TimeSpan ProviderProbeInterval = TimeSpan.FromSeconds(4);

    // Maps a setup-screen provider id to its API_KEY_CONFIG.YAML location ({section}.api_keys.{leaf}).
    // Only providers that authenticate with a pasted key appear here; Claude Code / local llama
    // need no stored key. Matches the sections in API_KEY_CONFIG.YAML.example.
    private static readonly Dictionary<string, (string Section, string Leaf)> KeyTargets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic"] = ("anthropic", "api_key"),
            ["google"] = ("gemini", "api_key"),
            ["openai"] = ("openai_compatible", "openai"),
            ["deepseek"] = ("openai_compatible", "deepseek"),
            ["openrouter"] = ("openai_compatible", "openrouter"),
        };

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
    private bool? _lastReady;
    private bool? _lastNeedsSetup;
    private string? _lastStatus;

    // Cached result of the async provider-availability probe: null until the first probe lands,
    // then true when some provider is configured. Mutated only on the UI thread (Tick / the
    // probe's AsyncInvoke continuation), so no locking is needed.
    private bool? _providersConfigured;
    private DateTime _lastProviderProbeUtc = DateTime.MinValue;
    private bool _providerProbeInFlight;

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
        Application.Instance.AsyncInvoke(() => Dispatch(uri));
    }

    // Routes a cancelled phbridge:// navigation by its host: submit a prompt, open an external
    // link in the system browser, or save a pasted API key. Runs on the UI thread.
    private void Dispatch(Uri uri)
    {
        switch (uri.Host)
        {
            case "submit":
                HandleSubmit(uri);
                break;
            case "open":
                HandleOpen(uri);
                break;
            case "savekey":
                HandleSaveKey(uri);
                break;
        }
    }

    // Opens an external setup link (http/https only) in the user's default browser. The chat runs
    // from file://, so an in-page navigation would replace it — links route here instead.
    private void HandleOpen(Uri uri)
    {
        string url = GetQueryValue(uri.Query, "url");
        if (string.IsNullOrEmpty(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"[Physalia] Could not open link: {ex.Message}");
        }
    }

    // Persists a pasted API key to API_KEY_CONFIG.YAML for the named provider, then forces a fresh
    // provider probe so the setup state clears once the key is detected, and reports the outcome
    // back to the page. The key is never logged.
    private void HandleSaveKey(Uri uri)
    {
        string provider = GetQueryValue(uri.Query, "provider");
        string key = GetQueryValue(uri.Query, "key");

        if (!KeyTargets.TryGetValue(provider, out (string Section, string Leaf) target))
        {
            PushSetupResult(provider, false, "Unknown provider.");
            return;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            PushSetupResult(provider, false, "Paste a non-empty API key, then press Enter.");
            return;
        }

        string path = GetApiKeyConfigPath();
        try
        {
            EnsureConfigFileExists(path);
            Api.SetKey(path, target.Section, target.Leaf, key.Trim());
        }
        catch (Exception ex)
        {
            PushSetupResult(provider, false, $"Could not save the key: {ex.Message}");
            return;
        }

        // Drop the cached result so the next tick re-probes; the now-present key resolves on the
        // first (synchronous) check, clearing the setup state without waiting for the interval.
        _providersConfigured = null;
        _lastProviderProbeUtc = DateTime.MinValue;

        PushSetupResult(provider, true, "API key saved to API_KEY_CONFIG.YAML. You're all set.");
    }

    // Copies the bundled template to API_KEY_CONFIG.YAML on first use so the standard provider
    // sections exist; if the template is missing, Api.SetKey creates a minimal file itself.
    private static void EnsureConfigFileExists(string path)
    {
        if (File.Exists(path))
        {
            return;
        }

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string example = path + ".example";
        if (File.Exists(example))
        {
            File.Copy(example, path);
        }
    }

    // One-shot push of a setup outcome to the page (the key is not included).
    private void PushSetupResult(string provider, bool ok, string message)
    {
        string json = JsonSerializer.Serialize(new { provider, ok, message }, WriteOpts);
        Exec($"window.physalia&&window.physalia.setSetupResult&&window.physalia.setSetupResult({json});");
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

        // First-run setup state: no LLM provider is configured at all (no API key, no Claude Code
        // CLI, no local llama-server). It takes precedence over the wiring hints below — there is
        // nothing to chat with until a provider exists. Detection is async; see MaybeProbeProviders.
        MaybeProbeProviders();
        bool needsSetup = _providersConfigured == false;

        // Once a provider is known to exist (chat mode, not setup), drop this window's Chatbox
        // onto the canvas if it isn't there yet — so the window is backed by a real component
        // immediately, without waiting for the first message.
        MaybePlaceComponent();

        // Pipeline-wiring readiness: chat needs Recorder -> Reasoner -> Model. Shown as a hint once
        // a provider exists but the graph isn't fully wired.
        bool ready = PromptPipelineView.IsPipelineReady(_component, 0);
        string status = needsSetup ? "Setup mode"
            : busy ? "Working…"
            : recorder is null ? "Connect a Recorder to begin."
            : !ready ? "Add a Reasoner with a Model to begin."
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

        if (connected != _lastConnected || busy != _lastBusy || ready != _lastReady
            || needsSetup != _lastNeedsSetup || status != _lastStatus)
        {
            _lastConnected = connected;
            _lastBusy = busy;
            _lastReady = ready;
            _lastNeedsSetup = needsSetup;
            _lastStatus = status;
            string state = JsonSerializer.Serialize(new { connected, busy, ready, needsSetup, status }, WriteOpts);
            Exec($"window.physalia&&window.physalia.setState({state});");
        }
    }

    // Kicks off the async provider-availability probe when none is in flight and either no result
    // is cached yet or the refresh interval has elapsed. The probe runs off the UI thread (it pings
    // a local server); its result is published back onto the UI thread, where Tick reads it.
    private void MaybeProbeProviders()
    {
        if (_providerProbeInFlight)
        {
            return;
        }

        if (_providersConfigured is not null && DateTime.UtcNow - _lastProviderProbeUtc < ProviderProbeInterval)
        {
            return;
        }

        _providerProbeInFlight = true;
        string configPath = GetApiKeyConfigPath();

        Task.Run(async () =>
        {
            bool configured;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                configured = await ProviderAvailability.AnyConfiguredAsync(configPath, ProbeClient, cts.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Keep the last known answer on failure; before any result, assume configured so the
                // setup screen never flashes during the very first probe.
                configured = _providersConfigured ?? true;
            }

            Application.Instance.AsyncInvoke(() =>
            {
                _providersConfigured = configured;
                _lastProviderProbeUtc = DateTime.UtcNow;
                _providerProbeInFlight = false;
            });
        });
    }

    // Drops this window's Chatbox onto the canvas, to the right of the window and vertically
    // centred on it, the moment a provider becomes available — but only when the component isn't
    // already on a document. A Chatbox loaded from a saved file or hand-placed by the user is left
    // exactly where it is (we never move it); and while in first-run setup (no provider) nothing is
    // placed, so the canvas isn't littered with an unusable component. If no document is open (the
    // window was opened from the widget with an empty canvas), a fresh document is created to host
    // the component — but only here, past the setup gate, so opening the setup page never makes
    // one. Runs on the UI thread (Tick).
    private void MaybePlaceComponent()
    {
        if (_component.OnPingDocument() is not null)
        {
            return; // already bound to a canvas component — leave it where the user/file put it
        }

        if (_providersConfigured != true)
        {
            return; // still probing, or first-run setup: nothing to chat with yet, so don't place
        }

        GH_Canvas canvas = Instances.ActiveCanvas;
        if (canvas is null)
        {
            return;
        }

        GH_Document? doc = canvas.Document ?? CreateActiveDocument(canvas);
        if (doc is null)
        {
            return;
        }

        if (_component.Attributes is null)
        {
            _component.CreateAttributes();
        }

        // Anchor = a few px right of the window's right edge, level with its vertical centre.
        System.Drawing.PointF anchor = AnchorRightOfWindow(canvas);

        // Provisional drop, then nudge so the component's left edge sits at the anchor and its
        // vertical centre lines up with it (Pivot is interior to the bounds, not a corner).
        _component.Attributes.Pivot = anchor;
        doc.AddObject(_component, false);

        // Match the rest of the canvas: show full parameter names when "Draw Full Names" is on.
        ComponentHelpers.ApplyNickNameDisplay(_component);

        // Force a layout so Bounds is valid (GH lays attributes out lazily, so a just-added
        // component's Bounds is otherwise stale), then shift Pivot by the gap between where the
        // bounds landed and where we want them.
        _component.Attributes.ExpireLayout();
        _component.Attributes.PerformLayout();
        System.Drawing.RectangleF bounds = _component.Attributes.Bounds;
        float dx = anchor.X - bounds.Left;
        float dy = anchor.Y - (bounds.Top + (bounds.Height / 2f));
        _component.Attributes.Pivot = new System.Drawing.PointF(
            _component.Attributes.Pivot.X + dx,
            _component.Attributes.Pivot.Y + dy);
        _component.Attributes.ExpireLayout();
        _component.Attributes.PerformLayout();
        canvas.Refresh();
    }

    // Creates a fresh, empty document and makes it the canvas's active one, so a chat started with
    // no file open has a real canvas to drop the Chatbox onto. Returns null if the document server
    // is unavailable. Runs on the UI thread (Tick), so touching the canvas/document is safe.
    private static GH_Document? CreateActiveDocument(GH_Canvas canvas)
    {
        GH_DocumentServer? server = Instances.DocumentServer;
        if (server is null)
        {
            return null;
        }

        var doc = new GH_Document();
        server.AddDocument(doc);
        canvas.Document = doc;
        return doc;
    }

    // The canvas-world point a few pixels right of the window's right edge, level with its vertical
    // centre. Uses the native window rect (device px) so it lines up with the floating window
    // regardless of pan/zoom; falls back to the viewport centre off Windows or if the rect is
    // unavailable.
    private System.Drawing.PointF AnchorRightOfWindow(GH_Canvas canvas)
    {
        const int PaddingPx = 12;
#if WINDOWS
        if (NativeHandle != IntPtr.Zero && GetWindowRect(NativeHandle, out RECT r))
        {
            var screenPt = new System.Drawing.Point(r.Right + PaddingPx, (r.Top + r.Bottom) / 2);
            System.Drawing.Point clientPt = canvas.PointToClient(screenPt);
            return canvas.Viewport.UnprojectPoint(new System.Drawing.PointF(clientPt.X, clientPt.Y));
        }
#endif
        return canvas.Viewport.MidPoint;
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
#endif

    // Path to API_KEY_CONFIG.YAML beside the plug-in (matches the ApiKeys component's resolution).
    private static string GetApiKeyConfigPath()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return assemblyDir is null
            ? "API_KEY_CONFIG.YAML"
            : Path.Combine(assemblyDir, "Files", "API_KEY_CONFIG.YAML");
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
