// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Eto.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Clusters;
using Physalia.Core.Grounding.Components;
using Physalia.Core.Grounding.Tools;
using Physalia.GH.Components;

namespace Physalia.GH.Panels;

/// <summary>
/// Standalone Eto window hosting the Svelte chat UI (Physalia.UI) for a <see
/// cref="Chat"/> component. The UI is a single self-contained HTML file built by
/// Physalia.UI and embedded in this assembly (Physalia.GH.chat.html); <see cref="LoadUi"/>
/// extracts it to a temp file and loads it via file:// (the bundle inlines all JS/CSS, so
/// there are no cross-origin module fetches).
///
///   JS -> C# : the page stashes the outgoing message as JSON on the window and
///              navigates to phbridge://submit; this class cancels that navigation and
///              pulls the JSON back with __physaliaTake() (the payload is far larger
///              than a URL can carry once images are attached).
///   C# -> JS : on a UI timer this class reads the wired ConversationLog's conversation, live
///              stream, and busy state (via <see cref="PromptPipelineView"/>) and pushes
///              the changed parts to window.physalia.{setHistory,setStream,setState}.
/// </summary>
public class ChatWindow : Form
{
    private const string BridgeScheme = "phbridge";

    // Clearance (canvas units) kept around a newly placed harness proxy, and how far down the
    // free-spot sweep will step before letting the proxy land where it falls. The cap only exists so
    // a pathologically tall column of components cannot spin the search.
    private const float PlacementGap = 20f;
    private const int MaxPlacementRows = 40;

    private static readonly JsonSerializerOptions WriteOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions ReadOpts =
        new() { PropertyNameCaseInsensitive = true };

    // Indented JSON for the exported transcript, so tool inputs stay readable in a .txt.
    private static readonly JsonSerializerOptions TranscriptOpts =
        new() { WriteIndented = true };

