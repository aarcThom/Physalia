// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Clusters;
using Physalia.Core.Grounding.Components;
using Physalia.Core.Grounding.Tools;
using Physalia.Core.HumanTools;
using Physalia.Core.Recording;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Harness;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Maintains the full conversation history as an append-only log and emits it as a single Signal
/// that <b>carries the full Instructions</b> (system prompt + conversation) for inference — the
/// trigger and the data are one event. The Conversation Log is the uncompacted source of truth: a compaction
/// component sits on the forward path (<c>Conversation Log → Compactor → LLM Call</c>) and only transforms the
/// copy on the signal; the LLM Call's response flows back (wirelessly, via Feedback Collector) and
/// appends to this full log.
///
/// <para>Events arrive on dedicated Signal inputs — Prompt, Response, Feedback, LLM Tool — so the turn
/// type comes from event identity, never from conversation parity. Signals are consumed in global
/// sequence order (causal order), which guarantees a response is recorded before the feedback it
/// provoked even when both arrive in the same solve. User-side text arriving while the last turn is
/// already a user message merges into that message, preserving the role alternation providers require.
/// The outgoing Signal is minted only when a user turn was recorded — assistant turns latch quietly so
/// an LLM Call wired off this output cannot re-fire itself in an infinite loop.</para>
/// </summary>
public class ConversationLog : StatefulComponentBase
{
    private const int InSystemPrompt = 0;
    private const int InPromptSignal = 1;
    private const int InGrounding = 2;
    private const int InHumanTools = 3;
    private const int InResponseSignal = 4;
    private const int InFeedbackSignal = 5;
    private const int InLlmToolSignal = 6;

    private const int OutSignal = 0;

    // How far to walk upstream through bare relay parameters when looking for the component that owns
    // a setting. Four hops is more indirection than any sane canvas has, and it bounds the walk.
    private const int MaxRelayDepth = 4;

    private Conversation _conversation = Conversation.Empty;

    // Legacy settings, read out of a file written before each of them moved onto the component that
    // owns it — the component-catalog selection and signature toggle onto Component Catalog, the
    // cluster selection onto Cluster Grounding, the units override onto Document Units Grounding, the
    // snapshot wording onto each snapshot tool, the tool on/off selection onto the tool nodes
    // themselves. Applied once, on the first solve after the read (the first moment the wired owners
    // are known) and then dropped. Nothing is ever written back under these keys: the components are
    // the only home now, which is what lets a configured grounder or tool travel with a copy and ship
    // inside a preset.
    private bool _legacyPending;
    private GroundingSelection? _legacySelection;
    private ClusterSelection? _legacyClusterSelection;
    private ToolsSelection? _legacyToolsSelection;
    private string? _legacyUnitsOverride;
    private string? _legacySnapshotMessage;
    private string? _legacyViewSnapshotMessage;
    private bool _legacyExposeSignatures;

    // Caches of the live grounding wired in this solve, for the prompt build and for the chat UI to
    // read. _liveCatalog is the merged catalog across all wired ComponentCatalogGroundings;
    // _liveClusterCatalog is the same for ClusterCatalogGroundings; _liveUnitsGrounding is the wired
    // DocumentUnitsGrounding (last one wins if several are wired).
    private IReadOnlyList<Grounding> _liveGroundings = Array.Empty<Grounding>();
    private ComponentCatalog? _liveCatalog;
    private ClusterCatalog? _liveClusterCatalog;
    private DocumentUnitsGrounding? _liveUnitsGrounding;

    // The tool definitions carried by every wired ToolsGrounding (the Tools Present grounder), merged.
    // Lifted onto the Instructions minted for inference so the LLM Call advertises them to the model,
    // and their names feed the chat input's "/t/" reference. Empty when no tools grounding is wired.
    private IReadOnlyList<LlmToolDefinition> _liveTools = Array.Empty<LlmToolDefinition>();

    // Whether a tools grounding is wired at all — separate from _liveTools being non-empty, because
    // parking every tool empties the advertised set while the Tools Present grounder is still there.
    // The chat window gates its tools page on THIS, or switching the last tool off would hide the page
    // that switches it back on.
    private bool _hasToolsGrounding;

    // Caches of the other grounding kinds wired this solve, so every kind has a live read for the chat
    // UI's grounding pages: whether a canvas-state grounding is wired, the Rhino-referenced geometry
    // params detected on the canvas, and the available python functions.
    private bool _hasCanvasStateGrounding;
    private bool _groupScopedCanvasGrounding;
    private IReadOnlyList<ReferencedGeometryInput> _liveReferencedGeometry = Array.Empty<ReferencedGeometryInput>();
    private IReadOnlyList<PythonFunctionGrounding> _livePythonFunctions = Array.Empty<PythonFunctionGrounding>();

    // Caches of the human tools wired this solve — the chat-window affordances the user enabled by
    // wiring components into the Human Tools input. Session-only, refreshed every solve.
    private GeometrySnapshotTool? _liveSnapshotTool;
    private ViewSnapshotTool? _liveViewSnapshotTool;
    private bool _hasAddImageTool;
    private bool _hasExportTool;
    private bool _hasSignalTraceTool;
    private bool _hasImageMarkUpTool;

    // Set ONLY by our own scheduled callback so the latch runs after the visible delay.
    private bool _doLatch;
    private RecordOutcome _pendingOutcome;
    private string _pendingUserText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationLog"/> class.
    /// </summary>
    public ConversationLog()
        : base("Conversation Log", "Conversation Log", "Keeps the conversation. Everything the model is told arrives here — instructions, what you typed, what it said, what came back from tools and checks — and every time your side gains a turn the whole thing goes out for another reply.", "Pipeline")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("43A02F6D-D97D-4241-B4DD-067D7AE0D75E");

    /// <summary>
    /// Gets the active conversation, for display only — e.g. the Chat window. Always the full
    /// uncompacted log (compaction happens downstream, never here). Conversation is immutable, so
    /// callers cannot corrupt the log, but they must never hold the reference across solves (it is
    /// replaced on every append).
    /// </summary>
    public Conversation ActiveConversation => _conversation;

