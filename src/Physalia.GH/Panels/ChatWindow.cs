// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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
using Physalia.Core.Common;
using Physalia.Core.Config;
using Physalia.GH.Config;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Clusters;
using Physalia.Core.Grounding.Components;
using Physalia.Core.Grounding.Tools;
using Physalia.Core.Mcp;
using Physalia.Core.Pdf;
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

    // Ticks (at 0.15 s each — so about five seconds) the push gate waits for DocumentLoaded before
    // giving up and assuming the page is up. Purely a backstop; the event is the real signal.
    private const int TicksBeforePageAssumedReady = 33;

    // Switcher-row id (and harness key) of the Home entry. Never collides with a real one: ids are
    // InstanceGuids and harness keys are either a guid or empty.
    private const string HomeId = "home";

    // The two snapshot kinds a submit payload can declare, marking a capture that went out to the
    // image editor in send mode and came back marked up. Shared with the page (bridge.ts), which
    // receives the kind on markUpSnapshot and hands the same string back on the way in.
    private const string SnapshotKindGeometry = "geometry-snapshot";
    private const string SnapshotKindView = "view-snapshot";

    // A PDF arriving by drag-and-drop. It rides the same envelope as a submit, because drag-and-drop
    // is the one intake path that cannot hand us a real path — the DOM File API withholds it — so
    // the bytes come over and are spooled to temp. The file PICKER, which is the path for anything
    // large, opens host-side and never moves a byte across the bridge.
    private const string PdfDropKind = "pdf-drop";


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

    // True while the window shows HOME — the placement and provider-setup screen — instead of a
    // Chat's conversation. Home is deliberately NOT a Chat: it is always offered, always first in
    // the switcher row, and it survives every harness in the file being deleted. It is also what
    // the canvas widget focuses, so the widget is a way back to placement rather than a jump into
    // whichever conversation happened to be found first.
    private bool _home;

    // Component-icon PNGs (as data URIs) for the feedback-turn badges, keyed by instance guid.
    // Per window rather than static: a guid is only unique within a document, and the icon is
    // resolved through the viewed Chat's document.
    private readonly Dictionary<Guid, string?> _sourceIcons = new();

    private readonly WebView _webView;
    private readonly UITimer _timer;

    // "LoadUi has been called" — guards against loading twice, nothing more.
    private bool _loaded;

    // "The page is up and window.physalia is installed" — the gate every push waits on. Distinct
    // from _loaded: the bundle takes a moment to extract and parse, and anything pushed in between
    // is swallowed by the window.physalia&& guard in the pushed script.
    private bool _pageReady;
    private int _ticksAwaitingPage;

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

    // Last seen (write time, length) of MCP_SERVERS.YAML, so the MCP page refreshes when the file
    // changes — from this window, from another one, or from the user editing it by hand — without
    // reading it on every 0.15 s tick.
    private string? _lastMcpSignature;
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
    private IReadOnlyList<UiProviderStatus> _providerStatuses = Array.Empty<UiProviderStatus>();
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
        _webView.DocumentLoaded += OnDocumentLoaded;
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
                HandleSaveProvider(uri);
                break;
            case "detect":
                HandleDetect(uri);
                break;
            case "connect":
                HandleConnect(uri);
                break;
            case "disconnect":
                HandleDisconnect(uri);
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
            case "addpdf":
                HandleAddPdf();
                break;
            case "removepdf":
                HandleRemovePdf(uri);
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
            case "marksnapshot":
                // The geometry button in send mode with an Image Mark Up tool wired: capture the
                // snapshot and hand it to the window's image editor rather than sending it. What
                // comes back (or nothing, if the human cancels) arrives as a marked-snapshot submit.
                HandleMarkSnapshot(geometry: true);
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
            case "markviewsnapshot":
                // The view button in send mode with an Image Mark Up tool wired — the view twin of
                // marksnapshot: capture, then hand it to the image editor instead of sending it.
                HandleMarkSnapshot(geometry: false);
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
            case "savemcpserver":
                // The MCP page's save button: write one entry into MCP_SERVERS.YAML.
                HandleSaveMcpServer(uri);
                break;
            case "deletemcpserver":
                HandleDeleteMcpServer(uri);
                break;
            case "testmcpcommand":
                // The automatic page's test button: parse a pasted CLI command and connect to what
                // it describes, writing nothing.
                HandleMcpCommand(uri, save: false);
                break;
            case "savemcpcommand":
                // The automatic page's commit button: parse, write the entry, then connect.
                HandleMcpCommand(uri, save: true);
                break;
            case "testmcpserver":
                // The MCP page's test button: connect to the entry as currently typed, writing
                // nothing. Separate from the save verb because there is no file to read back.
                HandleTestMcpServer(uri);
                break;
            case "signinmcpserver":
                // Connect to a configured server now, which is what runs a remote one's OAuth
                // sign-in — so the browser handshake happens during setup rather than on the first
                // solve of a node the user has not placed yet.
                BeginMcpSignIn(GetQueryValue(uri.Query, "name"));
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

    // Captures a snapshot and hands it to the page's image editor instead of sending it — the send-mode
    // path taken when an Image Mark Up tool is wired. Nothing is minted here: the human draws on the
    // capture and the marked-up image comes back as a submit payload carrying its snapshot kind (see
    // SubmitJsonPayload), or does not come back at all when they cancel. `geometry` picks which
    // snapshot tool's capture to take; each capture helper already refuses when its tool is unwired.
    private void HandleMarkSnapshot(bool geometry)
    {
        byte[]? png;
        bool captured = geometry
            ? _component.TryCaptureGeneratedGeometryPng(out png)
            : _component.TryCaptureViewPng(out png);
        if (!captured || png is null)
        {
            return;
        }


        string json = JsonSerializer.Serialize(
            new UiImage(Convert.ToBase64String(png), "image/png"), WriteOpts);
        string kind = geometry ? SnapshotKindGeometry : SnapshotKindView;
        Exec($"window.physalia&&window.physalia.markUpSnapshot&&window.physalia.markUpSnapshot({json},'{kind}');");
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

    // Stores one provider's endpoint and key in the encrypted credential store, then forces a fresh
    // provider probe so the setup state clears immediately, and reports the outcome back to the
    // page. Neither value is ever logged.
    private void HandleSaveProvider(Uri uri)
    {
        string provider = GetQueryValue(uri.Query, "provider");
        string key = GetQueryValue(uri.Query, "key");
        string url = GetQueryValue(uri.Query, "url");

        ProviderInfo? info = ProviderCatalog.Find(provider);
        if (info is null || info.Auth != ProviderAuth.Credential)
        {
            PushSetupResult(provider, false, "Unknown provider.");
            return;
        }

        // "other" and a local endpoint are legitimately key-less, so an endpoint on its own is a
        // complete configuration. What is never useful is an entry carrying neither.
        if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(url))
        {
            PushSetupResult(provider, false, "Enter an API key (and an endpoint, if this provider needs one).");
            return;
        }

        if (info.Kind != ProviderKind.Tool && string.IsNullOrWhiteSpace(url) && info.DefaultBaseUrl.Length == 0)
        {
            PushSetupResult(provider, false, "This provider needs an API URL — there is no default to fall back on.");
            return;
        }

        try
        {
            PhyCredentials.Store.Save(new ModelApi(info.Id, url.Trim(), key.Trim()));

            // Typing a key and pressing Save IS the opt-in — there is no second confirmation to ask
            // for. Only a credential Physalia merely FOUND (an environment variable) needs one.
            PhyCredentials.Activation.Activate(info.Id);
        }
        catch (Exception ex)
        {
            PushSetupResult(provider, false, $"Could not save: {ex.Message}");
            return;
        }

        // Drop the cached probe so the next tick re-resolves; the now-present credential resolves on
        // the first (synchronous) check, clearing the setup state without waiting for the interval.
        PhyCredentials.Invalidate();
        _configuredProviders = null;
        _lastProviderProbeUtc = DateTime.MinValue;

        string where = PhyCredentials.Store.IsEncrypted
            ? $"Saved, {PhyCredentials.Store.Protection}."
            : $"Saved ({PhyCredentials.Store.Protection}).";
        PushSetupResult(provider, true, where + " You're all set.");
    }

    // Runs the availability probe for one CLI / local provider — the Detect button. Nothing is
    // stored: what the button really does is force the check the window otherwise runs on a timer,
    // so the user gets an answer at the moment they ask instead of waiting for the next tick.
    private async void HandleDetect(Uri uri)
    {
        string provider = GetQueryValue(uri.Query, "provider");

        ProviderInfo? info = ProviderCatalog.Find(provider);
        if (info is null || info.Auth != ProviderAuth.Detected)
        {
            PushSetupResult(provider, false, "Unknown provider.");
            return;
        }

        bool found;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            found = await ProviderAvailability.DetectAsync(info.Id, ProbeClient, cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PushSetupResult(provider, false, $"Could not run the check: {ex.Message}");
            return;
        }

        if (!found)
        {
            PushSetupResult(provider, false, info.Id == "local-llm"
                ? "No local server answered at http://127.0.0.1:8080. Start llama-server and try again."
                : $"{info.Label} was not found on your PATH. Finish the install above, open a NEW terminal to pick up the change, then try again.");
            return;
        }

        _configuredProviders = null;
        _lastProviderProbeUtc = DateTime.MinValue;

        // Found, not connected. The page now offers a Connect button; adopting it here would be the
        // old behaviour, where having a CLI installed for some other purpose silently enrolled it.
        PushSetupResult(provider, true, $"{info.Label} found. Press Connect to use it in Physalia.");
    }

    // Opts a provider in. This is the only thing that makes an available provider usable: a key in
    // the environment, or a CLI on PATH, says a provider COULD be used — never that the user wants
    // this plug-in spending that quota.
    private void HandleConnect(Uri uri)
    {
        string provider = GetQueryValue(uri.Query, "provider");

        ProviderInfo? info = ProviderCatalog.Find(provider);
        if (info is null)
        {
            PushSetupResult(provider, false, "Unknown provider.");
            return;
        }

        try
        {
            PhyCredentials.Activation.Activate(info.Id);
        }
        catch (Exception ex)
        {
            PushSetupResult(provider, false, $"Could not connect: {ex.Message}");
            return;
        }

        PhyCredentials.Invalidate();
        _configuredProviders = null;
        _lastProviderProbeUtc = DateTime.MinValue;

        PushSetupResult(provider, true, $"{info.Label} connected.");
    }

    // Opts a provider back out, and forgets any credential Physalia was storing for it. Deactivating
    // while keeping the key would leave a secret on disk that nothing uses and nothing shows — this
    // is also the "remove my key" affordance, which is why it is worded as a disconnect.
    private void HandleDisconnect(Uri uri)
    {
        string provider = GetQueryValue(uri.Query, "provider");

        ProviderInfo? info = ProviderCatalog.Find(provider);
        if (info is null)
        {
            PushSetupResult(provider, false, "Unknown provider.");
            return;
        }

        try
        {
            PhyCredentials.Activation.Deactivate(info.Id);
            PhyCredentials.Store.Remove(info.Id);
        }
        catch (Exception ex)
        {
            PushSetupResult(provider, false, $"Could not disconnect: {ex.Message}");
            return;
        }

        PhyCredentials.Invalidate();
        _configuredProviders = null;
        _lastProviderProbeUtc = DateTime.MinValue;

        PushSetupResult(provider, true, $"{info.Label} disconnected.");
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
        if (_home)
        {
            // Home drives no pipeline; _component is whichever Chat was last viewed, so submitting
            // here would post into a conversation the user cannot see. The composer is already inert
            // on Home (its gate keys on `connected`) — this is the belt to that pair of braces.
            return;
        }

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

    // Parses a {text, images[], kind} JSON payload (from the postMessage channel, or the pull
    // fallback) into interleaved content blocks (text first, then images) and submits it.
    private void SubmitJsonPayload(string raw)
    {
        SubmitMessage? message = ParseSubmitMessage(raw);
        if (message is null)
        {
            return;
        }

        // A marked-up snapshot returning from the image editor in send mode: not a prompt at all, so
        // it takes its own path — the wired tool's message rides it, not anything the page typed.
        if (message.Kind is SnapshotKindGeometry or SnapshotKindView)
        {
            SubmitMarkedSnapshot(message);
            return;
        }

        // A dropped PDF is an attachment, not a prompt: it registers and waits for the next send.
        if (message.Kind == PdfDropKind)
        {
            ReceiveDroppedPdfs(message);
            return;
        }


        string msgText = NormalizeRefs(message.Text ?? string.Empty);
        IReadOnlyList<SubmitImage> images = message.Images ?? (IReadOnlyList<SubmitImage>)Array.Empty<SubmitImage>();

        // Announce any PDFs attached since the last send, by PREPENDING their descriptor to the
        // text rather than adding a block of its own. On the text it becomes the signal's payload
        // and flows through WithPayloadText like any other prompt, which also means a PDF sent with
        // no typed message still produces a turn with something in it.
        msgText = PrependPdfDescriptor(msgText);

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

    // Sends a snapshot that came back from the image editor with the human's mark-up flattened into
    // it, as its own user message carrying the wired tool's message — the send-mode tail of the
    // geometry/view button when an Image Mark Up tool is wired (HandleMarkSnapshot is its head).
    //
    // The grant is checked here as well as at capture time, for the same reason the prompt path
    // re-checks its images: the wire can change between the capture and the confirm, and no image may
    // reach the model without the tool that admitted it still being wired. Exactly one image is
    // expected — a snapshot is one capture — so anything else is a payload we did not send.
    private void SubmitMarkedSnapshot(SubmitMessage message)
    {
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(_component, 0);
        if (conversationLog?.HasImageMarkUpTool != true)
        {
            return;
        }

        if (message.Images is not { Count: 1 } images || string.IsNullOrEmpty(images[0].Base64))
        {
            return;
        }

        byte[] png;
        try
        {
            png = Convert.FromBase64String(images[0].Base64);
        }
        catch
        {
            return;
        }

        _component.SendMarkedSnapshotFromWindow(png, message.Kind == SnapshotKindGeometry);
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
        if (!_pageReady)
        {
            // Safety net for a backend that never raises DocumentLoaded: rather than leave the
            // window permanently dead, assume the page is up after the grace period. Worst case is
            // the pre-fix behaviour — a swallowed first push — not a frozen window.
            if (++_ticksAwaitingPage < TicksBeforePageAssumedReady)
            {
                return;
            }

            MarkPageReady();
        }

        // A Chat can stop being part of the file without ever being removed from a document: deleting
        // the HARNESS takes its whole sub-document out wholesale, leaving the Chat inside perfectly
        // intact and still reporting that inner document from OnPingDocument(). No
        // Chat.RemovedFromDocument fires, so nothing told the window, which sat frozen on the
        // conversation of a pipeline no longer in the file. Falling back to Home covers that and every
        // other way the viewed Chat can be orphaned, without needing a hook per case.
        if (!_home && !IsViewedChatLive())
        {
            ShowHome();
        }

        // Home reads no pipeline at all — it is the entry screen, not a conversation. Leaving the
        // Conversation Log null here is what drives that: every field below is already written to
        // cope with an unwired Chat, so the whole page falls back to the connect screen and the
        // composer greys out (its inert gate keys on `connected`).
        ConversationLog? conversationLog = _home ? null : PromptPipelineView.FindConversationLog(_component, 0);
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
        IReadOnlyList<UiProviderStatus> providerStatuses = _providerStatuses;
        // First-run setup needs an LLM provider, not merely a web-tool key (Tavily/Jina): show setup
        // once the probe has landed and no chat-model provider is configured.
        bool needsSetup = _configuredProviders is not null && !HasLlmProvider();

        // Pipeline-wiring readiness: chat needs ConversationLog -> [compactor…] -> LLM Call -> Model. Shown
        // as a hint once a provider exists but the graph isn't fully wired.
        bool ready = !_home && PromptPipelineView.IsPipelineReady(_component, 0);
        string status = needsSetup ? "Setup mode"
            : busy ? "Working…"
            : _home ? "Choose an option above to begin."
            : conversationLog is null ? "Wire a Conversation Log to this Chat to begin."
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

        // Token counter: the count from the Token Estimator that a wired Token Count human tool is
        // linked to, or null (counter hidden) when no such tool is wired, none of them is linked to
        // an estimator, or the estimator has no count yet. Counting and showing the count are two
        // components now — an estimator on its own puts nothing on screen.
        int? tokenCount = conversationLog?.LinkedTokenCountOrNull;
        if (_forcePush || tokenCount != _lastTokenCount)
        {
            _lastTokenCount = tokenCount;
            Exec($"window.physalia&&window.physalia.setTokenCount({JsonSerializer.Serialize(tokenCount)});");
        }

        // Serialised form of the id list, used both as the change signature and the wire payload.
        string configuredJson = JsonSerializer.Serialize(configuredProviders, WriteOpts)
            + JsonSerializer.Serialize(providerStatuses, WriteOpts);

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

        // Another marker tool, but this one changes what the two snapshot buttons do rather than adding
        // one of its own: with it wired, every capture detours through the window's image editor, and
        // each image already in the prompt box grows an edit button.
        bool markUpToolWired = conversationLog?.HasImageMarkUpTool == true;

        // Listed among the human tools, but its number is pushed on its own line above (the count
        // changes far more often than the wiring does, and must not drag the whole grounding
        // payload across with it every time it ticks).
        bool tokenCountToolWired = conversationLog?.HasTokenCountTool == true;

        // PDF intake. Deliberately a separate grant from imageToolWired: a PDF is not an image, it
        // does not travel as one, and it never reaches the image editor. `pendingPdfs` is what the
        // composer draws as chips — the files picked but not yet announced in a turn. Bytes never
        // cross the bridge for a PDF, only these summaries: a drawing set can be hundreds of
        // megabytes and base64 through postMessage is not a place to put it.
        bool pdfToolWired = conversationLog?.HasReadPdfTool == true;
        var pendingPdfs = (PdfSessionFor(conversationLog)?.Pending() ?? (IReadOnlyList<PdfDescriptor>)Array.Empty<PdfDescriptor>())
            .Select(d => new UiPdf(d.Alias, d.DisplayName, d.PageCount))
            .ToList();


        // Cheap proxy for availableComponents in the signature (serializing the full list every tick
        // would churn); the tree/selection already trigger a push, this just catches a catalog resize.
        int componentCount = availableComponents.Sum(c => c.components.Count);

        string groundingSignature = JsonSerializer.Serialize(
            new { groundingWired, exposeSignatures, groundingTree, groundingSelection, componentCount, clustersWired, availableClusters, clusterSelection, toolsWired, availableTools, toolsSelection, referencedGeometryWired, availableReferencedGeometry, pythonWired, pythonFunctions, unitsWired, documentUnits, unitsOverride, unitOptions, snapshotWired, snapshotGeometryPresent, snapshotSendsMessage, snapshotDefaultMessage, snapshotMessage, viewSnapshotWired, viewSnapshotSendsMessage, viewSnapshotDefaultMessage, viewSnapshotMessage, imageToolWired, exportToolWired, signalTraceToolWired, markUpToolWired, tokenCountToolWired, pdfToolWired, pendingPdfs }, WriteOpts);


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
            // `home` rides along so the page can tell the two connect-screen cases apart: on Home the
            // placement options are the point, while a Chat merely awaiting its Conversation Log
            // (an empty harness) shows the logo alone — offering to place another harness there
            // would answer a question the user did not ask.
            bool home = _home;
            string state = JsonSerializer.Serialize(
                new { connected, busy, ready, needsSetup, home, status, configuredProviders, providerStatuses, groundingWired, exposeSignatures, groundingTree, groundingSelection, availableComponents, clustersWired, availableClusters, clusterSelection, toolsWired, availableTools, toolsSelection, referencedGeometryWired, availableReferencedGeometry, pythonWired, pythonFunctions, unitsWired, documentUnits, unitsOverride, unitOptions, snapshotWired, snapshotGeometryPresent, snapshotSendsMessage, snapshotDefaultMessage, snapshotMessage, viewSnapshotWired, viewSnapshotSendsMessage, viewSnapshotDefaultMessage, viewSnapshotMessage, imageToolWired, exportToolWired, signalTraceToolWired, markUpToolWired, tokenCountToolWired, pdfToolWired, pendingPdfs }, WriteOpts);

            Exec($"window.physalia&&window.physalia.setState({state});");
        }

        // The forced post-switch push (above) is done; subsequent ticks resume change-detection.
        _forcePush = false;

        // Bundled presets for the "Add preset" page — pushed once and whenever the set changes.
        MaybePushPresets();

        // The MCP server list for the "Configure MCP connections" page — same deal, keyed on the
        // config file's own write time so a hand edit shows up too.
        MaybePushMcpServers();

        // Switcher row: one circle per Chat on the canvas, pushed when the set/active changes.
        MaybePushChats();
    }

    // Pushes the preset library to the page, but only when the set actually changes — a cheap
    // signature (relative paths + last-write times) is compared first, so this is nearly free on the
    // 0.15 s tick. That also means a harness saved as a preset shows up in the gallery within a tick,
    // with no refresh action needed.
    //
    // Descriptions are read only on the far side of that check, because each one opens the preset's
    // archive: fine once when the library changes, absurd several times a second.
    private void MaybePushPresets()
    {
        IReadOnlyList<Harness.PresetEntry> entries = Harness.PresetLibrary.Enumerate();

        string signature = string.Join(";", entries.Select(e => $"{e.RelativePath}|{e.WriteTicks}"));
        if (signature == _lastPresetSignature)
        {
            return;
        }

        _lastPresetSignature = signature;

        var presets = entries
            .Select(e => new
            {
                // The wire value is the library-relative path; the page hands it back verbatim and
                // PresetLibrary.Resolve matches it against the library rather than composing a path.
                file = e.RelativePath,
                folder = e.Folder,
                name = Path.GetFileNameWithoutExtension(e.FileName),

                // The text of the Harness Notes panel inside the preset, read straight out of the
                // archive — the only description a .gh can carry.
                description = Harness.PresetLibrary.ReadDescription(
                    Path.Combine(Harness.PresetLibrary.RootDir, e.Folder, e.FileName)),
            })
            .ToList();

        string json = JsonSerializer.Serialize(presets, WriteOpts);
        Exec($"window.physalia&&window.physalia.setPresets&&window.physalia.setPresets({json});");
    }

    // Pushes the configured MCP servers to the page for the "Configure MCP connections" screen.
    //
    // Read only when MCP_SERVERS.YAML actually changes (write time + length), like the preset
    // library below — reading a file several times a second on the tick would be absurd, and this way
    // a hand edit, or a save from another window, shows up on its own within a tick.
    //
    // Values go over UNEXPANDED (ReadRaw): the page is an editor, so it must show and hand back
    // "${GITHUB_TOKEN}" rather than the resolved token — otherwise the next save would write the
    // secret into the store that the reference existed to keep it out of.
    private void MaybePushMcpServers()
    {
        string path = McpServer.Store.FilePath;

        string signature;
        try
        {
            var info = new FileInfo(path);
            signature = info.Exists ? $"{info.LastWriteTimeUtc.Ticks}|{info.Length}" : "none";
        }
        catch (IOException)
        {
            signature = "unreadable";
        }

        if (signature == _lastMcpSignature)
        {
            return;
        }

        _lastMcpSignature = signature;

        var servers = McpServer.Store.ReadRaw()
            .Select(d => new
            {
                name = d.Name,
                transport = d.IsRemote ? "remote" : "local",
                command = d.Command ?? string.Empty,
                args = d.Arguments,
                cwd = d.WorkingDirectory ?? string.Empty,
                env = d.Environment.Select(pair => new[] { pair.Key, pair.Value }).ToList(),
                url = d.Url ?? string.Empty,
                headers = d.Headers.Select(pair => new[] { pair.Key, pair.Value }).ToList(),
                scope = d.Scope ?? string.Empty,
                runnable = d.IsRunnable,
            })
            .ToList();

        string json = JsonSerializer.Serialize(
            // Nothing makes the page read-only any more: the store is ours, in one shape, and the
            // JSON-form refusal existed only because that shape was a config file shared with
            // another host that we must not rewrite.
            new { servers, readOnlyReason = (string?)null },
            WriteOpts);

        Exec($"window.physalia&&window.physalia.setMcpServers&&window.physalia.setMcpServers({json});");
    }

    // Writes one server entry into MCP_SERVERS.YAML, creating the file if it is not there yet.
    private void HandleSaveMcpServer(Uri uri)
    {
        string raw = GetQueryValue(uri.Query, "entry");
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        McpServerPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<McpServerPayload>(raw, ReadOpts);
        }
        catch (JsonException)
        {
            PushMcpResult(false, "Physalia could not read that server definition.");
            return;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            PushMcpResult(false, "Give the server a name.");
            return;
        }

        bool remote = string.Equals(payload.Transport, "remote", StringComparison.OrdinalIgnoreCase);

        if (remote && string.IsNullOrWhiteSpace(payload.Url))
        {
            PushMcpResult(false, "A remote server needs a URL.");
            return;
        }

        if (!remote && string.IsNullOrWhiteSpace(payload.Command))
        {
            PushMcpResult(false, "A local server needs a command to launch (npx, uvx, node...).");
            return;
        }

        McpServerDefinition definition = BuildMcpDefinition(payload, remote, expand: false);

        WriteMcpEntry(definition, NullIfBlank(payload.Replacing), QueryFlagSet(uri.Query, "signin"));
    }

    // Writes one entry and, optionally, connects to it. Shared by the manual form and the pasted-
    // command path so both land in the store the same way.
    private void WriteMcpEntry(McpServerDefinition definition, string? replacing, bool connect)
    {
        try
        {
            McpServer.Store.Save(definition, replacing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            PushMcpResult(false, $"Could not save: {ex.Message}");
            return;
        }

        // Drop the signature so the next tick re-reads and re-pushes: the list must reflect the save
        // straight away, and the file's own write time is not what the page is waiting on.
        _lastMcpSignature = null;

        // "Save & connect": connect straight away so a remote server's browser handshake happens
        // here, while the user is still setting up. Saving and connecting are ONE bridge verb rather
        // than two calls from the page, because the connection has to read the entry back off disk
        // and the page cannot know when the write landed.
        if (connect)
        {
            BeginMcpSignIn(definition.Name);
            return;
        }

        PushMcpResult(true, $"Saved '{definition.Name}'.");
    }

    // Parses a setup command copied from another client's instructions and either connects to what
    // it describes (test) or writes it and then connects (save).
    //
    // The parse lives in Core and is the SAME function either way, so a command that tests clean
    // cannot save as something else. What the page sends is the raw text: parsing on the host keeps
    // one implementation, unit-tested, rather than a second one in TypeScript that would drift.
    private void HandleMcpCommand(Uri uri, bool save)
    {
        string command = GetQueryValue(uri.Query, "command");

        if (McpCommandParser.Parse(command).IsErr(out string? parseError, out McpServerDefinition? parsed))
        {
            PushMcpResult(false, parseError);
            return;
        }

        if (!parsed.IsRunnable)
        {
            PushMcpResult(false, "That command describes neither a URL nor a program to launch.");
            return;
        }

        if (save)
        {
            // Replacing by its own name, so pasting a refreshed command over an existing entry
            // updates it in place rather than adding a second one beside it.
            WriteMcpEntry(parsed, replacing: parsed.Name, connect: true);
            return;
        }

        // A test connects to what was pasted, so ${VAR} must resolve — the same rule the sign-in
        // path follows, and the reason it reads the file expanded.
        ConnectAndReport(
            ExpandMcpDefinition(parsed),
            parsed.Name,
            "Nothing has been saved — use Save & connect to keep it.");
    }

    // Resolves every ${VAR} in a definition. Values only, never keys, matching what
    // McpServerLibrary expands when it reads the file.
    private static McpServerDefinition ExpandMcpDefinition(McpServerDefinition definition)
    {
        Dictionary<string, string> Resolve(IReadOnlyDictionary<string, string> pairs, StringComparer comparer)
        {
            var resolved = new Dictionary<string, string>(comparer);
            foreach (KeyValuePair<string, string> pair in pairs)
            {
                resolved[pair.Key] = McpServerLibrary.ExpandEnvironment(pair.Value);
            }

            return resolved;
        }

        return definition with
        {
            Command = definition.Command is null
                ? null
                : McpServerLibrary.ExpandEnvironment(definition.Command),
            Arguments = definition.Arguments.Select(McpServerLibrary.ExpandEnvironment).ToList(),
            Environment = Resolve(definition.Environment, StringComparer.Ordinal),
            Url = definition.Url is null ? null : McpServerLibrary.ExpandEnvironment(definition.Url),
            Headers = Resolve(definition.Headers, StringComparer.OrdinalIgnoreCase),
        };
    }

    // Builds the definition one MCP page payload describes. `expand` is the whole reason this is
    // shared rather than duplicated: an EDIT keeps ${VAR} verbatim, because writing the resolved
    // token into the file is the leak the reference existed to prevent, while a CONNECTION must
    // resolve it to the credential it names. Values only, never keys — a header name or an
    // environment variable's name is not a place a reference belongs, and McpServerLibrary expands
    // exactly the same halves when it reads the file.
    private static McpServerDefinition BuildMcpDefinition(McpServerPayload payload, bool remote, bool expand)
    {
        string? Resolve(string? value) =>
            expand && value is not null ? McpServerLibrary.ExpandEnvironment(value) : value;

        Dictionary<string, string> ResolveAll(Dictionary<string, string> pairs)
        {
            if (!expand)
            {
                return pairs;
            }

            var resolved = new Dictionary<string, string>(pairs.Comparer);
            foreach (KeyValuePair<string, string> pair in pairs)
            {
                resolved[pair.Key] = McpServerLibrary.ExpandEnvironment(pair.Value);
            }

            return resolved;
        }

        return new McpServerDefinition(
            payload.Name.Trim(),
            remote ? null : Resolve(payload.Command?.Trim()),
            remote
                ? Array.Empty<string>()
                : CleanList(payload.Args).Select(a => Resolve(a) !).ToList(),
            remote
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ResolveAll(CleanPairs(payload.Env, StringComparer.Ordinal)),
            remote ? null : Resolve(NullIfBlank(payload.Cwd)),
            remote ? Resolve(payload.Url?.Trim()) : null,
            remote
                ? ResolveAll(CleanPairs(payload.Headers, StringComparer.OrdinalIgnoreCase))
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            remote ? Resolve(NullIfBlank(payload.Scope)) : null);
    }

    // Connects to an entry the page has NOT saved, so a URL or a command can be checked before it is
    // committed to the file. Nothing is written and the list is not re-pushed; the only outcome is
    // the message.
    private void HandleTestMcpServer(Uri uri)
    {
        string raw = GetQueryValue(uri.Query, "entry");
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        McpServerPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<McpServerPayload>(raw, ReadOpts);
        }
        catch (JsonException)
        {
            PushMcpResult(false, "Physalia could not read that server definition.");
            return;
        }

        if (payload is null)
        {
            PushMcpResult(false, "Physalia could not read that server definition.");
            return;
        }

        bool remote = string.Equals(payload.Transport, "remote", StringComparison.OrdinalIgnoreCase);

        // A test needs something to connect TO, but not a name to file it under — so unlike the save
        // path it does not insist on one, and stands in a label for the message instead.
        if (remote && string.IsNullOrWhiteSpace(payload.Url))
        {
            PushMcpResult(false, "Fill in the URL first, then test it.");
            return;
        }

        if (!remote && string.IsNullOrWhiteSpace(payload.Command))
        {
            PushMcpResult(false, "Fill in the command first, then test it.");
            return;
        }

        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            payload = payload with { Name = remote ? "this URL" : "this command" };
        }

        ConnectAndReport(
            BuildMcpDefinition(payload, remote, expand: true),
            payload.Name.Trim(),
            "Nothing has been saved — use Save & connect to keep it.");
    }

    // Opens a connection to one configured server, which for a remote entry runs the OAuth sign-in
    // (the bridge opens a browser and waits for the loopback redirect). Doubles as a connection test:
    // what comes back is the tool count, so a wrong URL or a bad command is caught here rather than
    // on the first solve.
    //
    // The token survives this process — the bridge caches it per user under LocalApplicationData,
    // DPAPI-encrypted on Windows — which is what makes signing in at setup time worth anything at
    // all. Without that cache the credential would die with the pooled session ten minutes later.
    private void BeginMcpSignIn(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // Read EXPANDED here, unlike everywhere else on this page: this is a connection, not an
        // edit, so a ${VAR} must resolve to the credential it names.
        McpServerDefinition? definition = McpServer.Store
            .Read()
            .FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            PushMcpResult(false, $"'{name}' is not one of your configured MCP servers.");
            return;
        }

        if (!definition.IsRunnable)
        {
            PushMcpResult(false, $"'{name}' has neither a command nor a URL, so there is nothing to connect to.");
            return;
        }

        ConnectAndReport(definition, name, note: null);
    }

    // Connects, lists the tools, and pushes the outcome to the page. Shared by the sign-in path (a
    // saved entry, read off disk) and the test path (an unsaved draft, built in memory) — the two
    // differ only in where the definition came from and in what is worth saying afterwards.
    private void ConnectAndReport(McpServerDefinition definition, string name, string? note)
    {
        Task.Run(async () =>
        {
            bool ok;
            string message;

            try
            {
                // Generous, because a browser sign-in runs at human speed — the two-minute discovery
                // timeout an MCP Server node uses would expire mid-consent-screen.
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

                Result<McpSession, LlmError> connection = await McpConnections
                    .GetAsync(definition, McpServer.BridgeExecutable(), timeout.Token)
                    .ConfigureAwait(false);

                if (connection.IsErr(out LlmError? error, out McpSession? session))
                {
                    ok = false;
                    message = error.Message;
                }
                else
                {
                    Result<IReadOnlyList<LlmToolDefinition>, LlmError> listed =
                        await session.ListToolsAsync(timeout.Token).ConfigureAwait(false);

                    if (listed.IsErr(out LlmError? listError, out IReadOnlyList<LlmToolDefinition>? tools))
                    {
                        ok = false;
                        message = listError.Message;
                    }
                    else
                    {
                        ok = true;
                        message = tools.Count == 1
                            ? $"Connected to '{name}' — 1 tool available."
                            : $"Connected to '{name}' — {tools.Count} tools available.";

                        if (note is not null)
                        {
                            message += " " + note;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ok = false;
                message = ex.Message;
            }

            // Back to the UI thread: this is a background task and Exec drives the WebView.
            Application.Instance.AsyncInvoke(() => PushMcpResult(ok, message));
        });
    }

    // Removes one configured server.
    private void HandleDeleteMcpServer(Uri uri)
    {
        string name = GetQueryValue(uri.Query, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            McpServer.Store.Remove(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PushMcpResult(false, $"Could not save: {ex.Message}");
            return;
        }

        _lastMcpSignature = null;
        PushMcpResult(true, $"Removed '{name}'. Any MCP Server node still naming it will say so.");
    }

    private static List<string> CleanList(List<string>? values) =>
        (values ?? new List<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToList();

    // Pair rows arrive as [key, value]; a row with no key is a half-filled form field, not a setting.
    // The value is trimmed but otherwise untouched — it may well be a "${VAR}" reference, and this is
    // the last place that could accidentally resolve one.
    private static Dictionary<string, string> CleanPairs(List<List<string>>? pairs, StringComparer comparer)
    {
        var result = new Dictionary<string, string>(comparer);

        foreach (List<string> pair in pairs ?? new List<List<string>>())
        {
            if (pair is { Count: >= 1 } && !string.IsNullOrWhiteSpace(pair[0]))
            {
                result[pair[0].Trim()] = (pair.Count > 1 ? pair[1] ?? string.Empty : string.Empty).Trim();
            }
        }

        return result;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // One-shot push of an MCP save/delete outcome to the page.
    private void PushMcpResult(bool ok, string message)
    {
        string json = JsonSerializer.Serialize(new { ok, message }, WriteOpts);
        Exec($"window.physalia&&window.physalia.setMcpResult&&window.physalia.setMcpResult({json});");
    }

    // Pushes the switcher list — one entry per Chat on the canvas, in left-to-right canvas order —
    // marking which is active and which already has recorded history. Pushed only when the serialised
    // list changes (a Chat added/removed/moved, the active one switched, or a history appearing).
    private void MaybePushChats()
    {
        // Home leads the row and is always present — the way back to placement and provider setup
        // whatever is, or is not, on the canvas. Its sentinel harness key matches no real one, so
        // the row always rules a divider between Home and the first Chat.
        var home = new[]
        {
            new
            {
                id = HomeId,
                active = _home,
                key = HomeId,
                ordinal = -1,
                hasHistory = false,
                emoji = string.Empty,
                harness = HomeId,
                home = true,
            },
        };

        var list = home
            .Concat(EnumerateChats().Select((cb, index) => new
            {
                id = cb.InstanceGuid.ToString(),
                active = !_home && ReferenceEquals(cb, _component),

                // The row's render key, and how a click identifies which circle it was.
                //
                // NOT the InstanceGuid on its own: two Chats can share one. Placing the same preset
                // twice copies the Chat straight out of the archive, guid and all, and a duplicate
                // render key collapses the two circles into one — which is the bug this fixes. The
                // position disambiguates them; the guid still rides along so a click can be
                // cross-checked against it.
                key = $"{cb.InstanceGuid}#{index}",
                ordinal = index,

                hasHistory = ChatHasHistory(cb),
                emoji = cb.Emoji,

                // Which harness this Chat belongs to, so the row can rule a divider wherever the
                // group changes. Empty for a Chat with no harness — one loose on the canvas in a
                // pre-harness file.
                harness = HarnessOf(cb)?.InstanceGuid.ToString() ?? string.Empty,
                home = false,
            }))
            .ToList();

        string json = JsonSerializer.Serialize(list, WriteOpts);
        if (json == _lastChats)
        {
            return;
        }

        _lastChats = json;
        Exec($"window.physalia&&window.physalia.setChats&&window.physalia.setChats({json});");
    }

    // Every Chat in the file, grouped by harness and ordered left-to-right then top-to-bottom, for a
    // stable, intuitive circle sequence.
    //
    // A Chat that is not on a document — the backing component the widget creates when a file holds
    // none — is deliberately absent. It has no conversation to show and nowhere to be switched to;
    // Home is what stands in its place now.
    private List<Chat> EnumerateChats()
    {
        var result = new List<Chat>();

        // The switcher row lists every Chat in the file, so it looks inside harnesses too — a Chat
        // that has moved into one is still a chat the user can switch to, and after the move it is
        // the ONLY place a Chat lives.
        GH_Document? host = LiveHost();
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

        result.Sort(CompareChats);
        return result;
    }

    // Switcher order: harness by harness across the user's canvas, then Chat by Chat within each
    // harness — which is what lets the row rule one divider per boundary and never inside a group.
    //
    // Sorting on the Chat's own pivot alone would interleave harnesses, because every harness
    // sub-document has its own coordinate space: two Chats sitting at the same spot in different
    // harnesses are indistinguishable by position, and a preset's Chat at (0,0) would sort ahead of
    // everything no matter where its proxy sits.
    private static int CompareChats(Chat a, Chat b)
    {
        int cmp = ComparePivots(HarnessPivot(a), HarnessPivot(b));
        return cmp != 0
            ? cmp
            : ComparePivots(a.Attributes?.Pivot ?? default, b.Attributes?.Pivot ?? default);
    }

    private static int ComparePivots(System.Drawing.PointF a, System.Drawing.PointF b)
    {
        int cmp = a.X.CompareTo(b.X);
        return cmp != 0 ? cmp : a.Y.CompareTo(b.Y);
    }

    // The harness holding a Chat, or null when it has none — loose on the canvas in a pre-harness
    // file, or created by the widget and not yet placed.
    private static Harness.HarnessComponent? HarnessOf(Chat chat) =>
        Harness.HarnessComponent.OwnerOf(chat.OnPingDocument());

    // Where a Chat's harness proxy sits on the user's canvas, which is what the group ordering keys
    // on. Harness-less Chats sort ahead of every harness (and so band together at the left end of
    // the row) rather than being scattered through it by an arbitrary pivot.
    private static System.Drawing.PointF HarnessPivot(Chat chat) =>
        HarnessOf(chat)?.Attributes?.Pivot
        ?? new System.Drawing.PointF(float.NegativeInfinity, float.NegativeInfinity);

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
        // Leaving Home counts as a switch even when the component is unchanged — the window was
        // showing the entry screen, not this Chat's conversation.
        if (component is null || (!_home && ReferenceEquals(component, _component)))
        {
            return;
        }

        _home = false;
        _component = component;
        ResetPushedState();
    }

    /// <summary>
    /// Tells whether this window is showing a given Chat's conversation. Home is not showing any
    /// Chat, even though one is still the window's backing component.
    /// </summary>
    /// <param name="chat">The Chat to test.</param>
    /// <returns>true when that Chat's conversation is what the window is displaying.</returns>
    internal bool IsViewing(Chat chat) => !_home && ReferenceEquals(chat, _component);

    // The user's document to work from — the file whose Chats fill the switcher row and whose
    // components "Clear all" sweeps.
    //
    // Normally that is resolved from the viewed Chat, which is more reliable than the canvas (the
    // canvas may be showing a harness's insides, or another file entirely). But an ORPHANED Chat — one
    // whose harness was deleted — must not be trusted: climbing from it stops at the dead
    // sub-document, which is itself non-null, so the row would go on listing the deleted harness's
    // Chats and Clear-all would sweep components no longer in the file. Fall back to the canvas there.
    private GH_Document? LiveHost() =>
        IsViewedChatLive()
            ? Harness.PhyDocuments.Host(_component) ?? Harness.PhyDocuments.ActiveHost()
            : Harness.PhyDocuments.ActiveHost();

    // Whether the Chat being viewed is still part of the open file.
    //
    // Climbing the ownership chain from a live Chat ends at the user's document. It ends at a HARNESS
    // sub-document only when a proxy somewhere up the chain is no longer placed: PhyDocuments.Host
    // stops as soon as it finds an owner that is not on a document, and a harness sub-document
    // outlives the proxy that owned it — the objects inside a deleted harness are untouched, so
    // testing the Chat's own document would always say "live".
    private bool IsViewedChatLive()
    {
        GH_Document? document = _component.OnPingDocument();
        if (document is null)
        {
            return false; // removed outright, or never placed (the widget's backing Chat)
        }

        return !Harness.PhyDocuments.IsHarnessDocument(Harness.PhyDocuments.Host(document));
    }

    /// <summary>
    /// Switches the window to Home — the harness-placement and provider-setup screen.
    ///
    /// <para>Home is not backed by a Chat, so it is always reachable: from its circle at the left of
    /// the switcher row, from the canvas widget, and as the fallback when the Chat being viewed is
    /// deleted. Runs on the UI thread.</para>
    /// </summary>
    public void ShowHome()
    {
        if (_home)
        {
            return;
        }

        _home = true;
        ResetPushedState();
    }

    // Called when any Chat is removed from the document. If the viewed one was deleted, switch to
    // another Chat still on the canvas, or fall back to Home when none remain — the window stays
    // open, because Home is where a new harness is placed from. On Home the switch is silent: the
    // backing component is replaced and the screen stays put. An unrelated removal needs nothing
    // here: the switcher row drops its circle on the next tick. Runs on the UI thread.
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

        if (next is null)
        {
            ShowHome();
            return;
        }

        // Home is a window state rather than a Chat, and the Chat backing it is only whichever one
        // was last viewed. Losing that one — deleted, or replaced wholesale when a harness loads a
        // pipeline in — is no reason to drop the user into somebody else's conversation, so re-back
        // Home and stay on it.
        if (_home)
        {
            _component = next;
            ResetPushedState();
            return;
        }

        SetActiveComponent(next);
    }

    // Resolves a switcher-circle click (?id=<InstanceGuid>, or the Home sentinel) and views it. Runs
    // on the UI thread (bridge dispatch).
    private void HandleSelectChat(Uri uri)
    {
        string id = GetQueryValue(uri.Query, "id");
        if (string.Equals(id, HomeId, StringComparison.Ordinal))
        {
            ShowHome();
            return;
        }

        if (string.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid guid))
        {
            return;
        }

        List<Chat> chats = EnumerateChats();

        // Resolve by POSITION, verifying the guid still matches. Two Chats can share an InstanceGuid
        // (the same preset placed twice brings its Chat's guid out of the archive), so the guid alone
        // cannot say which circle was clicked. The check catches the one race there is: the row
        // changing between the push and the click.
        if (int.TryParse(GetQueryValue(uri.Query, "ordinal"), out int ordinal)
            && ordinal >= 0
            && ordinal < chats.Count
            && chats[ordinal].InstanceGuid == guid)
        {
            SetActiveComponent(chats[ordinal]);
            return;
        }

        foreach (Chat cb in chats)
        {
            if (cb.InstanceGuid == guid)
            {
                SetActiveComponent(cb);
                return;
            }
        }
    }

    // The page has finished loading and installed its window.physalia bridge, so pushes can land.
    //
    // Only the FIRST completed navigation counts. On WebView2 a cancelled phbridge:// navigation
    // also raises this (NavigationCompleted fires with IsSuccess false), and re-running the reset
    // there would force a full history re-push on every single user action.
    private void OnDocumentLoaded(object? sender, WebViewLoadedEventArgs e) => MarkPageReady();

    // Opens the push gate and drops every last-pushed cache, because anything sent while the page
    // was still loading went nowhere.
    //
    // The two global signatures have to go with them. Change detection assumes a swallowed push will
    // be made good by the next change, which holds for history and state but NOT for these: the
    // preset list changes only when the .gh files under Files/PRESETS do, and the switcher row only
    // when Chats are added or moved. A push lost during load left the preset gallery empty for the
    // life of the window — reopening it worked only because that built a window with fresh caches.
    private void MarkPageReady()
    {
        if (_pageReady)
        {
            return;
        }

        _pageReady = true;
        ResetPushedState();
        _lastPresetSignature = null;
        _lastChats = null;
    }

    // Drops the per-component last-pushed caches so a freshly viewed Chat re-pushes its full
    // history/state next tick. The preset and chat-list signatures are global (not per viewed
    // component), so a Chat switch leaves them intact — see MarkPageReady for the one case that
    // must clear them too.
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

        // Answer synchronously on the very first pass so the window opens on the correct screen.
        // Without this, needsSetup stays false — "everything is fine" — until the async probe lands,
        // and that probe waits on PATH scans and a socket timeout.
        _configuredProviders ??= ProviderAvailability.ConfiguredProviderIdsNow();

        _providerProbeInFlight = true;

        Task.Run(async () =>
        {
            IReadOnlyList<string>? configured;
            IReadOnlyList<UiProviderStatus>? statuses;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                IReadOnlyList<ProviderStatus> probed =
                    await ProviderAvailability.StatusesAsync(ProbeClient, cts.Token).ConfigureAwait(false);

                configured = probed.Where(p => p.Ready).Select(p => p.Id).ToList();
                statuses = probed
                    .Select(p => new UiProviderStatus(
                        p.Id,
                        p.Activated,
                        p.Source.ToString().ToLowerInvariant(),
                        p.Detail))
                    .ToList();
            }
            catch
            {
                // Keep the last known answer on failure; leaving it null before the first result means
                // setup never flashes during the very first probe (null is treated as "configured").
                configured = _configuredProviders;
                statuses = _providerStatuses;
            }

            Application.Instance.AsyncInvoke(() =>
            {
                _configuredProviders = configured;
                _providerStatuses = statuses ?? Array.Empty<UiProviderStatus>();
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

        // Lay the Chat out and drop the document's attribute cache, which is what the canvas
        // hit-tests against. Rendering walks Objects directly, so a stale cache produces a component
        // you can SEE but cannot select or drag — exactly the shape of the bug this fixes. GH's own
        // advice on DestroyAttributeCache is to call it "whenever you do something which might affect
        // attributes", and adding an object to an off-canvas document is one of those things.
        chat.Attributes.ExpireLayout();
        chat.Attributes.PerformLayout();
        inner.DestroyAttributeCache();

        PlaceHarness(canvas, host, harness);

        // Only now can the Chat see its siblings: until the proxy is on the host document, the harness
        // it lives in is not reachable from the file, so its emoji was picked blind.
        chat.EnsureDistinctEmoji();

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
        GH_Document? host = LiveHost();
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
    // value is resolved by MATCH against the enumerated library rather than by composing it into a
    // path, so nothing it contains can reach outside the preset folders. Runs on the UI thread
    // (bridge dispatch), so editing the document is safe here — outside any active solve.
    private void HandlePlacePreset(Uri uri)
    {
        string file = GetQueryValue(uri.Query, "file");
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        if (Harness.PresetLibrary.Resolve(file) is not { } path)
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

        // A preset carries the emoji it was saved with, so placing the same one twice would give both
        // copies the same circle. Its Chats were read in before the harness existed, so this is the
        // first moment they can see what the rest of the file is using.
        foreach (Chat chat in contents.Objects.OfType<Chat>())
        {
            chat.EnsureDistinctEmoji();
        }

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

    // Maps the committed conversation to the UI message shape (text / images / tool calls / sources).
    private List<UiMessage> BuildMessages(Conversation? convo)
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
                message.IsFeedback,
                BuildSources(message.Sources)));
        }

        return messages;
    }

    // Resolves a turn's origin trail into what the UI badges it with: the node's CURRENT nickname
    // and icon, looked up live in this Chat's document (which, inside a harness, is the harness
    // sub-document the whole pipeline lives in). The trail's recorded name is the fallback for a
    // component that has since been deleted — a turn already in the log must keep its attribution.
    private List<UiSource>? BuildSources(IReadOnlyList<ComponentOrigin> origins)
    {
        if (origins.Count == 0)
        {
            return null;
        }

        var sources = new List<UiSource>(origins.Count);
        GH_Document? doc = _component.OnPingDocument();
        foreach (ComponentOrigin origin in origins)
        {
            IGH_DocumentObject? obj = doc?.FindObject(origin.Id, false);
            string name = obj is null
                ? origin.Name
                : string.IsNullOrWhiteSpace(obj.NickName) ? obj.Name : obj.NickName;
            sources.Add(new UiSource(name, IconDataUri(origin.Id, obj)));
        }

        return sources;
    }

    // A component icon as a data URI, encoded once per component and cached: the history is rebuilt
    // on every conversation change, and re-encoding a PNG per feedback turn per push is pure waste.
    // Only a resolved lookup is cached, so a component found later still gets its icon.
    private string? IconDataUri(Guid id, IGH_DocumentObject? obj)
    {
        if (_sourceIcons.TryGetValue(id, out string? cached))
        {
            return cached;
        }

        if (obj is null)
        {
            return null;
        }

        string? uri = null;
        try
        {
            Bitmap? icon = obj.Icon_24x24;
            if (icon is not null)
            {
                using var buffer = new MemoryStream();
                icon.Save(buffer, ImageFormat.Png);
                uri = "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());
            }
        }
        catch (Exception)
        {
            // An icon is decoration: a component whose bitmap cannot be read still gets its name.
            uri = null;
        }

        _sourceIcons[id] = uri;
        return uri;
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

    // ---- PDF intake -----------------------------------------------------------------------
    //
    // Attaching a PDF puts almost nothing in the conversation. The file is registered for the
    // session and the turn carries a short descriptor; every page of it is pulled on demand by the
    // model-callable read_pdf tool. That split is what makes a four-hundred-sheet drawing set
    // affordable to attach at all.
    //
    // Files are referenced where they sit and never copied — which is why the PICKER matters: it
    // runs host-side and yields a real path, where a browser drop can only ever give us bytes.

    // The most a dropped PDF may weigh. Drops carry their bytes across the bridge as base64, so
    // this is a real ceiling rather than a tidiness rule; the picker has no such limit because it
    // moves nothing.
    private const int MaxDroppedPdfBytes = 100 * 1024 * 1024;

    /// <summary>
    /// The PDF session for a pipeline, scoped to the document its Conversation Log lives in — the
    /// same document the Read PDF tool node resolves, which is what connects the two components.
    /// </summary>
    /// <param name="log">The Conversation Log being viewed.</param>
    /// <returns>The session, or null when nothing is wired.</returns>
    private static PdfSession? PdfSessionFor(ConversationLog? log) =>
        PdfRegistry.For(log?.OnPingDocument());

    /// <summary>
    /// The PDF session for the pipeline this window is attached to.
    /// </summary>
    /// <returns>The session, or null when nothing is wired.</returns>
    private PdfSession? CurrentPdfSession() =>
        PdfSessionFor(PromptPipelineView.FindConversationLog(_component, 0));

    /// <summary>
    /// Whether a Read PDF human tool is wired, which is the grant every intake path here checks.
    /// </summary>
    /// <returns>True when PDF intake is enabled.</returns>
    private bool PdfIntakeGranted() =>
        PromptPipelineView.FindConversationLog(_component, 0)?.HasReadPdfTool == true;

    /// <summary>
    /// Opens a native file picker and registers whatever is chosen. Host-side on purpose: this is
    /// the only intake path that learns a file's real location, and so the one to use for a set too
    /// large to hand across the bridge.
    /// </summary>
    private void HandleAddPdf()
    {
        // Re-checked here and not only in the UI: the wire can change between the button being
        // drawn and being pressed.
        if (!PdfIntakeGranted())
        {
            return;
        }

        PdfSession? session = CurrentPdfSession();
        if (session is null)
        {
            return;
        }

        using var dialog = new OpenFileDialog { MultiSelect = true, Title = "Attach PDFs" };
        dialog.Filters.Add(new FileFilter("PDF", ".pdf"));

        if (dialog.ShowDialog(this) != DialogResult.Ok)
        {
            return;
        }

        foreach (string path in dialog.Filenames)
        {
            RegisterPdf(session, path);
        }
    }

    /// <summary>
    /// Drops one queued PDF, for the X on its chip.
    /// </summary>
    /// <param name="uri">The bridge URI, carrying the alias.</param>
    private void HandleRemovePdf(Uri uri)
    {
        string alias = GetQueryValue(uri.Query, "alias");
        if (!string.IsNullOrWhiteSpace(alias))
        {
            CurrentPdfSession()?.RemovePending(alias);
        }
    }

    /// <summary>
    /// Receives PDFs dropped onto the prompt box. A drop hands us bytes and a file name and no
    /// path, so the bytes are spooled to a temp file and referenced from there.
    /// </summary>
    /// <param name="message">The drop payload.</param>
    private void ReceiveDroppedPdfs(SubmitMessage message)
    {
        if (!PdfIntakeGranted())
        {
            return;
        }

        PdfSession? session = CurrentPdfSession();
        if (session is null || message.Images is null)
        {
            return;
        }

        foreach (SubmitImage file in message.Images)
        {
            if (string.IsNullOrEmpty(file.Base64))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(file.Base64);
            }
            catch (FormatException)
            {
                continue;
            }

            if (bytes.Length > MaxDroppedPdfBytes)
            {
                ShowPdfProblem(
                    $"\"{file.Filename}\" is too large to drop ({bytes.Length / (1024 * 1024)} MB). " +
                    "Use the PDF button instead — it reads the file where it sits, at any size.");
                continue;
            }

            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "Physalia", "dropped-pdfs");
                Directory.CreateDirectory(dir);

                string name = string.IsNullOrWhiteSpace(file.Filename) ? "dropped.pdf" : file.Filename;
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    name = name.Replace(c, '-');
                }

                string path = Path.Combine(dir, Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + name);
                File.WriteAllBytes(path, bytes);
                RegisterPdf(session, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowPdfProblem($"\"{file.Filename}\" could not be saved: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Probes and registers one file, reporting anything that makes it unreadable rather than
    /// letting it fail later inside a tool call.
    /// </summary>
    /// <param name="session">The session to register into.</param>
    /// <param name="path">The file to register.</param>
    private void RegisterPdf(PdfSession session, string path)
    {
        try
        {
            session.Add(path);
        }
        catch (Exception ex)
        {
            // Probing opens and walks the document, so a corrupt or encrypted file fails HERE,
            // while somebody is looking at it — much better than surfacing three turns later as an
            // unexplained tool error.
            ShowPdfProblem($"\"{Path.GetFileName(path)}\" could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports a PDF problem to the user.
    /// </summary>
    /// <param name="text">The message to show.</param>
    private void ShowPdfProblem(string text) =>
        MessageBox.Show(this, text, "Read PDF", MessageBoxButtons.OK, MessageBoxType.Warning);

    /// <summary>
    /// Prepends the descriptor for any PDFs attached since the last send, and clears the queue so
    /// each attachment is announced exactly once.
    /// </summary>
    /// <param name="text">The prompt text.</param>
    /// <returns>The prompt text with the descriptor in front of it.</returns>
    private string PrependPdfDescriptor(string text)
    {
        PdfSession? session = CurrentPdfSession();
        if (session is null)
        {
            return text;
        }

        // Drained even when the grant has gone, so an unwired tool leaves no stale queue behind to
        // surface on some later turn.
        IReadOnlyList<PdfDescriptor> attached = session.DrainPending();
        if (attached.Count == 0 || !PdfIntakeGranted())
        {
            return text;
        }

        string descriptor = PdfReports.DescribeAttachments(attached);
        return string.IsNullOrEmpty(text) ? descriptor : descriptor + "\n\n" + text;
    }

    // One provider as the setup page draws it. `source` is the lower-cased ProviderSource — "none",
    // "environment", "stored" or "detected" — and together with `activated` it picks which of the
    // three footers the page shows: the form, one Connect button, or a connected pill. `detail`
    // names the environment variable a key was found in, so the button can say which one.
    private sealed record UiProviderStatus(string Id, bool Activated, string Source, string? Detail);

    private sealed record UiImage(string Base64, string MediaType);

    private sealed record UiTool(string Id, string Name, string State, object? Input, string? Output, string? ErrorText);

    private sealed record UiMessage(
        string Id,
        string Role,
        string Text,
        IReadOnlyList<UiImage>? Images,
        IReadOnlyList<UiTool>? Tools,
        bool Feedback,
        IReadOnlyList<UiSource>? Sources);

    // The component a turn came from, as the UI shows it: nickname plus its icon as a data URI
    // (null when the node is gone or its bitmap could not be read).
    private sealed record UiSource(string Name, string? Icon);

    private sealed record SubmitImage(string Base64, string MediaType, string Filename);

    // One attached PDF as the composer draws it. A summary only — never bytes: a drawing set runs
    // to hundreds of megabytes and the page has no use for the file itself.
    private sealed record UiPdf(string Alias, string Name, int Pages);


    // An outgoing message from the page. Kind is absent (or "prompt") for a typed prompt; the two
    // snapshot kinds mark a capture that went out to the image editor in send mode and came back
    // marked up — it carries no text, because the message that speaks for it is read from the wired
    // tool host-side (see SubmitJsonPayload).
    private sealed record SubmitMessage(string Text, List<SubmitImage>? Images, string? Kind);


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

    // One MCP server entry pushed from the window's MCP page. Transport picks which half matters:
    // "local" reads Command/Args/Cwd/Env, "remote" reads Url/Headers/Scope. Replacing carries the
    // entry's previous name when a rename is being saved, so it is edited in place rather than
    // added alongside the original.
    private sealed record McpServerPayload(
        string Name,
        string? Transport,
        string? Command,
        List<string>? Args,
        string? Cwd,
        List<List<string>>? Env,
        string? Url,
        List<List<string>>? Headers,
        string? Scope,
        string? Replacing);
}