    // Shared client for the llama-server setup probe. A short timeout bounds the rare case where
    // packets to the default endpoint are dropped (a refused connection fails fast on its own).
    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    // Common Rhino unit-system names offered in the grounding window's Document Units dropdown. These
    // match Rhino.UnitSystem.ToString() so an override reads like the live document value. The live
    // document units (and any current override) are merged in at push time, so uncommon systems still
    // appear when in use.
    private static readonly string[] UnitOptions =
    {
        "Millimeters", "Centimeters", "Meters", "Kilometers", "Inches", "Feet", "Yards", "Miles",
    };

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
            ["tavily"] = ("web_search", "tavily"),
            ["jina"] = ("web_search", "jina"),
        };

    // Setup-screen ids that are web-tool keys (Web Search / Read URL), not chat-model providers.
    // They show as configured pills but do NOT satisfy the first-run LLM requirement.
    private static readonly HashSet<string> ToolProviderIds =
        new(StringComparer.OrdinalIgnoreCase) { "tavily", "jina" };

    private const string MissingHtml =
        "<!doctype html><html><body style='font:13px sans-serif;padding:24px;color:#333'>"
        + "<h3>Physalia chat UI not found</h3>"
        + "<p>Expected <code>Files/UI/chat.html</code> next to the plug-in. "
        + "Build the <b>Physalia.UI</b> project (<code>npm run build</code>, or "
        + "<code>dotnet build -p:BuildUI=true</code>) to generate it.</p></body></html>";

    // The Chat this window is currently viewing. Mutable: the switcher row at the bottom of
    // the window (and a double-click on another Chat) rebinds it to a different component,
    // so one window can move between every Chat on the canvas. Always non-null.
    private Chat _component;
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
    private string? _lastConfigured;
    private string? _lastPresetSignature;
    private string? _lastChats;
    private string? _lastGroundingSignature;
    private int? _lastTokenCount;

    // Set on a Chat switch: forces the next Tick to push history/stream/state unconditionally,
    // even when the newly viewed component's values equal the reset caches (e.g. a fresh component
    // with no conversation pushes null == null, which change-detection would otherwise suppress —
    // leaving the previous component's messages on screen). Cleared after that one push.
    private bool _forcePush;

    // Cached result of the async provider-availability probe: null until the first probe lands,
    // then the setup-ids of the configured providers (empty when none → first-run setup). Mutated
    // only on the UI thread (Tick / the probe's AsyncInvoke continuation), so no locking is needed.
    private IReadOnlyList<string>? _configuredProviders;
    private DateTime _lastProviderProbeUtc = DateTime.MinValue;
    private bool _providerProbeInFlight;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatWindow"/> class.
    /// </summary>
    /// <param name="component">The Chat component this window drives.</param>
    public ChatWindow(Chat component)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));

        Title = "Physalia Chat";
        ClientSize = new Eto.Drawing.Size(460, 620);
        Resizable = true;

        // Float above the Rhino/Grasshopper canvas so the user can keep editing the canvas with the
        // chat visible — but NOT above every other application (that's what Topmost would do). On
        // Windows the window is re-owned by the Grasshopper editor once shown (OwnToGrasshopperEditor),
        // so it tracks GH's z-order and drops behind whatever app the user switches to. The Eto owner
        // is the cross-platform fallback (e.g. macOS, or if the editor handle is unavailable).
        Owner = Rhino.UI.RhinoEtoApp.MainWindow;

        _webView = new WebView();
        _webView.DocumentLoading += OnDocumentLoading;
        Content = _webView;

        // GH never re-solves on a wire connection, so polling is the simplest correct
        // refresh (same cadence as the Chat's busy animation). Ticks run on the UI thread.
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
            PositionOverGrasshopperEditor();
            OwnToGrasshopperEditor();
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

    // Logical name of the chat UI bundle embedded by Physalia.GH's EmbedChatHtml build target.
    private const string ChatHtmlResource = "Physalia.GH.chat.html";

    // Loads the built Svelte app, or an explanatory page if it isn't embedded.
    // The bundle is embedded in this assembly (not shipped loose in Files/); we extract it to a
    // version-keyed temp file and load that via file://. file:// is required because the ~3 MB
    // bundle exceeds the WebView NavigateToString/LoadHtml size ceiling, and it keeps the load
    // path identical across WebView2 (Windows) and WKWebView (Mac).
    private void LoadUi()
    {
        string? tempPath = TryExtractChatHtml();
        if (tempPath is not null)
        {
            _webView.Url = new Uri(tempPath);
        }
        else
        {
            _webView.LoadHtml(MissingHtml);
        }
    }

    // Writes the embedded chat bundle to %TEMP%/Physalia/chat-<version>.html and returns its
    // path, or null when the resource isn't embedded (e.g. a -p:BuildUI=false build). The file is
    // version-keyed and reused when already present with the expected size, so we don't rewrite
    // ~3 MB on every window open.
    private static string? TryExtractChatHtml()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream? resource = assembly.GetManifestResourceStream(ChatHtmlResource);
        if (resource is null)
        {
            return null;
        }

        string version = assembly.GetName().Version?.ToString() ?? "0";
        string dir = Path.Combine(Path.GetTempPath(), "Physalia");
        string path = Path.Combine(dir, $"chat-{version}.html");

        if (File.Exists(path) && new FileInfo(path).Length == resource.Length)
        {
            return path;
        }

        Directory.CreateDirectory(dir);
        using (FileStream file = File.Create(path))
        {
            resource.CopyTo(file);
        }

        return path;
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
            case "placeemptyharness":
                HandlePlaceEmptyHarness();
                break;
            case "placepreset":
                HandlePlacePreset(uri);
                break;
            case "clearall":
                HandleClearAll();
                break;
            case "selectchat":
                HandleSelectChat(uri);
                break;
            case "setgrounding":
                HandleSetGrounding(uri);
                break;
            case "setsignatures":
                HandleSetSignatures(uri);
                break;
            case "setclusters":
                HandleSetClusters(uri);
                break;
            case "settools":
                HandleSetTools(uri);
                break;
            case "setunits":
                HandleSetUnits(uri);
                break;
            case "setsnapshotmessage":
                HandleSetSnapshotMessage(uri);
                break;
            case "setsnapshotsends":
                HandleSetSnapshotSends(uri);
                break;
            case "sendsnapshot":
                // The geometry button: capture a viewport snapshot of the generated geometry and
                // send it (with its predefined message) as its own user message, right now.
                _component.SendGeometrySnapshotFromWindow();
                break;
            case "attachsnapshot":
                // The same geometry button with "Send With Default Message" unchecked: capture the
                // snapshot and hand it to the prompt box as an attachment instead of sending it.
                HandleAttachSnapshot();
                break;
            case "setviewsnapshotmessage":
                HandleSetViewSnapshotMessage(uri);
                break;
            case "setviewsnapshotsends":
                HandleSetViewSnapshotSends(uri);
                break;
            case "sendviewsnapshot":
                // The view button: capture the active viewport as-is and send it (with its predefined
                // message) as its own user message, right now. No geometry needed, no camera move.
                _component.SendViewSnapshotFromWindow();
                break;
            case "attachviewsnapshot":
                // The same view button with "Send With Default Message" unchecked: capture the view and
                // hand it to the prompt box as an attachment instead of sending it.
                HandleAttachViewSnapshot();
                break;
            case "exportconversation":
                // The export button (an Export Conversation human tool is wired): write the viewed
                // conversation to a plain-text transcript.
                HandleExportConversation();
                break;
            case "opensignaltrace":
                // The signal-trace button (a Signal Trace human tool is wired): open the session's
                // trace window. The log is process-wide, so this is a door, not a per-chat view.
                SignalTraceWindow.ShowOrFocus();
                break;
            case "cancel":
                HandleCancel();
                break;
        }
    }

    // Applies the send-with-default-message switch from the window's Geometry Snapshot page. ?on=1
    // sends the snapshot as its own message; anything else attaches it to the prompt box. The flag is
    // set on the wired Geometry Snapshot component itself (same field its context menu toggles), so the
    // window and the canvas can never drift apart — the new value comes back on the next state push.
    private void HandleSetSnapshotSends(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        conversationLog.SetGeometrySnapshotSendsMessage(GetQueryValue(uri.Query, "on") == "1");
    }

    // Captures a viewport snapshot of the generated geometry and pushes it into the composer's
    // pending-attachment strip, where it behaves exactly like a pasted image: the human types their
    // own message and the snapshot rides that turn. Nothing is minted here — this is the attach half
    // of the geometry button, taken when the wired Geometry Snapshot tool has its default message
    // switched off. Silently does nothing when there is nothing to capture (the button is already
    // gated on armed geometry). Runs on the UI thread, where the viewport zoom + capture are safe.
    private void HandleAttachSnapshot()
    {
        if (!_component.TryCaptureGeneratedGeometryPng(out byte[]? png) || png is null)
        {
            return;
        }

        PushAttachment("attachSnapshot", png);
    }

    // Applies the send-with-default-message switch from the window's View Snapshot page — the
    // view-snapshot twin of HandleSetSnapshotSends, setting the flag on the wired View Snapshot
    // component so the canvas menu and the window stay one setting.
    private void HandleSetViewSnapshotSends(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        conversationLog.SetViewSnapshotSendsMessage(GetQueryValue(uri.Query, "on") == "1");
    }

    // Captures the active viewport as-is and pushes it into the composer's pending-attachment strip,
    // where it behaves exactly like a pasted image — the attach half of the view button, taken when the
    // wired View Snapshot tool has its default message switched off. Nothing is minted here; the capture
    // rides the human's own turn. Runs on the UI thread, where a viewport capture is safe.
    private void HandleAttachViewSnapshot()
    {
        if (!_component.TryCaptureViewPng(out byte[]? png) || png is null)
        {
            return;
        }

        PushAttachment("attachViewSnapshot", png);
    }

    // Hands a captured PNG to the page through the named host hook, which drops it into the composer's
    // pending attachments on the lane belonging to the tool that captured it.
    private void PushAttachment(string hook, byte[] png)
    {
        string json = JsonSerializer.Serialize(
            new UiImage(Convert.ToBase64String(png), "image/png"), WriteOpts);
        Exec($"window.physalia&&window.physalia.{hook}&&window.physalia.{hook}({json});");
    }

    // Cancels the active inference on the wired pipeline's LLM Call(s). Fired by the chat window's
    // cancel button, which the UI enables only while the pipeline is busy. Runs on the UI thread.
    private void HandleCancel()
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        PromptPipelineView.CancelPipeline(conversationLog);
    }

    // Applies a grounding selection from the window to the wired ConversationLog. The payload is JSON
    // {all:bool, leaves:[[category,subCategory],...]} passed in the ?sel= query. all:true (or a
    // missing payload) clears the selection back to null = include everything.
    private void HandleSetGrounding(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        string raw = GetQueryValue(uri.Query, "sel");
        GroundingSelection? selection = null;
        if (!string.IsNullOrEmpty(raw))
        {
            try
            {
                GroundingSelectionPayload? payload = JsonSerializer.Deserialize<GroundingSelectionPayload>(raw, ReadOpts);
                if (payload is not null && !payload.All)
                {
                    IEnumerable<(string, string)> leaves = (payload.Leaves ?? new List<List<string>>())
                        .Where(l => l is { Count: >= 2 })
                        .Select(l => (l[0] ?? string.Empty, l[1] ?? string.Empty));
                    selection = GroundingSelection.FromLeaves(leaves);
                }
                else if (payload is not null)
                {
                    // "Reset to all" means literally everything, plug-ins included — an EXPLICIT
                    // selection of every leaf. A null selection is no longer that: it is the
                    // native-only default, which the window re-checking every leaf would contradict.
                    selection = GroundingSelection.All(conversationLog.AvailableGroundingTree);
                }
            }
            catch (JsonException)
            {
                return;
            }
        }

        conversationLog.SetGroundingSelection(selection);
    }

    // Applies the expose-signatures toggle from the window's grounding panel to the wired ConversationLog.
    // ?on=1 folds typed component signatures into the prompt; anything else reverts to names only.
    private void HandleSetSignatures(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        conversationLog.SetExposeSignatures(GetQueryValue(uri.Query, "on") == "1");
    }

    // Applies a cluster selection from the window to the wired ConversationLog. The payload is JSON
    // {all:bool, names:[...]} passed in the ?sel= query. all:true (or a missing payload) clears the
    // selection back to null = include every available cluster.
    private void HandleSetClusters(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        string raw = GetQueryValue(uri.Query, "sel");
        ClusterSelection? selection = null;
        if (!string.IsNullOrEmpty(raw))
        {
            try
            {
                ClusterSelectionPayload? payload = JsonSerializer.Deserialize<ClusterSelectionPayload>(raw, ReadOpts);
                if (payload is not null && !payload.All)
                {
                    selection = ClusterSelection.FromNames(payload.Names ?? new List<string>());
                }
            }
            catch (JsonException)
            {
                return;
            }
        }

        conversationLog.SetClusterSelection(selection);
    }

    // Applies a tools selection from the window to the wired ConversationLog. The payload is JSON
    // {all:bool, names:[...]} passed in the ?sel= query. all:true (or a missing payload) clears the
    // selection back to null = advertise every tool present on the canvas.
    private void HandleSetTools(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        string raw = GetQueryValue(uri.Query, "sel");
        ToolsSelection? selection = null;
        if (!string.IsNullOrEmpty(raw))
        {
            try
            {
                ToolsSelectionPayload? payload = JsonSerializer.Deserialize<ToolsSelectionPayload>(raw, ReadOpts);
                if (payload is not null && !payload.All)
                {
                    selection = ToolsSelection.FromNames(payload.Names ?? new List<string>());
                }
            }
            catch (JsonException)
            {
                return;
            }
        }

        conversationLog.SetToolsSelection(selection);
    }

    // Applies a document-units override from the window to the wired ConversationLog. The payload is JSON
    // {reset:bool, units:string} passed in the ?sel= query. reset:true (or a missing payload) clears
    // the override back to null = use the live document units. The document itself is never changed.
    private void HandleSetUnits(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        string raw = GetQueryValue(uri.Query, "sel");
        string? units = null;
        if (!string.IsNullOrEmpty(raw))
        {
            try
            {
                UnitsOverridePayload? payload = JsonSerializer.Deserialize<UnitsOverridePayload>(raw, ReadOpts);
                if (payload is not null && !payload.Reset)
                {
                    units = payload.Units;
                }
            }
            catch (JsonException)
            {
                return;
            }
        }

        conversationLog.SetUnitsOverride(units);
    }

    // Applies a geometry-snapshot message override from the window to the wired ConversationLog. The
    // payload is JSON {reset:bool, message:string} passed in the ?sel= query. reset:true (or a
    // missing payload) clears the override back to null = use the tool's default message.
    private void HandleSetSnapshotMessage(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        if (TryReadSnapshotMessage(uri, out string? message))
        {
            conversationLog.SetSnapshotMessageOverride(message);
        }
    }

    // Applies a view-snapshot message override from the window to the wired ConversationLog — the
    // view-snapshot twin of HandleSetSnapshotMessage, same payload shape under its own verb.
    private void HandleSetViewSnapshotMessage(Uri uri)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return;
        }

        if (TryReadSnapshotMessage(uri, out string? message))
        {
            conversationLog.SetViewSnapshotMessageOverride(message);
        }
    }

    // Reads a {reset:bool, message:string} payload out of the ?sel= query. A null message means "use
    // the tool's default" — the payload asked for a reset, or there was no payload at all. False means
    // the JSON was malformed, so the caller leaves the current override alone instead of clearing it.
    private static bool TryReadSnapshotMessage(Uri uri, out string? message)
    {
        message = null;
        string raw = GetQueryValue(uri.Query, "sel");
        if (string.IsNullOrEmpty(raw))
        {
            return true;
        }

        try
        {
            SnapshotMessagePayload? payload = JsonSerializer.Deserialize<SnapshotMessagePayload>(raw, ReadOpts);
            if (payload is not null && !payload.Reset)
            {
                message = payload.Message;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The names of the components currently exposed to the model (the grounded catalog with the
    // grounding selection applied). Used to normalize "/c/<tab>/<name>" prompt tokens at submit time.
    private IReadOnlyCollection<string> IncludedComponentNames()
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return Array.Empty<string>();
        }

        return conversationLog.IncludedComponentEntries
            .Select(e => e.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The names of the clusters currently exposed to the model (the chat-selected subset, or all when
    // no selection is set). Used to normalize "/cl/<name>" prompt tokens at submit time.
    private IReadOnlyCollection<string> IncludedClusterNames()
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return Array.Empty<string>();
        }

        IEnumerable<string> names = conversationLog.AvailableClusters.Select(c => c.Name);
        ClusterSelection? selection = conversationLog.ClusterSelectionOrNull;
        return (selection is null ? names : names.Where(selection.Includes)).ToList();
    }

    // The names of the tools currently exposed to the model (the chat-selected subset, or all present
    // when no selection is set). Used to normalize "/t/<name>" prompt tokens at submit time.
    private IReadOnlyCollection<string> IncludedToolNames()
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog is null)
        {
            return Array.Empty<string>();
        }

        IEnumerable<string> names = conversationLog.AvailableToolNames;
        ToolsSelection? selection = conversationLog.ToolsSelectionOrNull;
        return (selection is null ? names : names.Where(selection.Includes)).ToList();
    }

    // Rewrites "/c/<tab>/<component>", "/cl/<clustername>" and "/t/<toolname>" references (including the
    // memory tool's "/t/memory/global" / "/t/memory/local" scope form) in submitted prompt text into
    // clear natural-language mentions the model understands. The markers are distinct, so the three
    // resolvers compose in any order without interfering.
    private string NormalizeRefs(string text) =>
        PromptToolResolver.Normalize(
            PromptClusterResolver.Normalize(
                PromptComponentResolver.Normalize(text, IncludedComponentNames()),
                IncludedClusterNames()),
            IncludedToolNames());

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
        _configuredProviders = null;
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
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _component.SubmitFromWindow(NormalizeRefs(text));
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

        string msgText = NormalizeRefs(message.Text ?? string.Empty);
        IReadOnlyList<SubmitImage> images = message.Images ?? (IReadOnlyList<SubmitImage>)Array.Empty<SubmitImage>();

        // Image intake is gated on the human tools that grant it: Add Image (paste/drop/picker) and
        // either snapshot tool in attach mode, whose capture rides the prompt box. The UI disables the
        // affordances it has no grant for, but the wire state can flip between compose and submit — drop
        // stale images here (keeping the text) so no image ever bypasses the tool contract.
        if (images.Count > 0 && PromptPipelineView.FindConversationLog(_component, 0)?.AcceptsPromptImages != true)
        {
            images = Array.Empty<SubmitImage>();
        }

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

    // Saves the viewed conversation as a plain-text transcript — the raw material for a bug
    // report: every turn verbatim (assistant <think> reasoning and raw JSON/Python replies
    // included), each tool call with its input and result. Fired by the window's export button,
    // which the UI shows only while an Export Conversation human tool is wired. Runs on the UI thread.
    private void HandleExportConversation()
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        List<UiMessage> messages = BuildMessages(conversationLog?.ActiveConversation);
        if (messages.Count == 0)
        {
            MessageBox.Show(
                this,
                "There is no conversation to export yet.",
                "Export conversation",
                MessageBoxButtons.OK,
                MessageBoxType.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Export conversation",
            FileName = $"physalia-chat-{DateTime.Now:yyyyMMdd-HHmm}.txt",
        };
        dialog.Filters.Add(new FileFilter("Text files", ".txt"));
        dialog.Filters.Add(new FileFilter("All files", ".*"));

        if (dialog.ShowDialog(this) != DialogResult.Ok || string.IsNullOrEmpty(dialog.FileName))
        {
            return;
        }

        string path = dialog.FileName;
        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            path += ".txt";
        }

        try
        {
            File.WriteAllText(path, BuildTranscript(messages));
            Rhino.RhinoApp.WriteLine($"[Physalia] Conversation exported to {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not write {path}: {ex.Message}",
                "Export conversation",
                MessageBoxButtons.OK,
                MessageBoxType.Error);
        }
    }

    // Renders the conversation to transcript text. Turn text is written verbatim (so <think>
    // blocks and raw JSON survive exactly as the model sent them); images cannot ride a .txt,
    // so each becomes a size-stamped placeholder.
    private static string BuildTranscript(IReadOnlyList<UiMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Physalia conversation export");
        sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Messages: {messages.Count}");

        foreach (UiMessage message in messages)
        {
            string role = message.Role == "assistant"
                ? "ASSISTANT"
                : message.Feedback ? "USER (auto-generated feedback)" : "USER";

            sb.AppendLine();
            sb.AppendLine(new string('=', 72));
            sb.AppendLine($"[{role}]");
            sb.AppendLine();

            foreach (UiImage image in message.Images ?? (IReadOnlyList<UiImage>)Array.Empty<UiImage>())
            {
                // Base64 is 4 chars per 3 bytes; close enough for a size stamp.
                int kilobytes = image.Base64.Length * 3 / 4 / 1024;
                sb.AppendLine($"[attached image: {image.MediaType}, ~{kilobytes} KB — omitted from text export]");
            }

            if (!string.IsNullOrEmpty(message.Text))
            {
                sb.AppendLine(message.Text);
            }

            foreach (UiTool tool in message.Tools ?? (IReadOnlyList<UiTool>)Array.Empty<UiTool>())
            {
                sb.AppendLine();
                sb.AppendLine($"--- tool call: {tool.Name} (id: {tool.Id}, state: {tool.State}) ---");
                if (tool.Input is not null)
                {
                    sb.AppendLine("input:");
                    sb.AppendLine(tool.Input as string ?? JsonSerializer.Serialize(tool.Input, TranscriptOpts));
                }

                if (!string.IsNullOrEmpty(tool.Output))
                {
                    sb.AppendLine("output:");
                    sb.AppendLine(tool.Output);
                }

                if (!string.IsNullOrEmpty(tool.ErrorText))
                {
                    sb.AppendLine("error:");
                    sb.AppendLine(tool.ErrorText);
                }
            }
        }

        return sb.ToString();
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

        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        Conversation? convo = conversationLog?.ActiveConversation;
        bool busy = conversationLog is not null && PromptPipelineView.IsPipelineBusy(conversationLog);
        bool connected = conversationLog is not null;

        // First-run setup state: no LLM provider is configured at all (no API key, no Claude Code
        // CLI, no local llama-server). It takes precedence over the wiring hints below — there is
        // nothing to chat with until a provider exists. Detection is async; see MaybeProbeProviders.
        // The list of ready providers is surfaced on the setup screen ("already set up"); null means
        // the first probe hasn't landed yet, so assume configured and don't flash setup.
        MaybeProbeProviders();
        IReadOnlyList<string> configuredProviders = _configuredProviders ?? Array.Empty<string>();
        // First-run setup needs an LLM provider, not merely a web-tool key (Tavily/Jina): show setup
        // once the probe has landed and no chat-model provider is configured.
        bool needsSetup = _configuredProviders is not null && !HasLlmProvider();

        // Whether the Chat this window is viewing sits in a harness yet. Nothing is placed until the
        // user asks, so this only picks the wording of the status line below — the placement options
        // themselves stay on offer for good, because a document may hold any number of harnesses.
        bool harnessPlaced = Harness.PhyDocuments.IsHarnessDocument(_component.OnPingDocument());

        // Pipeline-wiring readiness: chat needs ConversationLog -> [compactor…] -> LLM Call -> Model. Shown
        // as a hint once a provider exists but the graph isn't fully wired.
        bool ready = PromptPipelineView.IsPipelineReady(_component, 0);
        string status = needsSetup ? "Setup mode"
            : busy ? "Working…"
            : conversationLog is null && !harnessPlaced ? "Choose an option above to begin."
            : conversationLog is null ? "Harness placed — open it on the canvas and wire a Conversation Log to the Chat."
            : !ready ? "Add an LLM Call with a Model — directly or through a compactor — to begin."
            : string.Empty;

        if (_forcePush || !ReferenceEquals(convo, _lastConversation))
        {
            _lastConversation = convo;
            string payload = JsonSerializer.Serialize(BuildMessages(convo), WriteOpts);
            Exec($"window.physalia&&window.physalia.setHistory({payload});");
        }

        string? stream = busy ? PromptPipelineView.GetStreamingText(conversationLog!) : null;
        if (_forcePush || stream != _lastStream)
        {
            _lastStream = stream;
            Exec($"window.physalia&&window.physalia.setStream({JsonSerializer.Serialize(stream)});");
        }

        // Token counter: the estimate from a Token Estimator wired downstream of this chat's
        // ConversationLog, or null (counter hidden) when none is wired or it has no count yet.
        int? tokenCount = conversationLog is null ? null : PromptPipelineView.GetDownstreamTokenCount(conversationLog);
        if (_forcePush || tokenCount != _lastTokenCount)
        {
            _lastTokenCount = tokenCount;
            Exec($"window.physalia&&window.physalia.setTokenCount({JsonSerializer.Serialize(tokenCount)});");
        }

        // Serialised form of the id list, used both as the change signature and the wire payload.
        string configuredJson = JsonSerializer.Serialize(configuredProviders, WriteOpts);

        // Grounding state for the window's grounding panel: whether a component catalog is wired
        // (greys the icon when not), the available tab → panels tree, and the EFFECTIVE selection —
        // never null-as-all, so the native-only default renders with plug-in tabs unchecked. The
        // selection's flat leaves are regrouped to the tree's shape.
        bool groundingWired = conversationLog?.HasComponentGrounding == true;
        bool exposeSignatures = conversationLog?.ExposeComponentSignatures == true;
        var groundingTree = (conversationLog?.AvailableGroundingTree ?? Array.Empty<CatalogCategory>())
            .Select(c => new { category = c.Category, subCategories = c.SubCategories })
            .ToList();
        object? groundingSelection = null;
        if (conversationLog?.EffectiveGroundingSelection is { } sel)
        {
            groundingSelection = sel.Leaves
                .GroupBy(l => l.Category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    category = g.Key,
                    subCategories = g.Select(l => l.SubCategory)
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                })
                .ToList();
        }

        // Grounded components grouped by tab, for the "/c/<tab>/<component>" staged autocomplete. Kept
        // out of the change-detection signature below (it can be large): it changes only when the
        // component tree or its selection changes, both of which ARE in the signature.
        var availableComponents = (conversationLog?.IncludedComponentEntries ?? Array.Empty<CatalogEntry>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Category) && !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                tab = g.Key,
                components = g.Select(e => e.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .ToList();

        // Cluster grounding state for the window's cluster selector and the "/cl/" autocomplete: whether
        // any cluster grounding is wired, the available clusters (name + I/O + description), and the
        // current selection (null = include everything).
        bool clustersWired = conversationLog?.HasClusterGrounding == true;
        var availableClusters = (conversationLog?.AvailableClusters ?? Array.Empty<ClusterEntry>())
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new
            {
                name = c.Name,
                description = c.Description,
                inputs = c.Inputs.Select(p => p.Name).ToList(),
                outputs = c.Outputs.Select(p => p.Name).ToList(),
            })
            .ToList();
        object? clusterSelection = null;
        if (conversationLog?.ClusterSelectionOrNull is { } csel)
        {
            clusterSelection = csel.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Tool grounding state for the Tools page + "/t/" autocomplete: whether any tools grounding is
        // wired, the tools currently on the canvas, and the current selection (null = include all).
        bool toolsWired = conversationLog?.HasToolsGrounding == true;
        var availableTools = (conversationLog?.AvailableToolNames ?? Array.Empty<string>())
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        object? toolsSelection = null;
        if (conversationLog?.ToolsSelectionOrNull is { } tsel)
        {
            toolsSelection = tsel.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Referenced Rhino geometry state (read-only page): the params on the canvas referencing
        // live Rhino geometry, shown when a canvas-state grounding is wired (without it the model
        // cannot see those params anyway).
        bool referencedGeometryWired = conversationLog?.HasCanvasStateGrounding == true;
        var availableReferencedGeometry = (conversationLog?.AvailableReferencedGeometry ?? Array.Empty<ReferencedGeometryInput>())
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => new { name = i.Name, type = i.TypeName })
            .ToList();

        // Python-function grounding state (read-only page): the available python functions.
        bool pythonWired = conversationLog?.HasPythonGrounding == true;
        var pythonFunctions = (conversationLog?.AvailablePythonFunctions ?? Array.Empty<PythonFunctionGrounding>())
            .Select(p => new { signature = p.Signature, docstring = p.Docstring })
            .ToList();

        // Document-units grounding state: whether a units grounding is wired, the live document units,
        // the current override (null = use the document units), and the unit choices for the dropdown.
        // The live document value is always present in the options so the dropdown can show it.
        bool unitsWired = conversationLog?.HasUnitsGrounding == true;
        string documentUnits = conversationLog?.DocumentUnits ?? string.Empty;
        string? unitsOverride = conversationLog?.UnitsOverrideOrNull;
        var unitOptions = UnitOptions
            .Concat(new[] { documentUnits, unitsOverride ?? string.Empty })
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Human-tool state: whether a Geometry Snapshot tool is wired, whether a transmitter has
        // generated geometry right now (the composer shows its geometry button only while both
        // hold), whether that press sends the snapshot as its own message or attaches it to the
        // prompt box for the human to caption (the tool's "Send With Default Message" toggle — off
        // also hides the message editor, which is dead text then), the tool's default message, and
        // the current override (null = use the default). The geometry scan is gated on the tool
        // being wired so idle documents pay nothing on the 0.15 s tick. An Add Image tool gates
        // image intake in the composer — without it, paste/drop/picker are fully disabled.
        bool snapshotWired = conversationLog?.HasGeometrySnapshotTool == true;
        bool snapshotGeometryPresent = snapshotWired
            && Generation.GeneratedGeometryScan.HasGeneratedGeometry(conversationLog?.OnPingDocument());
        bool snapshotSendsMessage = conversationLog?.GeometrySnapshotSendsMessage == true;
        string snapshotDefaultMessage = conversationLog?.GeometrySnapshotDefaultMessage ?? string.Empty;
        string? snapshotMessage = conversationLog?.SnapshotMessageOverrideOrNull;
        bool imageToolWired = conversationLog?.HasAddImageTool == true;

        // View-snapshot state: the same four fields, minus the armed condition. A view capture needs
        // nothing on the canvas and moves no camera, so wired is armed and there is no scan to gate.
        bool viewSnapshotWired = conversationLog?.HasViewSnapshotTool == true;
        bool viewSnapshotSendsMessage = conversationLog?.ViewSnapshotSendsMessage == true;
        string viewSnapshotDefaultMessage = conversationLog?.ViewSnapshotDefaultMessage ?? string.Empty;
        string? viewSnapshotMessage = conversationLog?.ViewSnapshotMessageOverrideOrNull;

        // Marker human tools — nothing to configure, each just lights a header button: an export
        // that writes this conversation to a transcript, and a door onto the session's signal trace.
        bool exportToolWired = conversationLog?.HasExportTool == true;
        bool signalTraceToolWired = conversationLog?.HasSignalTraceTool == true;

        // Cheap proxy for availableComponents in the signature (serializing the full list every tick
        // would churn); the tree/selection already trigger a push, this just catches a catalog resize.
        int componentCount = availableComponents.Sum(c => c.components.Count);

        string groundingSignature = JsonSerializer.Serialize(
            new { groundingWired, exposeSignatures, groundingTree, groundingSelection, componentCount, clustersWired, availableClusters, clusterSelection, toolsWired, availableTools, toolsSelection, referencedGeometryWired, availableReferencedGeometry, pythonWired, pythonFunctions, unitsWired, documentUnits, unitsOverride, unitOptions, snapshotWired, snapshotGeometryPresent, snapshotSendsMessage, snapshotDefaultMessage, snapshotMessage, viewSnapshotWired, viewSnapshotSendsMessage, viewSnapshotDefaultMessage, viewSnapshotMessage, imageToolWired, exportToolWired, signalTraceToolWired }, WriteOpts);

        if (_forcePush || connected != _lastConnected || busy != _lastBusy || ready != _lastReady
            || needsSetup != _lastNeedsSetup || status != _lastStatus || configuredJson != _lastConfigured
            || groundingSignature != _lastGroundingSignature)
        {
            _lastConnected = connected;
            _lastBusy = busy;
            _lastReady = ready;
            _lastNeedsSetup = needsSetup;
            _lastStatus = status;
            _lastConfigured = configuredJson;
            _lastGroundingSignature = groundingSignature;
            string state = JsonSerializer.Serialize(
                new { connected, busy, ready, needsSetup, status, configuredProviders, groundingWired, exposeSignatures, groundingTree, groundingSelection, availableComponents, clustersWired, availableClusters, clusterSelection, toolsWired, availableTools, toolsSelection, referencedGeometryWired, availableReferencedGeometry, pythonWired, pythonFunctions, unitsWired, documentUnits, unitsOverride, unitOptions, snapshotWired, snapshotGeometryPresent, snapshotSendsMessage, snapshotDefaultMessage, snapshotMessage, viewSnapshotWired, viewSnapshotSendsMessage, viewSnapshotDefaultMessage, viewSnapshotMessage, imageToolWired, exportToolWired, signalTraceToolWired }, WriteOpts);
            Exec($"window.physalia&&window.physalia.setState({state});");
        }

        // The forced post-switch push (above) is done; subsequent ticks resume change-detection.
        _forcePush = false;

        // Bundled presets for the "Add preset" page — pushed once and whenever the set changes.
        MaybePushPresets();

        // Switcher row: one circle per Chat on the canvas, pushed when the set/active changes.
        MaybePushChats();
    }

    // Pushes the bundled presets to the page, but only when the set actually changes — a cheap
    // signature (file names + last-write times) is compared first, so this is nearly free on the
    // 0.15 s tick. No description is offered: a Grasshopper file carries none that can be read
    // without instantiating every component in it, so the file name is the label.
    private void MaybePushPresets()
    {
        string dir = GetPresetsDir();
        List<string> paths = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.gh")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        string signature = string.Join(
            ";",
            paths.Select(p => $"{Path.GetFileName(p)}|{File.GetLastWriteTimeUtc(p).Ticks}"));
        if (signature == _lastPresetSignature)
        {
            return;
        }

        _lastPresetSignature = signature;

        var presets = paths
            .Select(p => new
            {
                file = Path.GetFileName(p),
                description = (string?)null,
            })
            .ToList();

        string json = JsonSerializer.Serialize(presets, WriteOpts);
        Exec($"window.physalia&&window.physalia.setPresets&&window.physalia.setPresets({json});");
    }

    // Pushes the switcher list — one entry per Chat on the canvas, in left-to-right canvas order —
    // marking which is active and which already has recorded history. Pushed only when the serialised
    // list changes (a Chat added/removed/moved, the active one switched, or a history appearing).
    private void MaybePushChats()
    {
        var list = EnumerateChats()
            .Select(cb => new
            {
                id = cb.InstanceGuid.ToString(),
                active = ReferenceEquals(cb, _component),
                hasHistory = ChatHasHistory(cb),
                emoji = cb.Emoji,
            })
            .ToList();

        string json = JsonSerializer.Serialize(list, WriteOpts);
        if (json == _lastChats)
        {
            return;
        }

        _lastChats = json;
        Exec($"window.physalia&&window.physalia.setChats&&window.physalia.setChats({json});");
    }

    // Every Chat on the canvas, ordered left-to-right then top-to-bottom by canvas position for a
    // stable, intuitive circle sequence. The viewed component is always included even when it is not
    // on a document yet (created by the widget, awaiting its provider-gated placement), so its circle
    // is present and selectable from the first tick.
    private List<Chat> EnumerateChats()
    {
        var result = new List<Chat>();

        // The switcher row lists every Chat in the file, so it looks inside harnesses too — a Chat
        // that has moved into one is still a chat the user can switch to, and after the move it is
        // the ONLY place a Chat lives.
        GH_Document? host = Harness.PhyDocuments.Host(_component) ?? Harness.PhyDocuments.ActiveHost();
        if (host is not null)
        {
            foreach (IGH_DocumentObject obj in Harness.PhyDocuments.ObjectsIncludingHarnesses(host))
            {
                if (obj is Chat cb)
                {
                    result.Add(cb);
                }
            }
        }

        result.Sort(static (a, b) =>
        {
            System.Drawing.PointF pa = a.Attributes?.Pivot ?? default;
            System.Drawing.PointF pb = b.Attributes?.Pivot ?? default;
            int cmp = pa.X.CompareTo(pb.X);
            return cmp != 0 ? cmp : pa.Y.CompareTo(pb.Y);
        });

        if (!result.Contains(_component))
        {
            result.Insert(0, _component);
        }

        return result;
    }

    // True when a Chat's wired ConversationLog holds a non-empty conversation — used to fill its circle.
    private static bool ChatHasHistory(Chat chat)
    {
        Conversation? convo = PromptPipelineView.FindConversationLog(chat, 0)?.ActiveConversation;
        return convo is { Messages.Count: > 0 };
    }

    // Switches the window to view a different Chat (from the switcher row, a double-click, or a
    // fallback after the viewed one is deleted). Resets the per-component change-detection caches so
    // the new component's history and state push fresh on the next tick rather than being suppressed
    // as "unchanged". No-op when already viewing it. Runs on the UI thread.
    public void SetActiveComponent(Chat component)
    {
        if (component is null || ReferenceEquals(component, _component))
        {
            return;
        }

        _component = component;
        ResetPushedState();
    }

    // Called when any Chat is removed from the document. If the viewed one was deleted, switch to
    // another Chat still on the canvas, or close the window when none remain. An unrelated removal
    // needs nothing here — the switcher row drops its circle on the next tick. Runs on the UI thread.
    public void OnComponentRemoved(Chat removed)
    {
        if (!ReferenceEquals(removed, _component))
        {
            return;
        }

        // Harnesses included: after a pipeline moves into one, that is the only place a Chat lives,
        // and without looking inside this would find no replacement and close the window.
        Chat? next = null;
        foreach (IGH_DocumentObject obj in Harness.PhyDocuments.ObjectsIncludingHarnesses(Harness.PhyDocuments.ActiveHost()))
        {
            if (obj is Chat cb && !ReferenceEquals(cb, removed))
            {
                next = cb;
                break;
            }
        }

        if (next is not null)
        {
            SetActiveComponent(next);
        }
        else
        {
            Close();
        }
    }

    // Resolves a switcher-circle click (?id=<InstanceGuid>) to its Chat and views it. Runs on the
    // UI thread (bridge dispatch).
    private void HandleSelectChat(Uri uri)
    {
        string id = GetQueryValue(uri.Query, "id");
        if (string.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid guid))
        {
            return;
        }

        foreach (Chat cb in EnumerateChats())
        {
            if (cb.InstanceGuid == guid)
            {
                SetActiveComponent(cb);
                return;
            }
        }
    }

    // Drops the per-component last-pushed caches so a freshly viewed Chat re-pushes its full
    // history/state next tick. The preset and chat-list signatures are global (not per viewed
    // component), so they are left intact.
    private void ResetPushedState()
    {
        _lastConversation = null;
        _lastStream = null;
        _lastConnected = null;
        _lastBusy = null;
        _lastReady = null;
        _lastNeedsSetup = null;
        _lastStatus = null;
        _lastConfigured = null;
        _lastGroundingSignature = null;
        _forcePush = true;
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

        if (_configuredProviders is not null && DateTime.UtcNow - _lastProviderProbeUtc < ProviderProbeInterval)
        {
            return;
        }

        _providerProbeInFlight = true;
        string configPath = GetApiKeyConfigPath();

        Task.Run(async () =>
        {
            IReadOnlyList<string>? configured;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                configured = await ProviderAvailability.ConfiguredProviderIdsAsync(configPath, ProbeClient, cts.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Keep the last known answer on failure; leaving it null before the first result means
                // setup never flashes during the very first probe (null is treated as "configured").
                configured = _configuredProviders;
            }

            Application.Instance.AsyncInvoke(() =>
            {
                _configuredProviders = configured;
                _lastProviderProbeUtc = DateTime.UtcNow;
                _providerProbeInFlight = false;
            });
        });
    }

    // True once the probe has found at least one configured chat-model provider (an LLM), ignoring
    // web-tool keys (Tavily/Jina). Gates both the first-run setup screen and component placement —
    // a web-tool key alone is not something to chat with.
    private bool HasLlmProvider() =>
        _configuredProviders is { } configured && configured.Any(id => !ToolProviderIds.Contains(id));

    // Puts a Chat inside a fresh Harness and drops the HARNESS on the host document, at the first
    // free spot right of the window.
    //
    // The harness is the plug-in's unit of work: a pipeline lives in its own document and the user's
    // canvas carries only the proxy. So a Chat is never placed bare — it goes inside a harness, ready
    // for the pipeline to be built around it.
    //
    // WHICH Chat goes in depends on whether this window is already driving a placed one. A window
    // opened from the widget holds a detached Chat belonging to no document: that one is consumed, so
    // the first placement adopts it instead of stranding it. Once it is placed, every further harness
    // gets a Chat of its own — taking the viewed one would leave the earlier harness with nothing to
    // drive its pipeline. The window follows the Chat it just made; the switcher row is the way back
    // to the others. Returns the new harness's document. Runs on the UI thread.
    private GH_Document DropHarness(GH_Canvas canvas, GH_Document host)
    {
        Chat chat = _component.OnPingDocument() is null ? _component : new Chat();
        if (chat.Attributes is null)
        {
            chat.CreateAttributes();
        }

        var harness = new Harness.HarnessComponent();
        harness.CreateAttributes();

        // The Chat goes in directly — no archive round-trip — so this window's binding to it holds.
        GH_Document inner = harness.EnsureInnerDocument();
        chat.Attributes!.Pivot = new System.Drawing.PointF(0f, 0f);
        inner.AddObject(chat, false);
        ComponentHelpers.ApplyNickNameDisplay(chat);

        PlaceHarness(canvas, host, harness);
        SetActiveComponent(chat);
        return inner;
    }

    // Drops a harness proxy on the document at the first free spot right of the window: the anchor
    // itself when nothing is there, else stepped down a row at a time until it clears what is already
    // on the canvas. Harnesses can be placed over and over, and none lands hidden under another.
    private void PlaceHarness(GH_Canvas canvas, GH_Document host, Harness.HarnessComponent harness)
    {
        // Anchor = a few px right of the window's right edge, level with its vertical centre.
        System.Drawing.PointF anchor = AnchorRightOfWindow(canvas);

        harness.Attributes!.Pivot = anchor;
        host.AddObject(harness, false);
        MoveTo(harness, anchor);

        for (int row = 0; row < MaxPlacementRows && Overlaps(host, harness); row++)
        {
            anchor.Y += harness.Attributes.Bounds.Height + PlacementGap;
            MoveTo(harness, anchor);
        }

        canvas.Refresh();
    }

    // Positions a just-added object so its bounds' left edge sits at the anchor and its vertical
    // centre lines up with it (Pivot is interior to the bounds, not a corner). Lays the attributes
    // out first and again afterwards, because GH does so lazily and a fresh object's Bounds is
    // otherwise stale — which would make the nudge below a no-op.
    private static void MoveTo(IGH_DocumentObject obj, System.Drawing.PointF anchor)
    {
        obj.Attributes!.ExpireLayout();
        obj.Attributes.PerformLayout();

        System.Drawing.RectangleF bounds = obj.Attributes.Bounds;
        obj.Attributes.Pivot = new System.Drawing.PointF(
            obj.Attributes.Pivot.X + (anchor.X - bounds.Left),
            obj.Attributes.Pivot.Y + (anchor.Y - (bounds.Top + (bounds.Height / 2f))));

        obj.Attributes.ExpireLayout();
        obj.Attributes.PerformLayout();
    }

    // True when a freshly placed object's bounds — inflated by the placement gap, so a "free" spot is
    // actually clear rather than merely touching — intersect any other object on the document. Groups
    // are skipped: they are containers drawn behind their members, so treating one as an obstacle
    // would push a harness clear of a region that is mostly empty canvas.
    private static bool Overlaps(GH_Document doc, IGH_DocumentObject placed)
    {
        System.Drawing.RectangleF bounds = placed.Attributes!.Bounds;
        bounds.Inflate(PlacementGap, PlacementGap);

        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (!ReferenceEquals(obj, placed)
                && obj is not Grasshopper.Kernel.Special.GH_Group
                && obj.Attributes is { } attributes
                && attributes.Bounds.IntersectsWith(bounds))
            {
                return true;
            }
        }

        return false;
    }

    // The host document to place onto, creating one when nothing is open. Resolved through
    // PhyDocuments so a harness placed while the user is INSIDE another harness lands on their
    // canvas rather than nesting. Null when there is no canvas at all.
    private static GH_Document? HostForPlacement(GH_Canvas? canvas)
    {
        if (canvas is null)
        {
            return null;
        }

        GH_Document? doc = canvas.Document ?? CreateActiveDocument(canvas);
        return doc is null ? null : Harness.PhyDocuments.Host(doc) ?? doc;
    }

    // Creates a fresh, empty document and makes it the canvas's active one, so a chat started with
    // no file open has a real canvas to drop the harness onto. Returns null if the document server
    // is unavailable. Runs on the UI thread, so touching the canvas/document is safe.
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

    // Drops an empty harness — a Chat and nothing else — onto the canvas, from the connect screen's
    // "Place empty harness" option or the header menu's "Add empty harness". Nothing is placed until
    // the user asks: opening the window from the widget no longer litters the canvas on its own.
    //
    // Repeatable. A harness is a self-contained pipeline that exchanges no dataflow with anything
    // else, so a document can hold as many as the user wants — one per line of work. Each gets its
    // own Chat and the window switches to it. Runs on the UI thread (bridge dispatch), so editing the
    // document and forcing a solve is safe.
    private void HandlePlaceEmptyHarness()
    {
        GH_Canvas? canvas = Instances.ActiveCanvas;
        if (HostForPlacement(canvas) is not { } host)
        {
            return;
        }

        DropHarness(canvas!, host).NewSolution(false);
        canvas!.Refresh();
    }

    // Clears every Physalia lifecycle component in the open document back to Empty — dropping
    // latched signals and wiping recorded conversations / histories — then recomputes once so the
    // cleared state propagates and the canvas redraws. The whole-document analogue of a single
    // component's right-click "Clear" menu item. Runs on the UI thread (bridge dispatch), so
    // editing the document and forcing a solve is safe (same as HandleConnectConversationLog).
    private void HandleClearAll()
    {
        // "All" spans the whole file, so the sweep descends into every harness — that is where the
        // pipelines actually live — and each document that had something cleared is re-solved.
        GH_Document? host = Harness.PhyDocuments.Host(_component) ?? Harness.PhyDocuments.ActiveHost();
        if (host is null)
        {
            return;
        }

        var touched = new HashSet<GH_Document>();
        foreach (IGH_DocumentObject obj in Harness.PhyDocuments.ObjectsIncludingHarnesses(host))
        {
            if (obj is StatefulComponentBase stateful)
            {
                stateful.ClearLifecycle();
                if (stateful.OnPingDocument() is { } owner)
                {
                    touched.Add(owner);
                }
            }
        }

        if (touched.Count > 0)
        {
            foreach (GH_Document document in touched)
            {
                document.NewSolution(true); // expire all + recompute so cleared wires/state propagate
            }

            Instances.ActiveCanvas?.Refresh();
        }
    }

    // Adds a bundled preset (a .gh from Files/PRESETS) to the canvas as a new harness.
    //
    // A preset IS a harness's document, so there is nothing to place, splice or re-wire: the file is
    // read and becomes the new harness's contents wholesale, and the window re-points at the Chat
    // inside it. Existing harnesses are left running untouched — presets accumulate. The requested
    // file name is validated against the presets directory (only a bare file name from the enumerated
    // set is honoured — no path traversal). Runs on the UI thread (bridge dispatch), so editing the
    // document is safe here — outside any active solve.
    private void HandlePlacePreset(Uri uri)
    {
        string file = Path.GetFileName(GetQueryValue(uri.Query, "file"));
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        string path = Path.Combine(GetPresetsDir(), file);
        if (!File.Exists(path))
        {
            Rhino.RhinoApp.WriteLine($"[Physalia] Preset not found: {file}");
            return;
        }

        try
        {
            GH_Document? contents = Harness.HarnessComponent.ReadDocumentFile(path);
            if (contents is null)
            {
                Rhino.RhinoApp.WriteLine($"[Physalia] Preset could not be read: {file}");
                return;
            }

            // The window has to land on a Chat, and a preset that carries none would leave it with
            // nothing to switch to. Refuse rather than half-apply.
            if (contents.Objects.OfType<Chat>().FirstOrDefault() is not { } presetChat)
            {
                Rhino.RhinoApp.WriteLine(
                    $"[Physalia] Preset '{file}' contains no Chat component, so there would be nothing to "
                    + "drive it. Save a preset from inside a harness that has one.");
                return;
            }

            // Always a NEW harness, never a swap of the one the window happens to be driving: a
            // preset is a pipeline to add, not a replacement for whatever is already running.
            if (!DropPresetHarness(contents))
            {
                Rhino.RhinoApp.WriteLine("[Physalia] No Grasshopper canvas to add the preset to.");
                return;
            }

            SetActiveComponent(presetChat);
            Instances.ActiveCanvas?.Refresh();
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"[Physalia] Preset could not be added: {ex.Message}");
        }
    }

    // Drops a harness carrying the preset onto the canvas, at the first free spot right of the
    // window — beside whatever is already there, never on top of it. The preset arrives whole: it IS
    // a harness's document, so it needs no Chat of our own making (the caller has already refused a
    // preset that carries none). Returns false when there is no canvas to place onto.
    private bool DropPresetHarness(GH_Document contents)
    {
        GH_Canvas? canvas = Instances.ActiveCanvas;
        if (HostForPlacement(canvas) is not { } host)
        {
            return false;
        }

        Harness.HarnessComponent harness = Harness.HarnessComponent.CreateWith(contents);
        PlaceHarness(canvas!, host, harness);
        harness.ExpireSolution(true);
        return true;
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
    // GetWindowLongPtr index for a window's owner handle.
    private const int GWLP_HWNDPARENT = -8;

    // Re-parents this window onto the Grasshopper editor as an owned (non-topmost) window: it stays
    // above the GH/Rhino canvas, minimises and restores with it, and slips behind any other app the
    // user switches to — instead of pinning above everything as Topmost did. No-op if either handle
    // isn't available yet, in which case the Eto Owner (Rhino main window) remains the fallback.
    private void OwnToGrasshopperEditor()
    {
        IntPtr child = NativeHandle;
        IntPtr owner = _ghEditor?.Handle ?? IntPtr.Zero;
        if (child == IntPtr.Zero || owner == IntPtr.Zero)
        {
            return;
        }

        SetWindowLongPtr(child, GWLP_HWNDPARENT, owner);
    }

    // SetWindowPos flags: keep the current size and z-order and don't steal focus — we only move.
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    // Centres this window over the Grasshopper editor window (device pixels, via the native rects),
    // so it always opens on the monitor the canvas is on rather than wherever the Rhino main window
    // (the Eto owner) happens to be — on a multi-monitor setup the two can be on different screens,
    // which previously landed the window off-canvas and threw off the anchored component placement.
    // No-op if either handle or rect is unavailable, leaving Eto's default placement.
    private void PositionOverGrasshopperEditor()
    {
        IntPtr child = NativeHandle;
        IntPtr editor = _ghEditor?.Handle ?? IntPtr.Zero;
        if (child == IntPtr.Zero || editor == IntPtr.Zero)
        {
            return;
        }

        if (!GetWindowRect(editor, out RECT er) || !GetWindowRect(child, out RECT cr))
        {
            return;
        }

        int childWidth = cr.Right - cr.Left;
        int childHeight = cr.Bottom - cr.Top;
        int x = er.Left + (((er.Right - er.Left) - childWidth) / 2);
        int y = er.Top + (((er.Bottom - er.Top) - childHeight) / 2);

        SetWindowPos(child, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    // x64 only (Rhino 8 is 64-bit); the SetWindowLongPtr export exists on 64-bit Windows.
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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

    // Directory of bundled preset harnesses shipped beside the plug-in (Files/PRESETS). A preset is
    // an ordinary Grasshopper file holding one harness's worth of pipeline — which is exactly what
    // saving from inside a harness produces, so authoring one needs no export step.
    private static string GetPresetsDir()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return assemblyDir is null
            ? "PRESETS"
            : Path.Combine(assemblyDir, "Files", "PRESETS");
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
                tools.Count > 0 ? tools : null,
                message.IsFeedback));
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
        IReadOnlyList<UiTool>? Tools,
        bool Feedback);

    private sealed record SubmitImage(string Base64, string MediaType, string Filename);

    private sealed record SubmitMessage(string Text, List<SubmitImage>? Images);

    // Grounding selection pushed from the window: all=true clears to include-everything; otherwise
    // leaves is a list of [category, subCategory] pairs to include.
    private sealed record GroundingSelectionPayload(bool All, List<List<string>>? Leaves);

    // Cluster selection pushed from the window: all=true clears to include-everything; otherwise
    // names is the list of cluster names to include.
    private sealed record ClusterSelectionPayload(bool All, List<string>? Names);

    // Tools selection pushed from the window: all=true clears to include-every-present-tool; otherwise
    // names is the list of tool names to advertise to the model.
    private sealed record ToolsSelectionPayload(bool All, List<string>? Names);

    // Document-units override pushed from the window: reset=true clears to the live document units;
    // otherwise units is the override text handed to the model.
    private sealed record UnitsOverridePayload(bool Reset, string? Units);

    // Geometry-snapshot message override pushed from the window: reset=true clears to the wired
    // grounding's default message; otherwise message is the text sent alongside the snapshot image.
    private sealed record SnapshotMessagePayload(bool Reset, string? Message);
}