    /// <summary>
    /// Gets the two-level tree (tab → panels) of the component catalog(s) wired into the Grounding
    /// input, merged across all of them. Empty when no component-catalog grounding is wired. For the
    /// chat UI's grounding selector — updated every solve.
    /// </summary>
    public IReadOnlyList<CatalogCategory> AvailableGroundingTree =>
        _liveCatalog?.CategoryTree ?? Array.Empty<CatalogCategory>();

    /// <summary>
    /// Gets a value indicating whether any component-catalog grounding is currently wired (so the
    /// chat UI can enable/grey its grounding affordance).
    /// </summary>
    public bool HasComponentGrounding => _liveCatalog is not null;

    /// <summary>
    /// Gets the entries of the grounded component catalog with the current grounding selection applied
    /// — the components actually available to the model, for the chat input's <c>/c/&lt;tab&gt;/&lt;
    /// component&gt;</c> reference (tab → components autocomplete and submit-time normalization). Empty
    /// when no component-catalog grounding is wired.
    /// </summary>
    public IReadOnlyList<CatalogEntry> IncludedComponentEntries =>
        _liveCatalog?.Filtered(EffectiveGroundingSelection).Entries ?? Array.Empty<CatalogEntry>();

    /// <summary>
    /// Gets the current grounding selection, or <see langword="null"/> for the default — only the
    /// leaves holding native components; plug-in tabs stay listed but unchecked until opted in.
    /// </summary>
    public GroundingSelection? GroundingSelectionOrNull =>
        Owners<ComponentCatalogGrounder>().LastOrDefault()?.Selection;

    /// <summary>
    /// Gets the selection actually applied to the component catalog: the user's explicit selection
    /// when one is set, otherwise the native-only default computed from the live catalog. Null only
    /// when no catalog grounding is wired. The chat UI renders THIS (never null-as-all), so plug-in
    /// tabs show unchecked by default instead of silently checked.
    /// </summary>
    public GroundingSelection? EffectiveGroundingSelection =>
        GroundingSelectionOrNull ?? _liveCatalog?.NativeSelection();

    /// <summary>
    /// Gets the clusters wired into the Grounding input, merged across every wired cluster grounding.
    /// Empty when no cluster grounding is wired. For the chat UI's cluster selector and the <c>/c/</c>
    /// prompt autocomplete — updated every solve.
    /// </summary>
    public IReadOnlyList<ClusterEntry> AvailableClusters =>
        _liveClusterCatalog?.Entries ?? Array.Empty<ClusterEntry>();

    /// <summary>
    /// Gets a value indicating whether any cluster grounding is currently wired (so the chat UI can
    /// enable/grey its cluster affordance).
    /// </summary>
    public bool HasClusterGrounding => _liveClusterCatalog is not null;

    /// <summary>
    /// Gets the names of the tools currently in use (wired into a Router, collected by a wired Tools
    /// Present grounder). For the chat input's <c>/t/</c> prompt autocomplete — updated every solve.
    /// Empty when no tools grounding is wired.
    /// </summary>
    public IReadOnlyList<string> AvailableToolNames
    {
        get
        {
            // Every tool in use, PARKED ONES INCLUDED — the chat window's tools page has to list a
            // switched-off tool or there would be nothing to switch back on. Falls back to the
            // advertised set when no Tools Present grounder is reachable from this input (a grounding
            // arriving through something other than the grounder itself), so the list is never emptier
            // than what the model was told.
            IReadOnlyList<LlmToolComponentBase> scanned = ScannedToolNodes();
            IEnumerable<string> names = scanned.Count > 0
                ? scanned.Select(t => t.AdvertisedDefinition.Name)
                : _liveTools.Select(t => t.Name);

            return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// Gets a value indicating whether any tools grounding is currently wired (so the chat UI can
    /// enable/grey its tool affordance).
    /// </summary>
    public bool HasToolsGrounding => _hasToolsGrounding;

    /// <summary>
    /// Gets the current tools selection, or <see langword="null"/> for the default (include every tool
    /// present on the canvas).
    /// </summary>
    public ToolsSelection? ToolsSelectionOrNull
    {
        get
        {
            // Derived from the nodes, never stored: each tool node owns its own advertise switch, so
            // "the selection" is just which of them are on. Null when they all are, which is the
            // include-everything default the chat window renders as a full set of ticks.
            IReadOnlyList<LlmToolComponentBase> scanned = ScannedToolNodes();
            if (scanned.Count == 0 || scanned.All(t => t.Advertise))
            {
                return null;
            }

            return ToolsSelection.FromNames(
                scanned.Where(t => t.Advertise).Select(t => t.AdvertisedDefinition.Name));
        }
    }

    /// <summary>
    /// Gets the parameters on the canvas that reference live Rhino geometry (detected from the
    /// document, not from any grounding), for the chat UI's Referenced Rhino Geometry page. Empty
    /// when none exist.
    /// </summary>
    public IReadOnlyList<ReferencedGeometryInput> AvailableReferencedGeometry => _liveReferencedGeometry;

    /// <summary>
    /// Gets a value indicating whether a canvas-state grounding is currently wired (so the chat UI
    /// can show/hide the Referenced Rhino Geometry page — without the canvas state, the model
    /// cannot see those parameters anyway).
    /// </summary>
    public bool HasCanvasStateGrounding => _hasCanvasStateGrounding;

    /// <summary>
    /// Gets the python functions wired via a Python Function grounding, for the chat UI's grounding
    /// page. Empty when none is wired.
    /// </summary>
    public IReadOnlyList<PythonFunctionGrounding> AvailablePythonFunctions => _livePythonFunctions;

    /// <summary>
    /// Gets a value indicating whether any python-function grounding is currently wired.
    /// </summary>
    public bool HasPythonGrounding => _livePythonFunctions.Count > 0;

    /// <summary>
    /// Gets the current cluster selection, or <see langword="null"/> for the default (include all).
    /// </summary>
    public ClusterSelection? ClusterSelectionOrNull =>
        Owners<ClusterGrounder>().LastOrDefault()?.Selection;

    /// <summary>
    /// Gets a value indicating whether a document-units grounding is currently wired (so the chat UI
    /// can show/hide its Document Units affordance).
    /// </summary>
    public bool HasUnitsGrounding => _liveUnitsGrounding is not null;

    /// <summary>
    /// Gets the live document units carried by the wired grounding — what the model receives by
    /// default, and what the chat UI shows as the document's current units. Empty when no units
    /// grounding is wired.
    /// </summary>
    public string DocumentUnits => _liveUnitsGrounding?.Units ?? string.Empty;

    /// <summary>
    /// Gets the current document-units override, or <see langword="null"/> when the live document
    /// units are used (the default).
    /// </summary>
    public string? UnitsOverrideOrNull =>
        Owners<DocumentUnitsGrounder>().LastOrDefault()?.Override;

    /// <summary>
    /// Gets a value indicating whether the component grounding folds typed input/output signatures
    /// into the prompt for every included component, rather than only the curated common set.
    /// </summary>
    public bool ExposeComponentSignatures =>
        Owners<ComponentCatalogGrounder>().Any(g => g.ExposeSignatures);

    /// <summary>
    /// Gets a value indicating whether a Geometry Snapshot human tool is currently wired (so the
    /// chat UI can show its panel page and its send-a-snapshot geometry button).
    /// </summary>
    public bool HasGeometrySnapshotTool => _liveSnapshotTool is not null;

    /// <summary>
    /// Gets a value indicating whether an Add Image human tool is currently wired (so the chat UI
    /// can enable image attachments in the prompt box; without it image intake is fully disabled).
    /// </summary>
    public bool HasAddImageTool => _hasAddImageTool;

    /// <summary>
    /// Gets a value indicating whether an Export Conversation human tool is currently wired (so the
    /// chat UI can show its export button, which writes the viewed conversation to a transcript).
    /// </summary>
    public bool HasExportTool => _hasExportTool;

    /// <summary>
    /// Gets a value indicating whether a Signal Trace human tool is currently wired (so the chat UI
    /// can show the button that opens the signal-trace window).
    /// </summary>
    public bool HasSignalTraceTool => _hasSignalTraceTool;

    /// <summary>
    /// Gets a value indicating whether an Image Mark Up human tool is currently wired (so the chat UI
    /// opens every snapshot capture in its image editor, and puts an edit button on each attached
    /// image's thumbnail).
    /// </summary>
    public bool HasImageMarkUpTool => _hasImageMarkUpTool;

    /// <summary>
    /// Gets a value indicating whether the wired Geometry Snapshot tool sends its snapshot
    /// immediately as its own message (carrying <see cref="GeometrySnapshotMessage"/>) rather than
    /// attaching it to the prompt box for the human to caption. False when no tool is wired.
    /// </summary>
    public bool GeometrySnapshotSendsMessage => _liveSnapshotTool?.SendWithMessage == true;

    /// <summary>
    /// Gets the default snapshot message carried by the wired tool — what accompanies the
    /// snapshot image unless overridden. Empty when no Geometry Snapshot tool is wired.
    /// </summary>
    public string GeometrySnapshotDefaultMessage => _liveSnapshotTool?.Message ?? string.Empty;

    /// <summary>
    /// Gets the current snapshot-message override, or <see langword="null"/> when the wired
    /// tool's default message is used.
    /// </summary>
    public string? SnapshotMessageOverrideOrNull =>
        Owners<GeometrySnapshot>().LastOrDefault()?.MessageOverride;

    /// <summary>
    /// Gets the text sent alongside the snapshot image: the override when set, else the wired
    /// tool's default. Empty when no Geometry Snapshot tool is wired.
    /// </summary>
    public string GeometrySnapshotMessage => SnapshotMessageOverrideOrNull ?? GeometrySnapshotDefaultMessage;

    /// <summary>
    /// Gets a value indicating whether a View Snapshot human tool is currently wired (so the chat UI
    /// can show its panel page and its view button). Unlike the geometry snapshot there is no second
    /// condition: a view capture needs nothing on the canvas, so wired is armed.
    /// </summary>
    public bool HasViewSnapshotTool => _liveViewSnapshotTool is not null;

    /// <summary>
    /// Gets a value indicating whether the wired View Snapshot tool sends its capture immediately as
    /// its own message (carrying <see cref="ViewSnapshotMessage"/>) rather than attaching it to the
    /// prompt box for the human to caption. False when no tool is wired.
    /// </summary>
    public bool ViewSnapshotSendsMessage => _liveViewSnapshotTool?.SendWithMessage == true;

    /// <summary>
    /// Gets the default message carried by the wired View Snapshot tool — what accompanies the view
    /// capture unless overridden. Empty when no View Snapshot tool is wired.
    /// </summary>
    public string ViewSnapshotDefaultMessage => _liveViewSnapshotTool?.Message ?? string.Empty;

    /// <summary>
    /// Gets the current view-snapshot message override, or <see langword="null"/> when the wired
    /// tool's default message is used.
    /// </summary>
    public string? ViewSnapshotMessageOverrideOrNull =>
        Owners<ViewSnapshot>().LastOrDefault()?.MessageOverride;

    /// <summary>
    /// Gets the text sent alongside the view capture: the override when set, else the wired tool's
    /// default. Empty when no View Snapshot tool is wired.
    /// </summary>
    public string ViewSnapshotMessage => ViewSnapshotMessageOverrideOrNull ?? ViewSnapshotDefaultMessage;

    /// <summary>
    /// Gets a value indicating whether images may ride a submitted prompt. The Add Image tool is the
    /// general grant, but a snapshot tool in attach mode grants its own narrow lane: its capture lands
    /// in the prompt box and leaves on the human's own turn, so it must not be dropped at submit time
    /// just because Add Image happens to be unwired.
    /// </summary>
    public bool AcceptsPromptImages =>
        _hasAddImageTool
        || (HasGeometrySnapshotTool && !GeometrySnapshotSendsMessage)
        || (HasViewSnapshotTool && !ViewSnapshotSendsMessage);

    /// <inheritdoc/>
    protected override string ClearMenuText => "Clear Conversation";

    /// <summary>
    /// Sets the grounding selection (null = include everything) on every wired Component Catalog
    /// grounder and re-solves so the change takes effect on the next minted Instructions. Called from
    /// the chat window on the UI thread.
    ///
    /// <para>The selection is stored on the grounder, not here — the chat window's page and the
    /// component are two views of one setting, so nothing has to be reconciled and it is saved with the
    /// component that owns it. Every wired grounder is set, matching the read (the last one wins), so
    /// two of them can never disagree.</para>
    /// </summary>
    /// <param name="selection">The new selection, or null to include everything.</param>
    public void SetGroundingSelection(GroundingSelection? selection)
    {
        foreach (ComponentCatalogGrounder grounder in Owners<ComponentCatalogGrounder>())
        {
            grounder.SetSelection(selection);
        }

        ExpireSolution(true);
    }

    /// <summary>
    /// Sets the cluster selection (null = include every available cluster) and re-solves so the
    /// change takes effect on the next minted Instructions. Called from the chat window on the UI
    /// thread.
    /// </summary>
    /// <param name="selection">The new selection, or null to include every cluster.</param>
    public void SetClusterSelection(ClusterSelection? selection)
    {
        foreach (ClusterGrounder grounder in Owners<ClusterGrounder>())
        {
            grounder.SetSelection(selection);
        }

        ExpireSolution(true);
    }

    /// <summary>
    /// Switches each tool node in use on or off to match the given selection (null = advertise every
    /// tool present on the canvas). Called from the chat window on the UI thread.
    ///
    /// <para>Nothing is stored here: the switch lives on the tool node, so it is saved with the node,
    /// travels with a copy of it, and cannot be confused with a second node advertising the same tool
    /// name. Flipping a node re-solves it, and the wired Tools Present grounder — which scans rather
    /// than being wired to them — notices at the end of that solution and re-emits.</para>
    /// </summary>
    /// <param name="selection">The new selection, or null to advertise every tool in use.</param>
    public void SetToolsSelection(ToolsSelection? selection)
    {
        foreach (LlmToolComponentBase tool in ScannedToolNodes())
        {
            tool.SetAdvertise(selection is null || selection.Includes(tool.AdvertisedDefinition.Name));
        }

        ExpireSolution(true);
    }

    /// <summary>
    /// Sets the document-units override (null = use the live document units) and re-solves so the
    /// change takes effect on the next minted Instructions. The document itself is never modified.
    /// Called from the chat window on the UI thread.
    /// </summary>
    /// <param name="units">The override unit text, or null to use the live document units.</param>
    public void SetUnitsOverride(string? units)
    {
        foreach (DocumentUnitsGrounder grounder in Owners<DocumentUnitsGrounder>())
        {
            grounder.SetOverride(units);
        }

        ExpireSolution(true);
    }

    /// <summary>
    /// Sets the geometry-snapshot message override (null = use the wired tool's default
    /// message) and re-solves. The message accompanies the viewport snapshot sent by the chat
    /// window's geometry button. Called from the chat window on the UI thread.
    /// </summary>
    /// <param name="message">The override text, or null to use the tool's default message.</param>
    public void SetSnapshotMessageOverride(string? message) => SetSnapshotMessage<GeometrySnapshot>(message);

    /// <summary>
    /// Switches the wired Geometry Snapshot tool(s) between sending the snapshot as its own message
    /// and attaching it to the prompt box. The flag lives on the Geometry Snapshot component (whose
    /// context menu shows the same checkmark), not here: the chat window's switch and the canvas menu
    /// are two views of one setting, so nothing has to be reconciled. Every wired tool is set, since
    /// the last one collected is the one that wins. Called from the chat window on the UI thread.
    /// </summary>
    /// <param name="on">True to send the snapshot as its own message with its default text; false to attach it to the prompt box.</param>
    public void SetGeometrySnapshotSendsMessage(bool on) => SetSnapshotSendsMessage<GeometrySnapshot>(on);

    /// <summary>
    /// Sets the view-snapshot message override (null = use the wired tool's default message) and
    /// re-solves. The message accompanies the viewport capture sent by the chat window's view button.
    /// Called from the chat window on the UI thread.
    /// </summary>
    /// <param name="message">The override text, or null to use the tool's default message.</param>
    public void SetViewSnapshotMessageOverride(string? message) => SetSnapshotMessage<ViewSnapshot>(message);

    /// <summary>
    /// Switches the wired View Snapshot tool(s) between sending the capture as its own message and
    /// attaching it to the prompt box — the view-snapshot counterpart of
    /// <see cref="SetGeometrySnapshotSendsMessage"/>. Called from the chat window on the UI thread.
    /// </summary>
    /// <param name="on">True to send the capture as its own message with its default text; false to attach it to the prompt box.</param>
    public void SetViewSnapshotSendsMessage(bool on) => SetSnapshotSendsMessage<ViewSnapshot>(on);

    // Flips the send-with-default-message flag on every wired snapshot component of the given kind.
    // The flag lives on the component, not here — the chat window's switch and the canvas context menu
    // are two views of one field, so nothing has to be reconciled and the new value simply comes back
    // on the next state push. All wired tools are set, since the last one collected is the one that wins.
    private void SetSnapshotSendsMessage<T>(bool on)
        where T : SnapshotToolComponentBase
    {
        foreach (T snapshot in Owners<T>())
        {
            snapshot.SetSendWithMessage(on);
        }
    }

    // Sets the message that rides with one kind of snapshot's capture. The text lives on the tool
    // component for the same reason its send-or-attach flag does — it is that tool's setting, and it is
    // saved with it. Every wired tool of the kind is set, matching the read (the last one wins).
    private void SetSnapshotMessage<T>(string? message)
        where T : SnapshotToolComponentBase
    {
        foreach (T snapshot in Owners<T>())
        {
            snapshot.SetMessageOverride(message);
        }

        ExpireSolution(true);
    }

    /// <summary>
    /// Sets the expose-signatures flag and re-solves so the change takes effect on the next minted
    /// Instructions. Called from the chat window on the UI thread.
    /// </summary>
    /// <param name="on">True to fold typed signatures in for every included component; false keeps the hybrid default (signatures for the curated common set only).</param>
    public void SetExposeSignatures(bool on)
    {
        foreach (ComponentCatalogGrounder grounder in Owners<ComponentCatalogGrounder>())
        {
            grounder.SetExposeSignatures(on);
        }

        ExpireSolution(true);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("System Prompt", "S", "The standing instructions, sent ahead of the conversation on every turn. Wire a System Prompt component here.", GH_ParamAccess.item, string.Empty);
        pManager.AddParameter(new Param_Signal(), "Prompt Signal", "PS", "What you say, recorded as your turn. Wire a Chat for a typed message, or a Construct Signal to fire a fixed prompt from a button.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "What the model should know about this document — the canvas, the units, which components exist. Wire as many grounders as you like; each adds its own section to the instructions, and the chat window's grounding panel narrows what is actually sent.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_HumanTool(), "Human Tools", "HT", "Buttons and abilities switched on in the chat window for you: attaching images, capturing a view, exporting the transcript. The model is never told about these.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Response Signal", "RS", "What the model said, recorded as its turn. Wire an LLM Call's Success Signal here.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Feedback Signal", "FS", "Anything sent back for the model to act on — a validation error, a report, a retry instruction — recorded as though you had said it. Wire any number of Feedback Collectors straight in.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "LLM Tool Signal", "TS", "Tool traffic on its way back from a Router: the model's request and the answer it got, each recorded as the turn it belongs to.", GH_ParamAccess.list);

        pManager[InPromptSignal].Optional = true;
        pManager[InResponseSignal].Optional = true;
        pManager[InFeedbackSignal].Optional = true;
        pManager[InLlmToolSignal].Optional = true;
        pManager[InGrounding].Optional = true;
        pManager[InHumanTools].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "Sig", "Fires whenever your side of the conversation gains a turn, carrying the instructions and the whole history with it. Wire into an LLM Call — or into a compaction component first, if the conversation is outgrowing the model's context. Recording the model's own reply does not fire it, so the loop cannot run away.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(menu, "Save Conversation", OnSaveConversation);
        Menu_AppendItem(menu, "Load Conversation", OnLoadConversation);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string systemPrompt = string.Empty;

        DA.GetData(InSystemPrompt, ref systemPrompt);

        // Read the wired grounding and human tools every solve so the latch (and the chat UI) sees
        // the live state. Cheap: this is just projecting goo references already on the wire.
        ReadGroundingInputs(DA);
        ReadHumanToolInputs(DA);

        if (_legacyPending)
        {
            ScheduleLegacyMigration();
        }

        // Observe every solve, even mid-run: events arriving while busy wait, latched on
        // their wires, and are serviced after the latch.
        ObserveSignalInputs(DA, InPromptSignal, InResponseSignal, InFeedbackSignal, InLlmToolSignal);

        if (_doLatch)
        {
            // LATCH PASS — runs only from our own scheduled callback, after the visible delay.
            _doLatch = false;

            switch (_pendingOutcome)
            {
                case RecordOutcome.UserTurn:
                    // The minted signal carries the full Instructions: the trigger IS the data. The
                    // tools wired in as grounding ride on Instructions.Tools so the LLM Call advertises
                    // them to the model without a separate wire.
                    LatchSuccess(
                        _pendingUserText,
                        instructions: new Instructions(BuildGroundedSystemPrompt(systemPrompt), _conversation) { Tools = SelectedTools() });
                    break;
                case RecordOutcome.AssistantTurn:
                    // Quiet success: an LLM Call wired off the outgoing signal must not
                    // re-fire after its own response is recorded.
                    LatchSuccess(string.Empty, emitSignal: false);
                    break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Signal received but there was nothing new to record.");
                    LatchFailure(string.Empty, emitSignal: false);
                    break;
            }

            if (HasUnconsumedSignals(InPromptSignal, InResponseSignal, InFeedbackSignal, InLlmToolSignal))
            {
                // Events arrived mid-run; nothing was dropped. One follow-up solve
                // services them in sequence order.
                ScheduleStateSolve(1, () => { });
            }

            EmitSignal(DA, OutSignal, SuccessSignal);
            return;
        }

        if (State != SolveState.Active)
        {
            // Consuming in global sequence order is the ordering guarantee: feedback is
            // always minted after the response that provoked it, so even when both land
            // in this one solve the assistant turn is recorded first.
            IReadOnlyList<ConsumedSignal> consumed =
                ConsumeAllSignals(InPromptSignal, InResponseSignal, InFeedbackSignal, InLlmToolSignal);

            if (consumed.Count > 0)
            {
                EnterActive();

                var events = new List<RecordEvent>(consumed.Count);
                foreach (ConsumedSignal item in consumed)
                {
                    if (TryMapKind(item.ParamIndex, out RecordedTurnKind kind))
                    {
                        events.Add(new RecordEvent(kind, item.Signal));
                    }
                }

                RecordResult result = ConversationLogBuilder.Record(_conversation, events);
                _conversation = result.Conversation;
                _pendingOutcome = result.Outcome;
                _pendingUserText = result.UserTraceText;

                foreach (string warning in result.Warnings)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                }

                // A feedback turn means the model is iterating on the canvas; without a Canvas State
                // grounder it cannot see the current graph or a fresh patch base, so corrections are
                // guesswork. Nudge the graph author toward wiring one.
                if (!_hasCanvasStateGrounding && events.Any(e => e.Kind == RecordedTurnKind.Feedback))
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        "No Canvas State grounder is wired into Grounding: the model cannot see the current canvas, so patch bases may go stale during feedback loops.");
                }

                ScheduleStateSolve(SolveDelayMs, () => _doLatch = true);
                EmitSignal(DA, OutSignal, SuccessSignal);
                return;
            }
        }

        // Idle solve — re-emit the latched signal.
        EmitSignal(DA, OutSignal, SuccessSignal);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads nothing of its own. Every setting the chat window edits now lives on the component that
    /// owns it, so all this does is pick up the keys older files kept HERE and hand them on — see
    /// <see cref="ApplyLegacySettings"/> for why that cannot happen until a solve has run. The keys are
    /// never written again, so a file re-saved by this build has migrated for good.
    /// </remarks>
    public override bool Read(GH_IReader reader)
    {
        _legacySelection = ReadLegacyLeaves(reader);
        _legacyClusterSelection = ReadLegacyNames(reader, "ClusterSelectionSet", "ClusterNameCount", "ClusterName") is { } clusters
            ? ClusterSelection.FromNames(clusters)
            : null;
        _legacyToolsSelection = ReadLegacyNames(reader, "ToolsSelectionSet", "ToolNameCount", "ToolName") is { } tools
            ? ToolsSelection.FromNames(tools)
            : null;
        _legacyUnitsOverride = ReadLegacyString(reader, "UnitsOverrideSet", "UnitsOverride");
        _legacySnapshotMessage = ReadLegacyString(reader, "SnapshotMessageSet", "SnapshotMessage");
        _legacyViewSnapshotMessage = ReadLegacyString(reader, "ViewSnapshotMessageSet", "ViewSnapshotMessage");
        _legacyExposeSignatures = reader.ItemExists("ExposeComponentSignatures")
            && reader.GetBoolean("ExposeComponentSignatures");

        _legacyPending = _legacySelection is not null
            || _legacyClusterSelection is not null
            || _legacyToolsSelection is not null
            || _legacyUnitsOverride is not null
            || _legacySnapshotMessage is not null
            || _legacyViewSnapshotMessage is not null
            || _legacyExposeSignatures;

        return base.Read(reader);
    }

    // The component-catalog selection as older files stored it: a set flag, a count, and a
    // category/sub-category pair per leaf.
    private static GroundingSelection? ReadLegacyLeaves(GH_IReader reader)
    {
        if (!reader.ItemExists("GroundingSelectionSet") || !reader.GetBoolean("GroundingSelectionSet"))
        {
            return null;
        }

        int count = reader.ItemExists("GroundingLeafCount") ? reader.GetInt32("GroundingLeafCount") : 0;
        var leaves = new List<(string, string)>(count);
        for (int i = 0; i < count; i++)
        {
            leaves.Add((reader.GetString("GroundingLeafCategory", i), reader.GetString("GroundingLeafSubCategory", i)));
        }

        return GroundingSelection.FromLeaves(leaves);
    }

    // A name list as older files stored it. Returns null when the set flag says it was never
    // configured, which is not the same as an empty list (that means "include nothing").
    private static IReadOnlyList<string>? ReadLegacyNames(GH_IReader reader, string setKey, string countKey, string itemKey)
    {
        if (!reader.ItemExists(setKey) || !reader.GetBoolean(setKey))
        {
            return null;
        }

        int count = reader.ItemExists(countKey) ? reader.GetInt32(countKey) : 0;
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            names.Add(reader.GetString(itemKey, i));
        }

        return names;
    }

    // An optional string as older files stored it.
    private static string? ReadLegacyString(GH_IReader reader, string setKey, string valueKey) =>
        reader.ItemExists(setKey) && reader.GetBoolean(setKey) && reader.ItemExists(valueKey)
            ? reader.GetString(valueKey)
            : null;

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        // Conversation state resets. The settings are configuration, not conversation — and they do
        // not live here at all any more, so Clear cannot touch them even by accident.
        _conversation = Conversation.Empty;
        _doLatch = false;
        _pendingOutcome = RecordOutcome.Nothing;
        _pendingUserText = string.Empty;
    }

    // The wired components this log holds settings on behalf of, type-filtered — every getter and
    // setter goes through here. Last-one-wins on read, all-of-them on write, the same discipline the
    // live grounding caches use.
    //
    // Walked LIVE on each call rather than cached on solve, and deliberately so: the wires are the
    // truth, a rewire announces nothing, and callers arrive at moments when no solve has happened yet
    // — the chat window's tick between solves, and a ghjson import restoring a selection onto
    // components it has only just placed. A cache would be empty or stale in exactly those moments.
    // The walk is a few wires deep; the tick reads it a dozen times per 0.15 s and it does not show.
    private IEnumerable<T> Owners<T>()
    {
        var owners = new List<IGH_DocumentObject>();
        CollectSourceComponents(Params.Input[InGrounding], owners, 0);
        CollectSourceComponents(Params.Input[InHumanTools], owners, 0);
        return owners.OfType<T>();
    }

    // Collects the components feeding an input, stepping THROUGH any bare parameter used as a relay:
    // a generic param dropped in to tidy a long wire is common, and it would otherwise hide the
    // grounder that owns the settings behind it. Depth-limited so a relay loop cannot spin, and
    // de-duplicated by reference so a diamond of relays reports each component once.
    private static void CollectSourceComponents(IGH_Param input, List<IGH_DocumentObject> found, int depth)
    {
        if (depth > MaxRelayDepth)
        {
            return;
        }

        foreach (IGH_Param source in input.Sources)
        {
            IGH_DocumentObject? owner = source.Attributes?.GetTopLevel?.DocObject;
            if (owner is IGH_Component component)
            {
                if (!found.Contains(component))
                {
                    found.Add(component);
                }

                continue;
            }

            CollectSourceComponents(source, found, depth + 1);
        }
    }

    // The tool nodes a wired Tools Present grounder can see — every node dispatched by a Router,
    // advertised or parked. Read live off the grounder rather than cached, since a node can be wired,
    // parked or deleted without this component solving.
    private IReadOnlyList<LlmToolComponentBase> ScannedToolNodes() =>
        Owners<ToolsInUse>().SelectMany(t => t.ScannedTools).Distinct().ToList();

    // Hands the settings read out of an older file to the components that own them now, on the next
    // solution rather than during this solve.
    //
    // Deferred because applying them touches other components — and because waiting for a solve is the
    // whole point: the owners are only known once the wires have been read. Routed through the base's
    // scheduling funnel rather than posting to the document directly, because Grasshopper keeps ONE
    // document schedule and flushes every pending callback at the next solution: a raw post here would
    // race whatever the latch has queued, and one of the two would be lost.
    private void ScheduleLegacyMigration()
    {
        _legacyPending = false;
        ScheduleStateSolve(1, ApplyLegacySettings);
    }

    // Applies each legacy setting to the component that owns it now. Only values that were actually
    // configured are pushed: a legacy null means "never configured", which is already every
    // component's default, so pushing it could only overwrite a setting the component itself carries.
    private void ApplyLegacySettings()
    {
        if (_legacySelection is not null)
        {
            foreach (ComponentCatalogGrounder grounder in Owners<ComponentCatalogGrounder>())
            {
                grounder.SetSelection(_legacySelection);
            }
        }

        if (_legacyExposeSignatures)
        {
            foreach (ComponentCatalogGrounder grounder in Owners<ComponentCatalogGrounder>())
            {
                grounder.SetExposeSignatures(true);
            }
        }

        if (_legacyClusterSelection is not null)
        {
            foreach (ClusterGrounder grounder in Owners<ClusterGrounder>())
            {
                grounder.SetSelection(_legacyClusterSelection);
            }
        }

        if (_legacyUnitsOverride is not null)
        {
            foreach (DocumentUnitsGrounder grounder in Owners<DocumentUnitsGrounder>())
            {
                grounder.SetOverride(_legacyUnitsOverride);
            }
        }

        if (_legacySnapshotMessage is not null)
        {
            foreach (GeometrySnapshot snapshot in Owners<GeometrySnapshot>())
            {
                snapshot.SetMessageOverride(_legacySnapshotMessage);
            }
        }

        if (_legacyViewSnapshotMessage is not null)
        {
            foreach (ViewSnapshot snapshot in Owners<ViewSnapshot>())
            {
                snapshot.SetMessageOverride(_legacyViewSnapshotMessage);
            }
        }

        if (_legacyToolsSelection is { } tools)
        {
            // RestoreAdvertise, not SetAdvertise: this runs inside a scheduled callback, where asking a
            // node for a solution of its own is exactly what must not happen. The flag is folded into
            // the Tools Present signature, so the grounder picks the change up on its own.
            foreach (LlmToolComponentBase tool in ScannedToolNodes())
            {
                tool.RestoreAdvertise(tools.Includes(tool.AdvertisedDefinition.Name));
            }
        }

        _legacySelection = null;
        _legacyClusterSelection = null;
        _legacyToolsSelection = null;
        _legacyUnitsOverride = null;
        _legacySnapshotMessage = null;
        _legacyViewSnapshotMessage = null;
        _legacyExposeSignatures = false;
    }

    // Reads the Grounding input and caches it for the latch and the chat UI. The live catalog is the
    // merged entries of every wired ComponentCatalogGrounding, so a UI tree can offer all tabs/panels.
    private void ReadGroundingInputs(IGH_DataAccess da)
    {
        var goos = new List<GH_Grounding>();
        da.GetDataList(InGrounding, goos);

        _liveGroundings = goos
            .Where(g => g?.Value is not null)
            .Select(g => g.Value)
            .ToList();

        var mergedEntries = _liveGroundings
            .OfType<ComponentCatalogGrounding>()
            .Where(g => g.Catalog is not null)
            .SelectMany(g => g.Catalog.Entries)
            .ToList();

        _liveCatalog = mergedEntries.Count > 0 ? new ComponentCatalog(mergedEntries) : null;

        var mergedClusters = _liveGroundings
            .OfType<ClusterCatalogGrounding>()
            .Where(g => g.Catalog is not null)
            .SelectMany(g => g.Catalog.Entries)
            .ToList();

        _liveClusterCatalog = mergedClusters.Count > 0 ? new ClusterCatalog(mergedClusters) : null;

        // A document-units grounding carries a single value; if several are wired, the last one wins.
        _liveUnitsGrounding = _liveGroundings.OfType<DocumentUnitsGrounding>().LastOrDefault();

        // Whether a Tools Present grounder is wired at all, which is not the same question as whether
        // anything is advertised: parking every tool leaves the grounding in place carrying nothing.
        _hasToolsGrounding = _liveGroundings.OfType<ToolsGrounding>().Any();

        // Tool definitions carried by every wired ToolsGrounding, merged (deduped by name — one Tools
        // Present grounder is the norm, but two wired ones must not advertise a tool twice). Parked
        // tools are already absent: the grounder reports only what it advertises.
        _liveTools = _liveGroundings
            .OfType<ToolsGrounding>()
            .SelectMany(g => g.Tools ?? Array.Empty<LlmToolDefinition>())
            .Where(t => t is not null)
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        // Referenced Rhino geometry and python functions, cached so every grounding kind has a live
        // read for the chat UI's grounding pages. The referenced-geometry list comes straight from
        // the document (the params themselves are the registry), gated on a canvas-state grounding
        // being wired — without it the model cannot see those params anyway.
        _hasCanvasStateGrounding = _liveGroundings.OfType<CanvasStateGrounding>().Any();

        // A group-scoped grounder narrows the model's whole canvas view; if one is wired at all it
        // wins over a plain Canvas State (the restrictive intent is the deliberate one), and the
        // fresh re-export at mint time follows the same frame.
        _groupScopedCanvasGrounding = _liveGroundings.OfType<CanvasStateGrounding>().Any(g => g.GroupScoped);
        _liveReferencedGeometry = _hasCanvasStateGrounding
            ? Generation.CanvasRhinoReferences.Collect(PhyDocuments.Host(this))
                .Select(r => new ReferencedGeometryInput(r.Name, r.TypeName))
                .ToList()
            : Array.Empty<ReferencedGeometryInput>();

        _livePythonFunctions = _liveGroundings.OfType<PythonFunctionGrounding>().ToList();
    }

    // Reads the Human Tools input and caches which chat-window affordances are enabled. Human tools
    // never touch the system prompt — their presence on this input is the whole contract.
    private void ReadHumanToolInputs(IGH_DataAccess da)
    {
        var goos = new List<GH_HumanTool>();
        da.GetDataList(InHumanTools, goos);

        List<HumanTool> tools = goos
            .Where(g => g?.Value is not null)
            .Select(g => g.Value)
            .ToList();

        // A Geometry Snapshot tool carries a single default message; last one wins if several are
        // wired (same discipline as document units).
        _liveSnapshotTool = tools.OfType<GeometrySnapshotTool>().LastOrDefault();
        _liveViewSnapshotTool = tools.OfType<ViewSnapshotTool>().LastOrDefault();
        _hasAddImageTool = tools.OfType<AddImageTool>().Any();

        // Marker tools: presence is the whole contract — each just lights a button in the chat
        // window's header (a transcript export, a door onto the session's signal trace).
        _hasExportTool = tools.OfType<ExportConversationTool>().Any();
        _hasSignalTraceTool = tools.OfType<SignalTraceTool>().Any();
        _hasImageMarkUpTool = tools.OfType<ImageMarkUpTool>().Any();
    }

    // The tools advertised to the model. No narrowing happens here any more: each tool node carries
    // its own advertise switch and the wired Tools Present grounder reports only the ones that are on,
    // so what arrives on the wire IS the advertised set.
    private IReadOnlyList<LlmToolDefinition> SelectedTools() => _liveTools;

    // Folds the wired groundings into the system prompt, applying the component selection to
    // component-catalog groundings and the cluster selection to cluster groundings (other kinds
    // pass through untouched). The result is segmented rather than flat so the provider can cache
    // the stable prefix; the wire order of the groundings does not matter, because SystemPrompt
    // sorts the volatile canvas state behind everything invariant on construction.
    private Core.ConvoInstruct.SystemPrompt BuildGroundedSystemPrompt(string systemPrompt)
    {
        if (_liveGroundings.Count == 0)
        {
            return systemPrompt;
        }

        var mapped = new List<Grounding>();
        bool toolsAdded = false;
        foreach (Grounding g in _liveGroundings)
        {
            switch (g)
            {
                case ComponentCatalogGrounding cc:
                    // Signature enrichment is bounded to the filtered set — the only place the
                    // catalog ever instantiates components, and each type is introspected once per
                    // session (cached by GUID). Default is HYBRID: the curated common set always
                    // carries signatures (the model's worst failure mode is guessing a common
                    // component's parameter order) while the long tail stays names-only; the user's
                    // expose-signatures toggle widens enrichment to the whole filtered catalog.
                    // No explicit selection = the native-only default: plug-in components stay out
                    // of the prompt until the user checks their tabs in the grounding selector.
                    ComponentCatalog filteredCatalog =
                        cc.Catalog.Filtered(GroundingSelectionOrNull ?? cc.Catalog.NativeSelection());
                    mapped.Add(new ComponentCatalogGrounding(
                        ExposeComponentSignatures
                            ? ComponentSignatureProvider.EnrichWithSignatures(filteredCatalog)
                            : ComponentSignatureProvider.EnrichWithSignatures(filteredCatalog, CommonComponents.Names),
                        IncludeSignatures: true));
                    break;
                case ClusterCatalogGrounding cl:
                    mapped.Add(new ClusterCatalogGrounding(cl.Catalog.Filtered(ClusterSelectionOrNull)));
                    break;
                case DocumentUnitsGrounding du:
                    mapped.Add(UnitsOverrideOrNull is { } ov ? new DocumentUnitsGrounding(ov) : du);
                    break;
                case ToolsGrounding:
                    // Collapse every wired tools grounding into ONE section listing the SELECTED tools
                    // (exactly what is advertised to the model), so the prompt constrains it to that set.
                    if (!toolsAdded)
                    {
                        mapped.Add(new ToolsGrounding(SelectedTools()));
                        toolsAdded = true;
                    }

                    break;
                case CanvasStateGrounding:
                    // The wire value is only as fresh as the grounder's async, debounced,
                    // import-skipping re-solve, so at inference time it can lag the true canvas by a
                    // solution — two after a placement, since the import's own SolutionEnd is skipped.
                    // Re-export the live canvas here (synchronously, the same code path the ghpatch
                    // base resolves against) so the model always sees the current state + checksum and
                    // its patch base matches on the first try, even when the user edited between turns.
                    mapped.Add(FreshCanvasStateGrounding());
                    break;
                default:
                    mapped.Add(g);
                    break;
            }
        }

        return GroundingComposer.Append(systemPrompt, mapped);
    }

    // Snapshots the live canvas so the folded grounding matches exactly what a ghpatch will apply
    // against at this instant. Falls back to an empty grounding when no document is exportable — the
    // same as an empty canvas, which drops the model back to full-document generation.
    private Grounding FreshCanvasStateGrounding()
    {
        GH_Document? doc = PhyDocuments.Host(this);

        // Record which frame the model is being shown, so every guardrail that hands it a fresh
        // base checksum (GhJsonBridge.CurrentBaseChecksum) reports the same frame.
        HarnessComponent? harness = PhyDocuments.Harness(this);
        GhJsonBridge.RecordActiveFrame(doc, harness, _groupScopedCanvasGrounding);

        GhJsonBridge.CanvasStateSnapshot? snapshot =
            GhJsonBridge.TryExportCanvasState(doc, _groupScopedCanvasGrounding, harness);
        if (snapshot is null)
        {
            return new CanvasStateGrounding(string.Empty, string.Empty, 0);
        }

        // One-shot: set exactly on the turn after a placement that lost the model's authored ids,
        // so the model reads this canvas state's numbering as authoritative instead of patching
        // against ids only it remembers.
        string? numberingNote = GhJsonBridge.ConsumePlacementNumberingLoss(doc)
            ? "IMPORTANT: your last placement could NOT keep the component ids you authored — they were "
              + "renumbered. The ids in THIS canvas state are authoritative; never use ids from your "
              + "own earlier submission."
            : null;

        return new CanvasStateGrounding(snapshot.Json, snapshot.Checksum, snapshot.ComponentCount, GhJsonBridge.CountModelPlaced(doc))
        {
            NumberingNote = numberingNote,
            GroupScoped = _groupScopedCanvasGrounding,
        };
    }

    // Maps a Signal input index to the turn kind it designates. Turn type comes from input
    // identity — never from conversation parity.
    private static bool TryMapKind(int paramIndex, out RecordedTurnKind kind)
    {
        switch (paramIndex)
        {
            case InPromptSignal: kind = RecordedTurnKind.Prompt; return true;
            case InResponseSignal: kind = RecordedTurnKind.Response; return true;
            case InFeedbackSignal: kind = RecordedTurnKind.Feedback; return true;
            case InLlmToolSignal: kind = RecordedTurnKind.Tool; return true;
            default: kind = default; return false;
        }
    }

    private void OnSaveConversation(object? sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Save Conversation is not yet implemented.");
        ExpireSolution(true);
    }

    private void OnLoadConversation(object? sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Load Conversation is not yet implemented.");
        ExpireSolution(true);
    }
}
